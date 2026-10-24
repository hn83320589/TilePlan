using TilePlan.Core.Geometry;

namespace TilePlan.Core.Layout;

public static class PieceGeometry
{
    /// <summary>
    /// 切割片的最小寬度：凸包在所有方向上的最小寬度（旋轉卡尺：最窄的方向一定垂直於凸包的某一條邊）。
    /// 由多塊組成的切割片取最窄的一塊。L 形這類凹形切片會以凸包計算，可能高估。
    /// </summary>
    public static double MinWidth(Region shape)
    {
        double min = double.PositiveInfinity;
        foreach (var poly in shape.Polygons.Where(p => p.IsCounterClockwise))
        {
            var hull = ConvexHull(poly.Vertices);
            for (int i = 0; i < hull.Count; i++)
            {
                var a = hull[i];
                var b = hull[(i + 1) % hull.Count];
                if (a.DistanceTo(b) < 1e-9) continue;
                double width = hull.Max(p => Math.Abs(Segment.SignedDistanceToLine(a, b, p)));
                min = Math.Min(min, width);
            }
        }
        return double.IsPositiveInfinity(min) ? 0 : min;
    }

    /// <summary>Andrew monotone chain，逆時針。</summary>
    private static List<Vec2> ConvexHull(IReadOnlyList<Vec2> points)
    {
        var pts = points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        if (pts.Count < 3) return pts;
        var hull = new List<Vec2>();
        foreach (var pass in new[] { pts, Enumerable.Reverse(pts).ToList() })
        {
            int start = hull.Count;
            foreach (var p in pass)
            {
                while (hull.Count >= start + 2 && (hull[^1] - hull[^2]).Cross(p - hull[^2]) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            hull.RemoveAt(hull.Count - 1);
        }
        return hull;
    }
}
