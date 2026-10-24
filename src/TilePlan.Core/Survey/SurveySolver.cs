using TilePlan.Core.Geometry;

namespace TilePlan.Core.Survey;

/// <summary>
/// 由牆長與對角線推算房間形狀。三階段：
/// 1. 剛性分析：量測不足時拒絕求解，並建議要補量哪幾條（不假設直角）。
/// 2. 逐點三邊定位：每個新頂點由兩個已知頂點的距離求兩圓交點；鏡像兩解都試（深度優先），
///    只保留「不自交、方向正確、對角線都在房間內」的形狀。
/// 3. Levenberg–Marquardt 最小平方修正，回報每條量測的殘差。
/// </summary>
public static class SurveySolver
{
    /// <summary>殘差超過此值（cm）時警告量測可能有誤。</summary>
    public const double ResidualWarning = 1.0;

    /// <summary>兩圓差一點點沒交到時，視為相切的容許量（cm）；超過才判定三角不等式不成立。</summary>
    private const double TangentTolerance = 1.0;

    /// <summary>不同解的頂點差距小於此值（cm）時視為同一解。</summary>
    private const double SameSolutionTolerance = 1.0;

    private const int MaxLeaves = 200_000;

    public static string Label(int i) => i < 26 ? ((char)('A' + i)).ToString() : $"V{i + 1}";

    public static string LabelPair(int i, int j) => $"{Label(i)}–{Label(j)}";

    private sealed record Bar(int From, int To, double Length);

    public static SurveyResult Solve(SurveyInput input)
    {
        int n = input.Walls.Count;
        var bars = BuildBars(input);
        var pairs = bars.Select(b => (b.From, b.To)).ToList();

        int missing = RigidityAnalyzer.RequiredRank(n) - RigidityAnalyzer.Rank(n, pairs);
        if (missing > 0)
        {
            var suggested = RigidityAnalyzer.SuggestDiagonals(n, pairs);
            return Fail(SurveyStatus.Underdetermined,
                $"缺少 {missing} 條量測，形狀無法唯一決定（只量牆長時，各角的角度未知，不能假設為直角）。" +
                $"建議再量對角線：{string.Join("、", suggested.Select(s => LabelPair(s.From, s.To)))}。",
                missing, suggested);
        }

        var order = PlacementOrder(n, bars);
        if (order is null)
            return Fail(SurveyStatus.Underdetermined,
                "量測足夠，但無法逐點推算形狀。請改量從同一個角出發的對角線（例如 A–C、A–D…）。", 0, []);

        var search = new Search(input, order);
        search.Run();

        if (search.Solutions.Count == 0)
            return Fail(SurveyStatus.Inconsistent,
                search.FirstConflict ?? "量測互相矛盾，找不到符合所有長度的房間形狀，請重新確認量測值。", 0, []);

        var distinct = new List<Vec2[]>();
        foreach (var raw in search.Solutions)
        {
            var refined = LeastSquares.Refine(raw, bars.Select(b => (b.From, b.To, b.Length)).ToList());
            var poly = new Polygon(refined);
            if (!IsValidShape(poly, input)) continue;
            if (distinct.All(d => MaxDistance(d, refined) > SameSolutionTolerance))
                distinct.Add(refined);
        }

        if (distinct.Count == 0)
            return Fail(SurveyStatus.Inconsistent, "量測互相矛盾，修正後的形狀會自交，請重新確認量測值。", 0, []);

        if (distinct.Count > 1)
        {
            var (i, j) = MostDiscriminatingPair(distinct[0], distinct[1], bars);
            return Fail(SurveyStatus.Ambiguous,
                $"有 {distinct.Count} 種形狀都符合目前的量測，請再量 {LabelPair(i, j)} 來區分。", 1, [(i, j)]);
        }

        var solution = distinct[0];
        var residuals = bars
            .Select(b => new MeasurementResidual(b.From, b.To, b.Length, solution[b.From].DistanceTo(solution[b.To]) - b.Length))
            .ToList();
        int redundancy = bars.Count - RigidityAnalyzer.RequiredRank(n);
        var uncertainty = Uncertainty.Of(solution, bars.Select(b => (b.From, b.To, b.Length)).ToList(), input);
        string message;
        if (redundancy == 0)
        {
            // 量測剛好足夠時殘差必然為 0：顯示「殘差 0.00 cm」只會給人錯誤的信心
            message = "形狀已解出。目前沒有多餘量測，無法檢查是否量錯。";
            if (uncertainty.Check is { } c)
                message += $"建議再量 {LabelPair(c.From, c.To)} 作為檢核：依捲尺精度推估約為 {c.Expected:0.0} ± {c.Range:0.0} cm，" +
                           "量到的值若超出這個範圍，很可能有量測量錯。";
        }
        else
        {
            var worst = residuals.MaxBy(r => Math.Abs(r.Residual))!;
            message = Math.Abs(worst.Residual) > ResidualWarning
                ? $"形狀已解出，但量測可能有誤：{LabelPair(worst.From, worst.To)} 的殘差為 {worst.Residual:+0.0;-0.0} cm，請複量。"
                : $"形狀已解出，{redundancy} 條多餘量測互相吻合（最大殘差 {Math.Abs(worst.Residual):0.0} cm）。";
        }

        return new SurveyResult(SurveyStatus.Solved, new Polygon(solution), message, 0, [], residuals)
        {
            Alternatives = AlternativeShapes(search.ExteriorDiagonalShapes, solution, bars, input),
            Redundancy = redundancy,
            VertexUncertainty = uncertainty.Vertices,
            CheckDiagonal = uncertainty.Check is { } check ? (check.From, check.To) : null,
        };
    }

