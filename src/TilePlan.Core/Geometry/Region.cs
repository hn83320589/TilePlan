namespace TilePlan.Core.Geometry;

/// <summary>
/// 由多個多邊形組成的區域：逆時針為外框、順時針為洞（Clipper 的 NonZero 規則）。
/// </summary>
public sealed class Region
{
    public static readonly Region Empty = new([]);

    public IReadOnlyList<Polygon> Polygons { get; }

    public Region(IReadOnlyList<Polygon> polygons) => Polygons = polygons;

    public Region(Polygon polygon) : this([polygon.ToCounterClockwise()]) { }

    public double Area => Polygons.Sum(p => p.SignedArea);

    public bool IsEmpty => Polygons.Count == 0;

    public double Perimeter => Polygons.Sum(p => p.Perimeter);

    public Bounds Bounds => Bounds.Of(Polygons.SelectMany(p => p.Vertices));

    /// <summary>可鋪設區域 = 房間 − ∪不鋪設區。</summary>
    public static Region Tileable(Polygon room, IEnumerable<Polygon> exclusions)
    {
        var excluded = PolygonOps.Union(exclusions.Select(e => new Region(e)));
        return PolygonOps.Difference(new Region(room), excluded);
    }

    public bool Contains(Vec2 p)
    {
        int winding = 0;
        foreach (var poly in Polygons)
            if (poly.Contains(p)) winding += poly.IsCounterClockwise ? 1 : -1;
        return winding > 0;
    }
}
