using TilePlan.Core.Cutting;
using TilePlan.Core.Geometry;
using TilePlan.Core.Layout;
using TilePlan.Core.Tiles;

namespace TilePlan.Core.Estimation;

/// <summary>單一房間的材料用量。</summary>
/// <param name="FullTiles">整片磚數。</param>
/// <param name="StockForCuts">製作切割片所需的原磚數（已扣除重用）。</param>
/// <param name="TilesWithWaste">含損耗片數 = ceil((整片 + 切割用原磚) × (1 + 損耗率))。</param>
public sealed record RoomMaterials(
    int FullTiles,
    int StockForCuts,
    decimal WasteRate,
    int TilesWithWaste,
    Area TileableArea,
    double AdhesiveKg,
    double JointLengthCm,
    double GroutVolumeCm3,
    double GroutKg)
{
    public int BaseTiles => FullTiles + StockForCuts;
}

/// <summary>
/// 材料估算。片數、箱數、包數一律用 decimal 計算再無條件進位：
/// double 的 100 × 1.05 = 105.00000000000001，進位後會多算一片。
/// </summary>
public static class MaterialEstimator
{
    public static RoomMaterials Estimate(LayoutResult layout, CutPlan plan, TileSpec tile, decimal wasteRate, double adhesiveKgPerM2, double groutDensityKgPerM3)
    {
        int full = layout.FullCount;
        int stock = plan.StockCount;
        var area = Area.FromCm2(layout.Region.Area);

        double adhesiveKg = area.M2 * adhesiveKgPerM2;
        double groutVolume = layout.JointLength * tile.JointCm * tile.ThicknessCm;     // cm³
        double groutKg = groutVolume * groutDensityKgPerM3 / 1_000_000;                // kg/m³ → kg/cm³

        return new RoomMaterials(full, stock, wasteRate, TilesWithWaste(full + stock, wasteRate), area,
            adhesiveKg, layout.JointLength, groutVolume, groutKg);
    }

    public static int TilesWithWaste(int baseTiles, decimal wasteRate)
    {
        if (wasteRate < 0) throw new ArgumentOutOfRangeException(nameof(wasteRate), "損耗率不可為負。");
        return (int)Math.Ceiling(baseTiles * (1 + wasteRate));
    }

    public static int Boxes(int tiles, int piecesPerBox)
    {
        if (piecesPerBox <= 0) throw new ArgumentOutOfRangeException(nameof(piecesPerBox), "每箱片數必須大於 0。");
        return (int)Math.Ceiling((decimal)tiles / piecesPerBox);
    }

    /// <summary>包數 = ceil(kg / 每包 kg)。kg 先四捨五入到公克，避免 40.0000000001 kg 被進位成多一包。</summary>
    public static int Bags(double kg, double kgPerBag)
    {
        if (!(kgPerBag > 0)) throw new ArgumentOutOfRangeException(nameof(kgPerBag), "每包重量必須大於 0。");
        decimal grams = Math.Round((decimal)kg, 3);
        return (int)Math.Ceiling(grams / Math.Round((decimal)kgPerBag, 3));
    }
}
