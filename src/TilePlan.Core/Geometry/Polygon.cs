namespace TilePlan.Core.Geometry;

/// <summary>
/// 不可變的簡單多邊形（頂點不重複首點）。
/// 逆時針為正向；在 <see cref="Region"/> 中，順時針的多邊形代表洞。
/// </summary>
public sealed class Polygon
{
    public IReadOnlyList<Vec2> Vertices { get; }

    public Polygon(IReadOnlyList<Vec2> vertices)
    {
        if (vertices.Count < 3)
            throw new ArgumentException($"多邊形至少需要 3 個頂點，實際為 {vertices.Count}。", nameof(vertices));
        Vertices = vertices;
    }

    public int Count => Vertices.Count;

    public Vec2 this[int i] => Vertices[i];

    /// <summary>鞋帶公式；逆時針為正。</summary>
    public double SignedArea
    {
        get
        {
            double sum = 0;
            for (int i = 0, n = Vertices.Count; i < n; i++)
            {
                var a = Vertices[i];
                var b = Vertices[(i + 1) % n];
                sum += a.X * b.Y - b.X * a.Y;
            }
            return sum / 2;
        }
    }

    public double Area => Math.Abs(SignedArea);

    public bool IsCounterClockwise => SignedArea > 0;

    public Polygon Reversed() => new(Vertices.Reverse().ToArray());

    public Polygon ToCounterClockwise() => IsCounterClockwise ? this : Reversed();

    public IEnumerable<(Vec2 A, Vec2 B)> Edges()
    {
        for (int i = 0, n = Vertices.Count; i < n; i++)
            yield return (Vertices[i], Vertices[(i + 1) % n]);
    }

    public double Perimeter => Edges().Sum(e => e.A.DistanceTo(e.B));

    public Bounds Bounds => Bounds.Of(Vertices);

    public Polygon Transform(Func<Vec2, Vec2> f) => new(Vertices.Select(f).ToArray());

    public Polygon Translate(Vec2 d) => Transform(v => v + d);

    /// <summary>射線法；落在邊上的點結果不保證。</summary>
    public bool Contains(Vec2 p)
    {
        bool inside = false;
        for (int i = 0, n = Vertices.Count, j = n - 1; i < n; j = i++)
        {
            var a = Vertices[i];
            var b = Vertices[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) &&
                p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    /// <summary>判斷是否為簡單多邊形（邊兩兩不相交，相鄰邊除外）。</summary>
    public bool IsSimple()
    {
        int n = Vertices.Count;
        for (int i = 0; i < n; i++)
        {
            var (a, b) = (Vertices[i], Vertices[(i + 1) % n]);
            for (int j = i + 1; j < n; j++)
            {
                if (j == i + 1 || (i == 0 && j == n - 1)) continue;
                var (c, d) = (Vertices[j], Vertices[(j + 1) % n]);
                if (Segment.Intersects(a, b, c, d)) return false;
            }
        }
        return true;
    }
}
