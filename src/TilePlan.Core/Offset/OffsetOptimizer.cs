using TilePlan.Core.Geometry;
using TilePlan.Core.Layout;
using TilePlan.Core.Tiles;

namespace TilePlan.Core.Offset;

public sealed record OffsetResult(Lattice Lattice, LayoutResult Layout, OffsetReport Report);

/// <summary>
/// 自動調整磚格起始偏移 (oᵤ, oᵥ)。評分依序比較（前者優先）：
/// <list type="number">
/// <item>可達成的牆：切條 ≥ 門檻（不足量的總和越小越好）</item>
/// <item>互相面對的牆：切條寬度越對稱越好</item>
/// <item>跨牆的磚越少越好（切割次數少）</item>
/// <item>做不到的牆：最小切條越寬越好</item>
/// <item>偏移越接近基準線越好</item>
/// </list>
/// 搜尋：粗網格 + 沿 u、v（斜貼另加兩條對角方向）的線搜尋，候選點包含解析解（對稱點、剛好等於門檻的位置）。
/// </summary>
public static class OffsetOptimizer
{
    private const int CoarseGrid = 8;
    private const int LineSamples = 48;
    private const int RefineSamples = 16;
    private const int MaxRounds = 4;

    public static OffsetResult Optimize(Polygon room, Region region, Lattice template)
    {
        var walls = Wall.Of(room, template);
        var search = new Search(walls, template);
        var best = search.Run(feasibleOnly: null);

        var infeasible = walls.Where(w => search.BestSeen(w) < w.Threshold - 1e-6).Select(w => w.Index).ToHashSet();
        if (infeasible.Count > 0)
            best = search.Run(infeasible, best);

        var lattice = template.WithOffset(best.U, best.V);
        var layout = LayoutEngine.Layout(region, lattice);
        var report = BuildReport(room, layout, walls, w => search.BestSeen(w));
        return new OffsetResult(lattice, layout, report);
    }

    /// <summary>手動偏移時，同樣以精確排版產生切條報告。</summary>
    public static OffsetReport Report(Polygon room, LayoutResult layout) =>
        BuildReport(room, layout, Wall.Of(room, layout.Lattice), bestSeen: null);

    private sealed record Candidate(double U, double V, Score Score);

    private readonly record struct Score(double Shortfall, double Asymmetry, int Straddling, double WorstInfeasible, double AnchorDistance)
    {
        public bool BetterThan(Score o)
        {
            if (Math.Abs(Shortfall - o.Shortfall) > 1e-6) return Shortfall < o.Shortfall;
            if (Math.Abs(Asymmetry - o.Asymmetry) > 0.05) return Asymmetry < o.Asymmetry;
            if (Straddling != o.Straddling) return Straddling < o.Straddling;
            if (Math.Abs(WorstInfeasible - o.WorstInfeasible) > 0.05) return WorstInfeasible > o.WorstInfeasible;
            return AnchorDistance < o.AnchorDistance - 1e-9;
        }
    }

    private sealed class Search(List<Wall> walls, Lattice template)
    {
        private readonly Dictionary<int, double> _bestSeen = [];
        private readonly List<(Wall A, Wall B)> _facing = FacingPairs(walls);
        private HashSet<int>? _infeasible;

        public double BestSeen(Wall w) => _bestSeen.TryGetValue(w.Index, out var v) ? v : double.PositiveInfinity;

        public Candidate Run(HashSet<int>? feasibleOnly, Candidate? start = null)
        {
            _infeasible = feasibleOnly;
            double mu = template.Mu, mv = template.Mv;
            var best = Eval(0, 0);
            if (start is not null) best = Pick(best, Eval(start.U, start.V));

            for (int a = 0; a < CoarseGrid; a++)
                for (int b = 0; b < CoarseGrid; b++)
                    best = Pick(best, Eval(mu * a / CoarseGrid, mv * b / CoarseGrid));

            var directions = new List<Vec2> { new(1, 0), new(0, 1) };
            if (walls.Any(w => w.Axis == WallAxis.Oblique)) { directions.Add(new(1, 1)); directions.Add(new(1, -1)); }
            var analyticU = AnalyticCandidates(WallAxis.AlongV);
            var analyticV = AnalyticCandidates(WallAxis.AlongU);

            for (int round = 0; round < MaxRounds; round++)
            {
                var before = best;
                foreach (var dir in directions)
                {
                    double period = dir.Y == 0 ? mu : dir.X == 0 ? mv : Math.Max(mu, mv);
                    var ts = Enumerable.Range(0, LineSamples).Select(k => period * k / LineSamples).ToList();
                    if (dir == new Vec2(1, 0)) ts.AddRange(analyticU.Select(u => u - best.U));
                    if (dir == new Vec2(0, 1)) ts.AddRange(analyticV.Select(v => v - best.V));

                    var origin = best;
                    double bestT = 0;
                    foreach (var t in ts)
                    {
                        var c = Eval(origin.U + dir.X * t, origin.V + dir.Y * t);
                        if (c.Score.BetterThan(best.Score)) { best = c; bestT = t; }
                    }
                    double step = period / LineSamples;
                    for (int k = -RefineSamples / 2; k <= RefineSamples / 2; k++)
                    {
                        double t = bestT + step * k / (RefineSamples / 2);
                        best = Pick(best, Eval(origin.U + dir.X * t, origin.V + dir.Y * t));
                    }
                }
                if (best == before) break;
            }
            return best;
        }

