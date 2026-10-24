using TilePlan.Core.Geometry;
using TilePlan.Core.Layout;

namespace TilePlan.Core.Cutting;

/// <summary>
/// 安排每片切割片從哪片原磚切下（2D 裝箱的貪婪解：Best-Fit Decreasing）。
/// <list type="number">
/// <item>切割片依面積由大到小處理。</item>
/// <item>先找放得進去的餘料：候選位置是「切割片某頂點對齊餘料某頂點」與「外框角對齊」；
///   選擇面積最小、放得進去的餘料（把大塊留給後面），同一塊中選擇讓剩餘最大塊最大的位置。</item>
/// <item>找不到才開新原磚，切割片放在它原本在磚上的位置（切線就是排版時的裁切線）。</item>
/// <item>切剩的部分若比「最小的待切片」還小就丟棄，避免餘料清單膨脹。</item>
/// </list>
/// 關閉重用時每片切割片各用一片原磚，所以開啟重用後原磚數不會更多。
/// </summary>
public static class OffcutPlanner
{
    /// <summary>
    /// 判定「放得進去」時容許超出餘料的面積（cm²）：沿切割片周長 2 個整數單位（2 µm）寬的細縫，吸收整數化誤差。
    /// 斜向切線上每一點都可能差 1 單位，所以容許量必須與周長成正比。
    /// </summary>
    public static double FitTolerance(Region piece) => Math.Max(1e-3, 2 * piece.Perimeter / PolygonOps.Scale);

    private sealed class Offcut(int stockNo, Region shape)
    {
        public int StockNo { get; } = stockNo;
        public Region Shape { get; } = shape;
        public Bounds Bounds { get; } = shape.Bounds;
        public double Area { get; } = shape.Area;

        /// <summary>單一凸多邊形時，可用「頂點都在內」快速判定包含，不必呼叫 Clipper。</summary>
        public Polygon? Convex { get; } = shape.Polygons.Count == 1 && IsConvex(shape.Polygons[0]) ? shape.Polygons[0] : null;
    }

    public static CutPlan Plan(LayoutResult layout, CutSettings settings)
    {
        double tu = layout.Lattice.TileU, tv = layout.Lattice.TileV;
        var stockRect = new Region(new Polygon([new(0, 0), new(tu, 0), new(tu, tv), new(0, tv)]));
        var pieces = layout.CutPieces
            .OrderByDescending(p => Math.Round(p.Area, 6))
            .ThenBy(p => p.CutNo)
            .ToList();

        var cuts = new Dictionary<int, List<CutAssignment>>();
        var offcuts = new List<Offcut>();
        // 斜貼等情況切割片形狀大量重複：記住「這個形狀放不進這塊餘料」，同形狀的下一片就不必再試
        var failures = new HashSet<(Offcut, string)>();
        double[] rotations = settings.AllowQuarterTurns ? [0, 90, 180, 270] : [0, 180];
        int stockCount = 0;

        for (int k = 0; k < pieces.Count; k++)
        {
            var piece = pieces[k];
            double smallestRemaining = pieces[^1].Area;
            var fit = settings.ReuseOffcuts ? FindFit(piece, offcuts, failures, rotations, settings.KerfCm) : null;

            if (fit is not null)
            {
                var (offcut, placed, rotation) = fit.Value;
                cuts[offcut.StockNo].Add(new CutAssignment(piece, offcut.StockNo, placed, rotation, true, offcut.Shape));
                offcuts.Remove(offcut);
                AddRemainders(offcuts, offcut.StockNo, Subtract(offcut.Shape, placed, settings.KerfCm), smallestRemaining);
            }
            else
            {
                int no = ++stockCount;
                cuts[no] = [new CutAssignment(piece, no, piece.LocalShape, 0, false, stockRect)];
                if (settings.ReuseOffcuts)
                    AddRemainders(offcuts, no, Subtract(stockRect, piece.LocalShape, settings.KerfCm), smallestRemaining);
            }
        }

        var stockTiles = cuts.OrderBy(c => c.Key).Select(c =>
        {
            var used = PolygonOps.Union(c.Value.Select(a => a.Placed));
            return new StockTile(c.Key, c.Value, PolygonOps.Difference(stockRect, used));
        }).ToList();
        return new CutPlan(stockTiles, tu, tv);
    }

    private static Region Subtract(Region from, Region piece, double kerf)
    {
        var cutter = kerf > 0 ? PolygonOps.Inflate(piece, kerf / 2) : piece;
        return PolygonOps.Difference(from, cutter);
    }

    private static void AddRemainders(List<Offcut> offcuts, int stockNo, Region remainder, double minUsefulArea)
    {
        foreach (var part in PolygonOps.Components(remainder))
            if (part.Area >= minUsefulArea - 1e-3)
                offcuts.Add(new Offcut(stockNo, part));
    }

