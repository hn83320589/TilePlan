using TilePlan.Core.Geometry;
using TilePlan.Core.Tiles;

namespace TilePlan.Core.Layout;

public sealed record LayoutResult(
    Lattice Lattice,
    Region Region,
    IReadOnlyList<Piece> Pieces,
    IReadOnlyList<(int I, int J)> TouchedCells,
    double JointLength)
{
    public int FullCount => Pieces.Count(p => p.Kind == PieceKind.Full);
    public int CutCount => Pieces.Count(p => p.Kind == PieceKind.Cut);
    public double PieceArea => Pieces.Sum(p => p.Area);
    public IEnumerable<Piece> CutPieces => Pieces.Where(p => p.Kind == PieceKind.Cut);

    /// <summary>給人看的排、欄編號：從房間內最靠近基準線起點的那一排、那一欄算起，以 1 起算。</summary>
    public (int Row, int Column) DisplayIndex(Piece piece)
    {
        if (Pieces.Count == 0) return (1, 1);
        int minJ = Pieces.Min(p => p.J);
        int minI = Pieces.Where(p => p.J == piece.J).Min(p => p.I);
        return (piece.J - minJ + 1, piece.I - minI + 1);
    }
}

/// <summary>
/// 依磚格把可鋪區域排滿，並分類為整片／切割片。
/// <list type="number">
/// <item>把區域轉到磚格座標 (u, v)：每片磚都是對齊座標軸的長方形，裁切沒有旋轉誤差。</item>
/// <item>把區域的邊光柵化到格子上，記錄每格被哪些邊穿過（邊界格）。</item>
/// <item>非邊界格整格在內或在外，用格子中心判斷；邊界格若沒有邊穿進磚的內部，同樣用中心判斷。</item>
/// <item>只有真的被邊穿過的磚才做 Clipper 裁切。</item>
/// </list>
/// </summary>
public static class LayoutEngine
{
    /// <summary>裁切後面積小於此值（cm²）的碎片視為數值誤差，捨棄。</summary>
    public const double SliverArea = 1e-4;

    /// <summary>被裁掉的面積小於此值（cm²）時仍視為整片。</summary>
    public const double FullTolerance = 0.01;

    private const double Eps = 1e-6;

    public static LayoutResult Layout(Region region, Lattice lattice)
    {
        var pieces = new List<Piece>();
        var touched = new List<(int, int)>();
        if (region.IsEmpty) return new LayoutResult(lattice, region, pieces, touched, 0);

        var local = new Region(region.Polygons.Select(p => p.Transform(lattice.ToLattice)).ToList());
        var edges = local.Polygons.SelectMany(p => p.Edges()).ToArray();
        var boundary = BoundaryCells(edges, lattice);
        var b = local.Bounds;
        var boundaryCenterlines = new List<(Vec2, Vec2)>();
        double interiorJointLength = 0;
        int cutNo = 0;

        for (int j = lattice.RowOf(b.MinY); j <= lattice.RowOf(b.MaxY); j++)
        {
            for (int i = lattice.ColumnOf(b.MinX, j); i <= lattice.ColumnOf(b.MaxX, j); i++)
            {
                var o = lattice.CellOrigin(i, j);
                if (boundary.TryGetValue((i, j), out var cellEdges))
                {
                    touched.Add((i, j));
                    var (right, top) = lattice.JointCenterlines(i, j);
                    boundaryCenterlines.Add(right);
                    boundaryCenterlines.Add(top);

                    bool crossed = cellEdges.Any(e => CrossesOpenRect(edges[e].A, edges[e].B, o, lattice.TileU, lattice.TileV));
                    if (!crossed)
                    {
                        if (local.Contains(o + new Vec2(lattice.TileU / 2, lattice.TileV / 2)))
                            pieces.Add(FullPiece(lattice, i, j));
                        continue;
                    }
                    var piece = ClipTile(local, lattice, i, j, ref cutNo);
                    if (piece is not null) pieces.Add(piece);
                }
                else if (local.Contains(o + new Vec2(lattice.Mu / 2, lattice.Mv / 2)))
                {
                    touched.Add((i, j));
                    interiorJointLength += lattice.TileV + lattice.Mu;
                    pieces.Add(FullPiece(lattice, i, j));
                }
            }
        }

        double jointLength = lattice.Joint > 0
            ? interiorJointLength + PolygonOps.ClippedLineLength(boundaryCenterlines, region)
            : 0;
        return new LayoutResult(lattice, region, pieces, touched, jointLength);
    }

