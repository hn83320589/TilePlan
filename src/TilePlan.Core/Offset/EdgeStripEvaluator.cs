using TilePlan.Core.Geometry;
using TilePlan.Core.Tiles;

namespace TilePlan.Core.Offset;

/// <summary>
/// 最佳化時用的快速評估：把每面牆近似為半平面，只看跨過牆的磚，算出貼牆切片的深度。
/// 純算術、不呼叫 Clipper；忽略不鋪設區（最後由精確排版驗證）。
/// 同時跨過相鄰另一面（不平行）牆的磚是轉角片：牆上有其他切片時不計入條寬；整面牆都是轉角片時才用它們。
/// 與精確報告的定義一致。
/// </summary>
internal static class EdgeStripEvaluator
{
    private const double Eps = 1e-6;

    public readonly record struct WallMetric(double? MinWidth, int Straddling);

    public static WallMetric Evaluate(Wall wall, Wall? previous, Wall? next, Lattice lattice)
    {
        var a = wall.LatticeA;
        var b = wall.LatticeB;
        double? min = null, cornerMin = null;
        int straddling = 0;
        double lo = Math.Min(a.Y, b.Y), hi = Math.Max(a.Y, b.Y);
        // 只有靠近牆兩端的磚可能同時跨過相鄰的牆（轉角片）；牆中段不必檢查
        double cornerReach = lattice.TileU + lattice.TileV;

        for (int j = lattice.RowOf(lo - Eps); j <= lattice.RowOf(hi + Eps); j++)
        {
            double rowLo = lattice.OffsetV + j * lattice.Mv;
            if (!UOverlapInBand(a, b, rowLo - Eps, rowLo + lattice.TileV + Eps, out double u0, out double u1)) continue;
            for (int i = lattice.ColumnOf(u0 - lattice.Mu, j); i <= lattice.ColumnOf(u1 + Eps, j); i++)
            {
                var o = lattice.CellOrigin(i, j);
                double? depth = StraddleDepth(wall, o, lattice.TileU, lattice.TileV);
                if (depth is null) continue;
                straddling++;
                double along = (o + new Vec2(lattice.TileU / 2, lattice.TileV / 2) - a).Dot(wall.LatticeDir);
                bool nearEnd = along < cornerReach || along > wall.Length - cornerReach;
                if (nearEnd && (IsCorner(wall, previous, o, lattice) || IsCorner(wall, next, o, lattice)))
                {
                    if (cornerMin is null || depth < cornerMin) cornerMin = depth;
                    continue;
                }
                if (min is null || depth < min) min = depth;
            }
        }
        return new WallMetric(min ?? cornerMin, straddling);
    }

    private static bool IsCorner(Wall wall, Wall? other, Vec2 o, Lattice lattice) =>
        other is not null && !other.IsParallelTo(wall) && StraddleDepth(other, o, lattice.TileU, lattice.TileV) is not null;

    /// <summary>
    /// 磚 [o, o + (w, h)] 跨過牆線、且與牆段實際接觸時，回傳內側部分的深度（內側頂點到牆線的最大距離）。
    /// </summary>
    private static double? StraddleDepth(Wall wall, Vec2 o, double w, double h)
    {
        var n = wall.LatticeInward;
        var d = wall.LatticeDir;
        var a = wall.LatticeA;
        Span<Vec2> c = [o, o + new Vec2(w, 0), o + new Vec2(w, h), o + new Vec2(0, h)];
        Span<double> s = stackalloc double[4];
        double maxS = double.NegativeInfinity, minS = double.PositiveInfinity;
        for (int k = 0; k < 4; k++)
        {
            s[k] = (c[k] - a).Dot(n);
            maxS = Math.Max(maxS, s[k]);
            minS = Math.Min(minS, s[k]);
        }
        if (maxS <= Eps || minS >= -Eps) return null;

        // 磚與牆線的交線（弦）在牆方向上的範圍，必須與牆段 [0, L] 有重疊
        double tMin = double.PositiveInfinity, tMax = double.NegativeInfinity;
        for (int k = 0; k < 4; k++)
        {
            int k2 = (k + 1) % 4;
            if ((s[k] > 0) == (s[k2] > 0)) continue;
            double f = s[k] / (s[k] - s[k2]);
            var p = c[k] + (c[k2] - c[k]) * f;
            double t = (p - a).Dot(d);
            tMin = Math.Min(tMin, t);
            tMax = Math.Max(tMax, t);
        }
        if (Math.Min(tMax, wall.Length) - Math.Max(tMin, 0) <= 1e-3) return null;
        return maxS;
    }

    private static bool UOverlapInBand(Vec2 a, Vec2 b, double lo, double hi, out double u0, out double u1)
    {
        double t0 = 0, t1 = 1, dv = b.Y - a.Y;
        if (Math.Abs(dv) < 1e-12)
        {
            if (a.Y < lo || a.Y > hi) { u0 = u1 = 0; return false; }
        }
        else
        {
            double ta = (lo - a.Y) / dv, tb = (hi - a.Y) / dv;
            t0 = Math.Max(t0, Math.Min(ta, tb));
            t1 = Math.Min(t1, Math.Max(ta, tb));
            if (t0 > t1) { u0 = u1 = 0; return false; }
        }
        double ua = a.X + (b.X - a.X) * t0, ub = a.X + (b.X - a.X) * t1;
        u0 = Math.Min(ua, ub);
        u1 = Math.Max(ua, ub);
        return true;
    }
}
