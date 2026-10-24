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