        private static Candidate Pick(Candidate a, Candidate b) => b.Score.BetterThan(a.Score) ? b : a;

        private Candidate Eval(double u, double v)
        {
            u = Lattice.Mod(u, template.Mu);
            v = Lattice.Mod(v, template.Mv);
            var lattice = template.WithOffset(u, v);
            var widths = new Dictionary<int, double>();
            double shortfall = 0, worstInfeasible = double.PositiveInfinity;
            int straddling = 0;

            for (int k = 0; k < walls.Count; k++)
            {
                var w = walls[k];
                var metric = EdgeStripEvaluator.Evaluate(w, walls[(k + walls.Count - 1) % walls.Count], walls[(k + 1) % walls.Count], lattice);
                straddling += metric.Straddling;
                double width = metric.MinWidth ?? double.PositiveInfinity;
                widths[w.Index] = Math.Min(width, FullDepth(w));
                if (!_bestSeen.TryGetValue(w.Index, out var seen) || width > seen) _bestSeen[w.Index] = width;

                if (_infeasible is not null && _infeasible.Contains(w.Index))
                    worstInfeasible = Math.Min(worstInfeasible, width);
                else
                    shortfall += Math.Max(0, w.Threshold - width);
            }

            double asymmetry = _facing.Sum(p => Math.Abs(widths[p.A.Index] - widths[p.B.Index]));
            double du = Math.Min(u, template.Mu - u), dv = Math.Min(v, template.Mv - v);
            return new Candidate(u, v, new Score(shortfall, asymmetry, straddling, worstInfeasible, du + dv));
        }

        /// <summary>沒有切割片的牆，切條寬度視為一整片磚（用於對稱比較）。</summary>
        private double FullDepth(Wall w) => w.Axis switch
        {
            WallAxis.AlongU => template.TileV,
            WallAxis.AlongV => template.TileU,
            _ => Math.Min(template.TileU, template.TileV),
        };

        /// <summary>
        /// 平行於座標軸的牆，可以直接算出「切條剛好等於門檻」、「磚邊貼齊牆」與「兩面對牆對稱」的偏移。
        /// AlongV 的牆決定 oᵤ（含交丁各排的位移），AlongU 的牆決定 oᵥ。
        /// </summary>
        private List<double> AnalyticCandidates(WallAxis axis)
        {
            var result = new List<double>();
            bool forU = axis == WallAxis.AlongV;
            double tile = forU ? template.TileU : template.TileV;
            double joint = template.Joint;
            var shifts = forU ? Enumerable.Range(0, template.BondPeriod).Select(template.RowShift).ToList() : [0.0];
            var aligned = walls.Where(w => w.Axis == axis).ToList();

            double Pos(Wall w) => forU ? (w.LatticeA.X + w.LatticeB.X) / 2 : (w.LatticeA.Y + w.LatticeB.Y) / 2;
            bool InwardPositive(Wall w) => (forU ? w.LatticeInward.X : w.LatticeInward.Y) > 0;

            foreach (var shift in shifts)
            {
                foreach (var w in aligned)
                {
                    double c = Pos(w);
                    if (InwardPositive(w))
                    {
                        result.Add(c - shift);                          // 磚邊貼齊牆
                        result.Add(c + w.Threshold - tile - shift);     // 切條剛好等於門檻
                    }
                    else
                    {
                        result.Add(c - tile - shift);
                        result.Add(c - w.Threshold - shift);
                    }
                }
                foreach (var (a, b) in _facing.Where(p => p.A.Axis == axis))
                {
                    double m = (Pos(a) + Pos(b)) / 2;
                    result.Add(m - tile / 2 - shift);                   // 磚中心在正中間
                    result.Add(m - tile - joint / 2 - shift);           // 縫在正中間
                }
            }
            return result;
        }