    private static Piece FullPiece(Lattice lattice, int i, int j)
    {
        var rect = LocalTileRect(lattice);
        return new Piece(i, j, PieceKind.Full, 0, new Region([lattice.TileQuad(i, j)]), new Region([rect]));
    }

    private static Polygon LocalTileRect(Lattice l) =>
        new([new(0, 0), new(l.TileU, 0), new(l.TileU, l.TileV), new(0, l.TileV)]);

    private static Piece? ClipTile(Region local, Lattice lattice, int i, int j, ref int cutNo)
    {
        // 磚的原點對齊整數格，局部形狀才會剛好落在 [0, TileU] × [0, TileV] 內
        var raw = lattice.CellOrigin(i, j);
        var o = new Vec2(PolygonOps.Snap(raw.X), PolygonOps.Snap(raw.Y));
        var clipped = PolygonOps.RectClip(local, o.X, o.Y, o.X + lattice.TileU, o.Y + lattice.TileV);
        double area = clipped.Area;
        if (area < SliverArea) return null;
        if (lattice.TileU * lattice.TileV - area < FullTolerance) return FullPiece(lattice, i, j);

        var world = new Region(clipped.Polygons.Select(p => p.Transform(lattice.ToWorld)).ToList());
        var tileLocal = new Region(clipped.Polygons.Select(p => p.Translate(-o)).ToList());
        return new Piece(i, j, PieceKind.Cut, ++cutNo, world, tileLocal);
    }

    /// <summary>
    /// 線段 ab 是否穿進長方形的內部（Liang–Barsky，長方形往內縮 Eps）。
    /// 只貼著磚邊或只碰到角的線段不算。
    /// </summary>
    internal static bool CrossesOpenRect(Vec2 a, Vec2 b, Vec2 origin, double w, double h)
    {
        double x0 = origin.X + Eps, x1 = origin.X + w - Eps, y0 = origin.Y + Eps, y1 = origin.Y + h - Eps;
        double t0 = 0, t1 = 1, dx = b.X - a.X, dy = b.Y - a.Y;
        return Clip(-dx, a.X - x0) && Clip(dx, x1 - a.X) && Clip(-dy, a.Y - y0) && Clip(dy, y1 - a.Y) && t1 - t0 > 1e-12;

        bool Clip(double p, double q)
        {
            if (Math.Abs(p) < 1e-15) return q >= 0;
            double t = q / p;
            if (p < 0) { if (t > t1) return false; if (t > t0) t0 = t; }
            else { if (t < t0) return false; if (t < t1) t1 = t; }
            return true;
        }
    }

    /// <summary>
    /// 把區域的每條邊（磚格座標）光柵化到格子上：逐排求出這條邊落在該排的 u 區間，記錄重疊的格子與邊的索引。
    /// </summary>
    internal static Dictionary<(int, int), List<int>> BoundaryCells(IReadOnlyList<(Vec2 A, Vec2 B)> edges, Lattice lattice)
    {
        var cells = new Dictionary<(int, int), List<int>>();
        for (int e = 0; e < edges.Count; e++)
        {
            var (a, b) = edges[e];
            double lo = Math.Min(a.Y, b.Y), hi = Math.Max(a.Y, b.Y);
            for (int j = lattice.RowOf(lo - Eps); j <= lattice.RowOf(hi + Eps); j++)
            {
                double rowLo = lattice.OffsetV + j * lattice.Mv;
                double rowHi = rowLo + lattice.Mv;
                if (!ClipToBand(a, b, rowLo - Eps, rowHi + Eps, out double u0, out double u1)) continue;
                for (int i = lattice.ColumnOf(u0 - Eps, j); i <= lattice.ColumnOf(u1 + Eps, j); i++)
                {
                    if (!cells.TryGetValue((i, j), out var list)) cells[(i, j)] = list = [];
                    list.Add(e);
                }
            }
        }
        return cells;
    }

    /// <summary>線段 ab 落在 v ∈ [lo, hi] 帶狀區內的部分，其 u 範圍。</summary>
    private static bool ClipToBand(Vec2 a, Vec2 b, double lo, double hi, out double u0, out double u1)
    {
        double t0 = 0, t1 = 1;
        double dv = b.Y - a.Y;
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