    private static (Offcut Offcut, Region Placed, double Rotation)? FindFit(
        Piece piece, List<Offcut> offcuts, HashSet<(Offcut, string)> failures, double[] rotations, double kerf)
    {
        (Offcut, Region, double)? best = null;
        double bestOffcutArea = double.PositiveInfinity, bestLargestLeft = -1;
        string signature = Signature(piece.LocalShape);
        double minArea = piece.Area - FitTolerance(piece.LocalShape);

        // 由小到大：找到放得進去的最小餘料後，更大的就不必再試
        foreach (var offcut in offcuts.OrderBy(o => o.Area))
        {
            if (offcut.Area < minArea) continue;
            if (offcut.Area > bestOffcutArea + 1e-9) break;
            if (failures.Contains((offcut, signature))) continue;
            var ob = offcut.Bounds;
            bool fitsSomewhere = false;

            foreach (var rotation in rotations)
            {
                var rotated = Rotate(piece.LocalShape, rotation);
                var rb = rotated.Bounds;
                if (rb.Width > ob.Width + 1e-6 || rb.Height > ob.Height + 1e-6) continue;

                foreach (var t in Translations(rotated, rb, offcut))
                {
                    var tb = new Bounds(rb.MinX + t.X, rb.MinY + t.Y, rb.MaxX + t.X, rb.MaxY + t.Y);
                    if (tb.MinX < ob.MinX - 1e-6 || tb.MinY < ob.MinY - 1e-6 || tb.MaxX > ob.MaxX + 1e-6 || tb.MaxY > ob.MaxY + 1e-6) continue;

                    var placed = Translate(rotated, t);
                    if (!Contains(offcut, placed)) continue;
                    fitsSomewhere = true;

                    double largestLeft = PolygonOps.Components(Subtract(offcut.Shape, placed, kerf)).Select(r => r.Area).DefaultIfEmpty(0).Max();
                    bool better = offcut.Area < bestOffcutArea - 1e-9 || largestLeft > bestLargestLeft + 1e-9;
                    if (best is null || better)
                    {
                        best = (offcut, placed, rotation);
                        bestOffcutArea = offcut.Area;
                        bestLargestLeft = largestLeft;
                    }
                }
            }
            if (!fitsSomewhere) failures.Add((offcut, signature));
        }
        return best;
    }

    private static bool Contains(Offcut offcut, Region placed)
    {
        if (offcut.Convex is { } hull && placed.Polygons.All(p => p.IsCounterClockwise))
        {
            // 凸多邊形：所有頂點都在內（容許 1 µm）就代表整片在內
            const double tolerance = 1 / PolygonOps.Scale;
            foreach (var poly in placed.Polygons)
                foreach (var v in poly.Vertices)
                    for (int i = 0, n = hull.Count; i < n; i++)
                    {
                        var a = hull[i];
                        var b = hull[(i + 1) % n];
                        if ((b - a).Cross(v - a) < -tolerance * (b - a).Length) return false;
                    }
            return true;
        }
        return PolygonOps.Difference(placed, offcut.Shape).Area <= FitTolerance(placed);
    }

    private static bool IsConvex(Polygon p)
    {
        if (!p.IsCounterClockwise) return false;
        for (int i = 0, n = p.Count; i < n; i++)
        {
            var a = p[i];
            var b = p[(i + 1) % n];
            var c = p[(i + 2) % n];
            if ((b - a).Cross(c - b) < -1e-9) return false;
        }
        return true;
    }

    /// <summary>形狀的指紋：平移到外框左下角為原點後，頂點座標到 0.01 mm。</summary>
    private static string Signature(Region shape)
    {
        var b = shape.Bounds;
        return string.Join("|", shape.Polygons.Select(p => string.Join(";", p.Vertices.Select(v =>
            $"{Math.Round((v.X - b.MinX) * 1000)},{Math.Round((v.Y - b.MinY) * 1000)}"))));
    }

    /// <summary>候選平移：切割片外框四角對齊餘料外框四角，以及頂點對頂點。</summary>
    private static IEnumerable<Vec2> Translations(Region piece, Bounds pb, Offcut offcut)
    {
        var ob = offcut.Bounds;
        var seen = new HashSet<(long, long)>();
        IEnumerable<Vec2> All()
        {
            yield return new Vec2(ob.MinX - pb.MinX, ob.MinY - pb.MinY);
            yield return new Vec2(ob.MaxX - pb.MaxX, ob.MinY - pb.MinY);
            yield return new Vec2(ob.MinX - pb.MinX, ob.MaxY - pb.MaxY);
            yield return new Vec2(ob.MaxX - pb.MaxX, ob.MaxY - pb.MaxY);
            foreach (var ov in offcut.Shape.Polygons.SelectMany(p => p.Vertices))
                foreach (var pv in piece.Polygons.SelectMany(p => p.Vertices))
                    yield return ov - pv;
        }
        foreach (var t in All())
            if (seen.Add(((long)Math.Round(t.X * 1e4), (long)Math.Round(t.Y * 1e4))))
                yield return t;
    }

    private static Region Rotate(Region r, double degrees)
    {
        if (degrees == 0) return r;
        Func<Vec2, Vec2> f = degrees switch
        {
            90 => v => new Vec2(-v.Y, v.X),
            180 => v => new Vec2(-v.X, -v.Y),
            270 => v => new Vec2(v.Y, -v.X),
            _ => throw new ArgumentOutOfRangeException(nameof(degrees)),
        };
        return new Region(r.Polygons.Select(p => p.Transform(f)).ToList());
    }

    private static Region Translate(Region r, Vec2 t) => new(r.Polygons.Select(p => p.Translate(t)).ToList());
}