    /// <summary>
    /// 只因「對角線穿牆」被排除的形狀。光看長度無法分辨使用者是否記錯對角線的角落
    /// （正確的 L 形輸入也會有這種形狀），所以不當成警告，而是列出來讓使用者對照現場。
    /// </summary>
    private static IReadOnlyList<Polygon> AlternativeShapes(List<Vec2[]> candidates, Vec2[] solution, List<Bar> bars, SurveyInput input)
    {
        var measured = bars.Select(b => (b.From, b.To, b.Length)).ToList();
        var result = new List<Vec2[]>();
        foreach (var raw in candidates)
        {
            var refined = LeastSquares.Refine(raw, measured);
            if (!IsSimpleWithOrientation(new Polygon(refined), input)) continue;
            if (MaxDistance(refined, solution) <= SameSolutionTolerance) continue;
            if (result.Any(r => MaxDistance(r, refined) <= SameSolutionTolerance)) continue;
            result.Add(refined);
        }
        return result.Select(r => new Polygon(r)).ToList();
    }

    private static SurveyResult Fail(SurveyStatus status, string message, int missing, IReadOnlyList<(int, int)> suggested) =>
        new(status, null, message, missing, suggested, []);

    private static List<Bar> BuildBars(SurveyInput input)
    {
        int n = input.Walls.Count;
        if (n < 3) throw new ArgumentException("至少需要 3 面牆。", nameof(input));

        var bars = new List<Bar>();
        for (int i = 0; i < n; i++)
        {
            if (!(input.Walls[i] > 0)) throw new ArgumentException($"牆 {LabelPair(i, (i + 1) % n)} 的長度必須大於 0。", nameof(input));
            bars.Add(new Bar(i, (i + 1) % n, input.Walls[i]));
        }
        foreach (var d in input.Diagonals)
        {
            if (d.From < 0 || d.From >= n || d.To < 0 || d.To >= n || d.From == d.To)
                throw new ArgumentException($"對角線 {d.From}–{d.To} 的頂點不存在（房間只有 {n} 個角）。", nameof(input));
            if (!(d.Length > 0)) throw new ArgumentException($"對角線 {LabelPair(d.From, d.To)} 的長度必須大於 0。", nameof(input));
            bars.Add(new Bar(d.From, d.To, d.Length));
        }
        return bars;
    }

    /// <summary>
    /// 決定放置順序：A 在原點、B 在 +x 軸，之後每次挑「與已放置頂點有最多量測」的頂點（至少 2 條）。
    /// </summary>
    private static List<Step>? PlacementOrder(int n, List<Bar> bars)
    {
        var placed = new List<int> { 0, 1 };
        var steps = new List<Step>();
        while (placed.Count < n)
        {
            Step? best = null;
            for (int v = 0; v < n; v++)
            {
                if (placed.Contains(v)) continue;
                var links = bars
                    .Where(b => (b.From == v && placed.Contains(b.To)) || (b.To == v && placed.Contains(b.From)))
                    .Select(b => (Other: b.From == v ? b.To : b.From, b.Length))
                    .OrderBy(l => placed.IndexOf(l.Other))
                    .ToList();
                if (links.Count >= 2 && (best is null || links.Count > best.Links.Count))
                    best = new Step(v, links);
            }
            if (best is null) return null;
            steps.Add(best);
            placed.Add(best.Vertex);
        }
        return steps;
    }

    private sealed record Step(int Vertex, List<(int Other, double Length)> Links);

    private sealed class Search(SurveyInput input, List<Step> steps)
    {
        private readonly Vec2?[] _pos = new Vec2?[input.Walls.Count];
        private int _leaves;

        public List<Vec2[]> Solutions { get; } = [];

