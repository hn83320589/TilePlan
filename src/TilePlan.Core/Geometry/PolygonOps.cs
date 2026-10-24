using Clipper2Lib;

namespace TilePlan.Core.Geometry;

/// <summary>
/// Clipper2 轉接層。進出 Clipper 時把公分轉成整數：1 單位 = 1 µm。
/// RectClip 求交點時會有 ±1 單位的誤差，用 µm 才能讓一片磚的面積誤差低於 0.01 cm²。
/// </summary>
public static class PolygonOps
{
    public const double Scale = 10_000; // 每公分的整數單位數

    /// <summary>對齊到 Clipper 的整數格。</summary>
    public static double Snap(double cm) => Math.Round(cm * Scale) / Scale;

    public static Region Intersect(Region a, Region b) => Run(ClipType.Intersection, a, b);

    public static Region Difference(Region a, Region b) => Run(ClipType.Difference, a, b);

    public static Region Union(Region a, Region b) => Run(ClipType.Union, a, b);

    public static Region Union(IEnumerable<Region> regions)
    {
        var all = new Paths64();
        foreach (var r in regions) all.AddRange(ToPaths(r));
        return all.Count == 0 ? Region.Empty : FromPaths(Clipper.Union(all, FillRule.NonZero));
    }

    /// <summary>往外擴張（正）或往內縮（負）指定距離，直角保持直角。</summary>
    public static Region Inflate(Region r, double delta) =>
        FromPaths(Clipper.InflatePaths(ToPaths(r), delta * Scale, JoinType.Miter, EndType.Polygon));

    /// <summary>把區域拆成互不相連的部分（每部分含自己的洞）。</summary>
    public static IReadOnlyList<Region> Components(Region r)
    {
        if (r.Polygons.Count <= 1) return r.IsEmpty ? [] : [r];
        var tree = new PolyTree64();
        var clipper = new Clipper64();
        clipper.AddSubject(ToPaths(r));
        clipper.Execute(ClipType.Union, FillRule.NonZero, tree);

        var result = new List<Region>();
        void Walk(PolyPath64 node)
        {
            for (int i = 0; i < node.Count; i++)
            {
                var outer = node[i];
                var paths = new Paths64 { outer.Polygon! };
                for (int h = 0; h < outer.Count; h++)
                {
                    paths.Add(outer[h].Polygon!);
                    Walk(outer[h]);   // 洞裡的島
                }
                result.Add(FromPaths(paths));
            }
        }
        Walk(tree);
        return result;
    }

    /// <summary>把折線（開放路徑）裁切到區域內，回傳區域內的總長度（cm）。</summary>
    public static double ClippedLineLength(IEnumerable<(Vec2 A, Vec2 B)> segments, Region region)
    {
        var open = new Paths64(segments.Select(s => new Path64 { ToPoint(s.A), ToPoint(s.B) }));
        if (open.Count == 0 || region.IsEmpty) return 0;

        var clipper = new Clipper64();
        clipper.AddOpenSubject(open);
        clipper.AddClip(ToPaths(region));
        var closed = new Paths64();
        var openResult = new Paths64();
        clipper.Execute(ClipType.Intersection, FillRule.NonZero, closed, openResult);

        double total = 0;
        foreach (var path in openResult)
            for (int i = 1; i < path.Count; i++)
                total += Math.Sqrt(Clipper.DistanceSqr(path[i - 1], path[i]));
        return total / Scale;
    }

    /// <summary>
    /// 以對齊座標軸的長方形裁切區域（Clipper 的 RectClip 比一般布林運算快很多）。
    /// RectClip 逐條路徑處理，含洞時要再做一次聯集讓結果正規化。
    /// </summary>
    public static Region RectClip(Region region, double minX, double minY, double maxX, double maxY)
    {
        var rect = new Rect64((long)Math.Round(minX * Scale), (long)Math.Round(minY * Scale),
                              (long)Math.Round(maxX * Scale), (long)Math.Round(maxY * Scale));
        var result = Clipper.RectClip(rect, ToPaths(region));
        if (result.Count > 1) result = Clipper.Union(result, FillRule.NonZero);
        return FromPaths(result);
    }

    private static Region Run(ClipType type, Region a, Region b)
    {
        if (a.IsEmpty && type != ClipType.Union) return Region.Empty;
        var result = Clipper.BooleanOp(type, ToPaths(a), ToPaths(b), FillRule.NonZero);
        return FromPaths(result);
    }

    internal static Point64 ToPoint(Vec2 v) =>
        new((long)Math.Round(v.X * Scale), (long)Math.Round(v.Y * Scale));

    internal static Paths64 ToPaths(Region r)
    {
        var paths = new Paths64(r.Polygons.Count);
        foreach (var poly in r.Polygons)
        {
            var path = new Path64(poly.Count);
            foreach (var v in poly.Vertices) path.Add(ToPoint(v));
            paths.Add(path);
        }
        return paths;
    }

    internal static Region FromPaths(Paths64 paths)
    {
        var polys = new List<Polygon>(paths.Count);
        foreach (var path in paths)
        {
            if (path.Count < 3) continue;
            var verts = new Vec2[path.Count];
            for (int i = 0; i < path.Count; i++)
                verts[i] = new Vec2(path[i].X / Scale, path[i].Y / Scale);
            polys.Add(new Polygon(verts));
        }
        return new Region(polys);
    }
}
