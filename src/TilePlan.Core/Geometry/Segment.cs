namespace TilePlan.Core.Geometry;

public static class Segment
{
    private const double Eps = 1e-9;

    /// <summary>線段 ab 與 cd 是否相交（含端點接觸與共線重疊）。</summary>
    public static bool Intersects(Vec2 a, Vec2 b, Vec2 c, Vec2 d)
    {
        double d1 = (b - a).Cross(c - a);
        double d2 = (b - a).Cross(d - a);
        double d3 = (d - c).Cross(a - c);
        double d4 = (d - c).Cross(b - c);

        if (((d1 > Eps && d2 < -Eps) || (d1 < -Eps && d2 > Eps)) &&
            ((d3 > Eps && d4 < -Eps) || (d3 < -Eps && d4 > Eps)))
            return true;

        return (Math.Abs(d1) <= Eps && OnSegment(a, b, c))
            || (Math.Abs(d2) <= Eps && OnSegment(a, b, d))
            || (Math.Abs(d3) <= Eps && OnSegment(c, d, a))
            || (Math.Abs(d4) <= Eps && OnSegment(c, d, b));
    }

    private static bool OnSegment(Vec2 a, Vec2 b, Vec2 p) =>
        p.X >= Math.Min(a.X, b.X) - Eps && p.X <= Math.Max(a.X, b.X) + Eps &&
        p.Y >= Math.Min(a.Y, b.Y) - Eps && p.Y <= Math.Max(a.Y, b.Y) + Eps;

    /// <summary>點 p 到直線 ab 的有號距離；p 在 a→b 左側為正。</summary>
    public static double SignedDistanceToLine(Vec2 a, Vec2 b, Vec2 p) =>
        (b - a).Cross(p - a) / (b - a).Length;
}
