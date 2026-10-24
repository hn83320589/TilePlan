using System.Globalization;
using System.Text;
using TilePlan.Core.Geometry;

namespace TilePlan.Web.Rendering;

/// <summary>把幾何轉成 SVG 路徑字串（世界座標，y 軸向上；畫布外層再以 scale(1,-1) 翻轉）。</summary>
public static class Svg
{
    public static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    public static string Path(Polygon p) => Append(new StringBuilder(), p).ToString();

    public static string Path(Region r)
    {
        var sb = new StringBuilder();
        foreach (var p in r.Polygons) Append(sb, p);
        return sb.ToString();
    }

    /// <summary>多片磚合成一條路徑：1,200 片磚只產生一個 DOM 元素，重繪快很多。</summary>
    public static string Path(IEnumerable<Region> regions)
    {
        var sb = new StringBuilder();
        foreach (var r in regions)
            foreach (var p in r.Polygons) Append(sb, p);
        return sb.ToString();
    }

    /// <summary>
    /// 編號標籤的位置與字級：在切割片內取樣，找離邊界最遠的點（近似「最難到達的點」）。
    /// 有缺口的切片標籤不會落在缺口上（避免和缺口內的其他切片重疊），窄片的字級也會縮小以免超出。
    /// </summary>
    public static (Vec2 At, double Size) LabelPlacement(Region shape, double maxSize)
    {
        var b = shape.Bounds;
        var edges = shape.Polygons.SelectMany(p => p.Edges()).ToList();
        var center = new Vec2((b.MinX + b.MaxX) / 2, (b.MinY + b.MaxY) / 2);
        const int nx = 16, ny = 8;
        var samples = new List<(Vec2 Point, double Clearance)>();
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                var p = new Vec2(b.MinX + b.Width * (i + 0.5) / nx, b.MinY + b.Height * (j + 0.5) / ny);
                if (shape.Contains(p)) samples.Add((p, edges.Min(e => DistanceToSegment(p, e.A, e.B))));
            }
        if (samples.Count == 0) return (center, maxSize);

        // 長條上許多點離邊界一樣遠：在「幾乎最遠」的點中取最靠中心的，標籤才會置中
        double farthest = samples.Max(s => s.Clearance);
        var (best, bestClearance) = samples.Where(s => s.Clearance >= farthest * 0.95).MinBy(s => s.Point.DistanceTo(center));
        // 文字高約等於字級、寬約 2.4 倍字級（如 C012）：字級受「到邊界的距離」限制
        double size = Math.Min(maxSize, bestClearance * 0.8);
        return (best, size);
    }

    private static double DistanceToSegment(Vec2 p, Vec2 a, Vec2 b)
    {
        var ab = b - a;
        double t = ab.LengthSquared < 1e-12 ? 0 : Math.Clamp((p - a).Dot(ab) / ab.LengthSquared, 0, 1);
        return p.DistanceTo(a + ab * t);
    }

    public static string Points(IEnumerable<Vec2> pts) => string.Join(" ", pts.Select(v => $"{N(v.X)},{N(v.Y)}"));

    private static StringBuilder Append(StringBuilder sb, Polygon p)
    {
        for (int i = 0; i < p.Count; i++)
        {
            sb.Append(i == 0 ? 'M' : 'L');
            sb.Append(N(p[i].X)).Append(',').Append(N(p[i].Y));
        }
        return sb.Append('Z');
    }
}

public static class Fmt
{
    private static readonly CultureInfo C = CultureInfo.InvariantCulture;

    public static string Cm(double v) => v.ToString("0.#", C);
    public static string Cm2(double v) => v.ToString("0.0", C);
    public static string Kg(double v) => v.ToString("0.00", C);
    public static string D2(decimal v) => v.ToString("0.00", C);
    public static string Money(decimal v) => v.ToString("#,0", C);
    public static string Percent(decimal v) => (v * 100).ToString("0.#", C) + "%";
}