        /// <summary>長度都符合、不自交、方向也對，只因「對角線穿牆」被排除的形狀。</summary>
        public List<Vec2[]> ExteriorDiagonalShapes { get; } = [];
        public string? FirstConflict { get; private set; }

        public void Run()
        {
            _pos[0] = Vec2.Zero;
            _pos[1] = new Vec2(input.Walls[0], 0);
            Recurse(0);
        }

        private void Recurse(int k)
        {
            if (_leaves > MaxLeaves) return;
            if (k == steps.Count)
            {
                _leaves++;
                var pts = _pos.Select(p => p!.Value).ToArray();
                var poly = new Polygon(pts);
                if (IsValidShape(poly, input)) Solutions.Add(pts);
                else if (IsSimpleWithOrientation(poly, input)) ExteriorDiagonalShapes.Add(pts);
                return;
            }

            var step = steps[k];
            var (a, ra) = step.Links[0];
            var (b, rb) = step.Links[1];
            foreach (var candidate in CircleIntersections(_pos[a]!.Value, ra, _pos[b]!.Value, rb, step.Vertex, a, b))
            {
                if (!ConsistentWithExtraLinks(step, candidate)) continue;
                _pos[step.Vertex] = candidate;
                Recurse(k + 1);
                _pos[step.Vertex] = null;
            }
        }

        private bool ConsistentWithExtraLinks(Step step, Vec2 p)
        {
            foreach (var (other, length) in step.Links.Skip(2))
            {
                double err = Math.Abs(p.DistanceTo(_pos[other]!.Value) - length);
                if (err > 3 + 0.05 * length) return false;
            }
            return true;
        }

        private IEnumerable<Vec2> CircleIntersections(Vec2 a, double ra, Vec2 b, double rb, int v, int ia, int ib)
        {
            double d = a.DistanceTo(b);
            double gap = Math.Max(d - (ra + rb), Math.Abs(ra - rb) - d);
            if (gap > TangentTolerance || d < 1e-9)
            {
                FirstConflict ??= $"{LabelPair(ia, v)}（{ra:0.#}）、{LabelPair(ib, v)}（{rb:0.#}）與 {LabelPair(ia, ib)}（{d:0.#}）" +
                                  $"不滿足三角不等式，差了 {gap:0.#} cm，請確認這幾條量測。";
                yield break;
            }
            var e = (b - a) / d;
            double x = (ra * ra - rb * rb + d * d) / (2 * d);
            double h = Math.Sqrt(Math.Max(0, ra * ra - x * x));
            var foot = a + e * x;
            yield return foot + e.Perp() * h;
            if (h > 1e-6) yield return foot - e.Perp() * h;
        }
    }

    /// <summary>不自交、方向與量測方向一致、所有量測的對角線都在房間內（捲尺不能穿牆）。</summary>
    private static bool IsValidShape(Polygon poly, SurveyInput input) =>
        IsSimpleWithOrientation(poly, input) && input.Diagonals.All(d => DiagonalIsInterior(poly, d.From, d.To));

    private static bool IsSimpleWithOrientation(Polygon poly, SurveyInput input) =>
        poly.IsSimple() && poly.IsCounterClockwise != input.Clockwise;

    /// <summary>對角線 i–j 是否完全在房間內，也就是捲尺量得到。</summary>
    public static bool DiagonalIsInterior(Polygon poly, int i, int j)
    {
        var (a, b) = (poly[i], poly[j]);
        int n = poly.Count;
        for (int k = 0; k < n; k++)
        {
            int k2 = (k + 1) % n;
            if (k == i || k == j || k2 == i || k2 == j) continue;
            if (Segment.Intersects(a, b, poly[k], poly[k2])) return false;
        }
        var mid = (a + b) / 2;
        return poly.Contains(mid);
    }

    private static double MaxDistance(Vec2[] a, Vec2[] b) => a.Zip(b, (p, q) => p.DistanceTo(q)).Max();

    private static (int, int) MostDiscriminatingPair(Vec2[] s1, Vec2[] s2, List<Bar> bars)
    {
        var measured = new HashSet<(int, int)>(bars.Select(b => b.From < b.To ? (b.From, b.To) : (b.To, b.From)));
        (int, int) best = (0, 2);
        double bestDiff = -1;
        for (int i = 0; i < s1.Length; i++)
            for (int j = i + 1; j < s1.Length; j++)
            {
                if (measured.Contains((i, j))) continue;
                double diff = Math.Abs(s1[i].DistanceTo(s1[j]) - s2[i].DistanceTo(s2[j]));
                if (diff > bestDiff) { bestDiff = diff; best = (i, j); }
            }
        return best;
    }
}
