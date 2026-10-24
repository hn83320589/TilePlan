using TilePlan.Core.Geometry;

namespace TilePlan.Core.Tiles;

/// <summary>
/// 磚格座標系 (u, v)：原點在基準線上，u 軸沿基準線（斜貼時再轉 45°）。
/// 每一格（cell）= 一片磚 + 右側縫 + 上方縫，格子剛好鋪滿整個平面：
/// <code>
///   ┌────────────┬─┐  ← 上方縫（寬 Mu，含交叉點）
///   ├────────────┼─┤
///   │    磚      │ │  ← 右側縫（高 TileV）
///   └────────────┴─┘
/// </code>
/// 第 j 排的格子沿 u 方向錯開 (j mod 週期) / 週期 × Mu（交丁）。
/// </summary>
public sealed class Lattice
{
    public Vec2 Origin { get; }
    public double AngleRad { get; }
    public double TileU { get; }
    public double TileV { get; }
    public double Joint { get; }
    public double Mu => TileU + Joint;
    public double Mv => TileV + Joint;
    public double OffsetU { get; }
    public double OffsetV { get; }
    public int BondPeriod { get; }

    private readonly Vec2 _ex;
    private readonly Vec2 _ey;

    public Lattice(Vec2 origin, double angleRad, double tileU, double tileV, double joint, double offsetU, double offsetV, int bondPeriod)
    {
        if (!(tileU > 0) || !(tileV > 0)) throw new ArgumentException("磚的尺寸必須大於 0。");
        Origin = origin;
        AngleRad = angleRad;
        TileU = tileU;
        TileV = tileV;
        Joint = joint;
        BondPeriod = bondPeriod;
        OffsetU = Mod(offsetU, Mu);
        OffsetV = Mod(offsetV, Mv);
        _ex = Vec2.FromAngle(angleRad);
        _ey = _ex.Perp();
    }

    public static Lattice Create(TileSpec tile, Pattern pattern, Baseline baseline, double offsetU = 0, double offsetV = 0)
    {
        var (u, v) = pattern.LongSideAlongBaseline
            ? (Math.Max(tile.LengthCm, tile.WidthCm), Math.Min(tile.LengthCm, tile.WidthCm))
            : (Math.Min(tile.LengthCm, tile.WidthCm), Math.Max(tile.LengthCm, tile.WidthCm));
        double angle = baseline.AngleDeg + (pattern.Kind == PatternKind.Diagonal ? 45 : 0);
        return new Lattice(baseline.Origin, angle * Math.PI / 180, u, v, tile.JointCm, offsetU, offsetV, pattern.BondPeriod);
    }

    public Lattice WithOffset(double offsetU, double offsetV) =>
        new(Origin, AngleRad, TileU, TileV, Joint, offsetU, offsetV, BondPeriod);

    public static double Mod(double x, double m) => ((x % m) + m) % m;

    /// <summary>單位向量：u 軸、v 軸在世界座標中的方向。</summary>
    public Vec2 AxisU => _ex;
    public Vec2 AxisV => _ey;

    public Vec2 ToWorld(double u, double v) => Origin + _ex * u + _ey * v;

    public Vec2 ToWorld(Vec2 uv) => ToWorld(uv.X, uv.Y);

    public Vec2 ToLattice(Vec2 world)
    {
        var d = world - Origin;
        return new Vec2(d.Dot(_ex), d.Dot(_ey));
    }

    public double RowShift(int j) => (((j % BondPeriod) + BondPeriod) % BondPeriod) * Mu / BondPeriod;

    /// <summary>第 (i, j) 格左下角的 (u, v)。</summary>
    public Vec2 CellOrigin(int i, int j) => new(OffsetU + RowShift(j) + i * Mu, OffsetV + j * Mv);

    public int RowOf(double v) => (int)Math.Floor((v - OffsetV) / Mv);

    public int ColumnOf(double u, int row) => (int)Math.Floor((u - OffsetU - RowShift(row)) / Mu);

    public Polygon TileQuad(int i, int j) => Rect(CellOrigin(i, j), TileU, TileV);

    public Polygon CellQuad(int i, int j) => Rect(CellOrigin(i, j), Mu, Mv);

    /// <summary>格子 (i, j) 的縫：右側縫（高 TileV）與上方縫（寬 Mu），兩者互不重疊。</summary>
    public (Polygon Right, Polygon Top) JointRects(int i, int j)
    {
        var o = CellOrigin(i, j);
        return (Rect(o + new Vec2(TileU, 0), Joint, TileV), Rect(o + new Vec2(0, TileV), Mu, Joint));
    }

    /// <summary>格子 (i, j) 的縫中心線（世界座標）；長度 × 縫寬 = 縫面積。</summary>
    public ((Vec2 A, Vec2 B) Right, (Vec2 A, Vec2 B) Top) JointCenterlines(int i, int j)
    {
        var o = CellOrigin(i, j);
        double h = Joint / 2;
        return ((ToWorld(o.X + TileU + h, o.Y), ToWorld(o.X + TileU + h, o.Y + TileV)),
                (ToWorld(o.X, o.Y + TileV + h), ToWorld(o.X + Mu, o.Y + TileV + h)));
    }

    /// <summary>世界座標轉成磚的局部座標：磚的左下角為原點，u 軸向右。</summary>
    public Vec2 ToTileLocal(Vec2 world, int i, int j) => ToLattice(world) - CellOrigin(i, j);

    private Polygon Rect(Vec2 o, double w, double h) =>
        new([ToWorld(o.X, o.Y), ToWorld(o.X + w, o.Y), ToWorld(o.X + w, o.Y + h), ToWorld(o.X, o.Y + h)]);
}