        private static List<(Wall, Wall)> FacingPairs(List<Wall> walls)
        {
            var pairs = new List<(Wall, Wall)>();
            for (int i = 0; i < walls.Count; i++)
                for (int j = i + 1; j < walls.Count; j++)
                    if (walls[i].Faces(walls[j])) pairs.Add((walls[i], walls[j]));
            return pairs;
        }
    }

    private static OffsetReport BuildReport(Polygon room, LayoutResult layout, List<Wall> walls, Func<Wall, double>? bestSeen)
    {
        var strips = walls.Select(w => new WallStrip(w.Index, w.Name, w.Threshold, ActualMinWidth(w, walls, layout), w.Axis == WallAxis.Oblique, w.AngleToGridDeg)).ToList();
        var reasons = new List<string>();
        var violations = strips.Where(s => !s.Satisfied).ToList();
        if (violations.Count > 0)
        {
            double min = violations.Min(v => v.MinWidth!.Value);
            reasons.Add($"有 {violations.Count} 面牆的切條小於磚寬的 1/3，實際最小寬度 {min:0.0} cm。");
            foreach (var v in violations)
                reasons.Add(Explain(v, walls, layout.Lattice, bestSeen));
        }
        return new OffsetReport(layout.Lattice.OffsetU, layout.Lattice.OffsetV, strips, reasons);
    }

    private static string Explain(WallStrip strip, List<Wall> walls, Lattice lattice, Func<Wall, double>? bestSeen)
    {
        var wall = walls.First(w => w.Index == strip.Index);
        string tail = $"（門檻 {strip.Threshold:0.0} cm，實際最小 {strip.MinWidth:0.0} cm）";

        if (lattice.BondPeriod == 3 && wall.Axis == WallAxis.AlongV)
            return $"交丁 1/3：牆 {strip.Name} 每三排的切條寬度各差 1/3 磚長，無法同時都 ≥ 門檻{tail}。";
        bool feasibleAlone = bestSeen is not null && bestSeen(wall) >= wall.Threshold - 1e-6;
        if (wall.Axis == WallAxis.Oblique && !feasibleAlone)
            return $"斜牆 {strip.Name} 與磚縫夾 {strip.AngleToGridDeg:0.#}°，沿牆必然出現三角形小片{tail}。";
        if (feasibleAlone)
        {
            var others = walls.Where(w => w.Index != wall.Index && w.IsParallelTo(wall)).Select(w => w.Name).ToList();
            if (others.Count > 0)
                return $"牆 {strip.Name} 與 {string.Join("、", others)} 對偏移的要求互相衝突，無法同時滿足{tail}。";
        }
        if (bestSeen is null)
            return $"牆 {strip.Name} 的切條小於門檻，可改用自動偏移{tail}。";
        return $"牆 {strip.Name} 附近受轉角或不鋪設區影響，切條小於門檻{tail}。";
    }

    /// <summary>
    /// 以精確排版的切割片計算：與牆共用一段邊的切片，其頂點到牆的最大距離。
    /// 同時貼著另一面不平行牆的轉角片，形狀由兩面牆共同決定：牆上有其他切片時不計入，整面牆都是轉角片時才用它們。
    /// </summary>
    private static double? ActualMinWidth(Wall wall, List<Wall> walls, LayoutResult layout)
    {
        double? min = null, cornerMin = null;
        var others = walls.Where(o => o != wall && !o.IsParallelTo(wall)).ToList();
        foreach (var piece in layout.CutPieces)
        {
            foreach (var poly in piece.Shape.Polygons)
            {
                if (!poly.IsCounterClockwise || !TouchesWall(poly, wall)) continue;
                double depth = poly.Vertices.Max(v => (v - wall.A).Dot(wall.Inward));
                if (others.Any(o => TouchesWall(poly, o)))
                    cornerMin = cornerMin is null ? depth : Math.Min(cornerMin.Value, depth);
                else if (min is null || depth < min)
                    min = depth;
            }
        }
        return min ?? cornerMin;
    }

    private static bool TouchesWall(Polygon poly, Wall wall)
    {
        const double onLine = 1e-3, minContact = 1e-2;
        var dir = (wall.B - wall.A) / wall.Length;
        foreach (var (p, q) in poly.Edges())
        {
            if (Math.Abs((p - wall.A).Dot(wall.Inward)) > onLine || Math.Abs((q - wall.A).Dot(wall.Inward)) > onLine) continue;
            double t0 = (p - wall.A).Dot(dir), t1 = (q - wall.A).Dot(dir);
            if (Math.Min(Math.Max(t0, t1), wall.Length) - Math.Max(Math.Min(t0, t1), 0) > minContact) return true;
        }
        return false;
    }
}
