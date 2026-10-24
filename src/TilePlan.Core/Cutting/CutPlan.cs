using TilePlan.Core.Geometry;
using TilePlan.Core.Layout;

namespace TilePlan.Core.Cutting;

/// <param name="ReuseOffcuts">是否用切剩的餘料製作其他切割片。</param>
/// <param name="AllowQuarterTurns">餘料是否可轉 90°（有紋路方向的磚不建議）。0° 與 180° 一律允許。</param>
/// <param name="KerfCm">切割刀口寬度。</param>
public sealed record CutSettings(bool ReuseOffcuts = true, bool AllowQuarterTurns = false, double KerfCm = 0)
{
    public static readonly CutSettings Default = new();
}

/// <summary>一片切割片從哪片原磚、哪個位置切下來。</summary>
/// <param name="Placed">切割片在原磚上的形狀（原磚座標，左下角為原點）。</param>
/// <param name="Source">切下之前，該原磚剩餘的餘料形狀；新開的原磚則為整片磚。</param>
public sealed record CutAssignment(Piece Piece, int StockNo, Region Placed, double RotationDeg, bool FromOffcut, Region Source);

/// <param name="No">原磚編號（1 起算），切割清單顯示為 S01、S02…</param>
/// <param name="Remaining">全部切完後剩下的部分（廢料）。</param>
public sealed record StockTile(int No, IReadOnlyList<CutAssignment> Cuts, Region Remaining)
{
    public string Label => $"S{No:00}";
}

public sealed record CutPlan(IReadOnlyList<StockTile> StockTiles, double TileU, double TileV)
{
    /// <summary>製作切割片所需的原磚數（已扣除重用）。</summary>
    public int StockCount => StockTiles.Count;

    public IEnumerable<CutAssignment> Assignments => StockTiles.SelectMany(s => s.Cuts);

    public int ReusedCount => Assignments.Count(a => a.FromOffcut);

    public CutAssignment? Find(Piece piece) => Assignments.FirstOrDefault(a => a.Piece.CutNo == piece.CutNo);
}
