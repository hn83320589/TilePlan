using TilePlan.Core.Geometry;

namespace TilePlan.Core.Layout;

/// <summary>
/// 縫網路：由磚格「另外」產生的縫矩形，與磚片的裁切流程互相獨立。
/// 用於驗證「磚片面積 + 縫面積 = 可鋪面積」——若縫面積改用「可鋪面積 − 磚片面積」計算，這個檢查就失去意義。
/// </summary>
public static class JointNetwork
{
    public static double Area(LayoutResult layout)
    {
        if (layout.Lattice.Joint <= 0) return 0;
        var rects = new List<Polygon>(layout.TouchedCells.Count * 2);
        foreach (var (i, j) in layout.TouchedCells)
        {
            var (right, top) = layout.Lattice.JointRects(i, j);
            rects.Add(right);
            rects.Add(top);
        }
        return PolygonOps.Intersect(new Region(rects), layout.Region).Area;
    }
}
