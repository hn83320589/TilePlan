using TilePlan.Core.Geometry;

namespace TilePlan.Core.Layout;

public enum PieceKind
{
    Full,
    Cut,
}

/// <summary>
/// 排版中的一片磚。切割片可能由多個多邊形組成（例如被柱子切成兩塊，但仍來自同一格）。
/// </summary>
/// <param name="CutNo">切割片的流水號（1 起算）；整片為 0。</param>
/// <param name="Shape">鋪設後的形狀（世界座標）。</param>
/// <param name="LocalShape">在原磚上的形狀（磚的局部座標，左下角為原點）。</param>
public sealed record Piece(int I, int J, PieceKind Kind, int CutNo, Region Shape, Region LocalShape)
{
    public double Area => Shape.Area;

    public Bounds LocalBounds => LocalShape.Bounds;

    public string Label => Kind == PieceKind.Full ? "整片" : $"C{CutNo:000}";
}
