using TilePlan.Core.Estimation;
using TilePlan.Core.Geometry;
using TilePlan.Core.Tiles;

namespace TilePlan.Core.Quoting;

public sealed record QuoteRoomInput(string RoomName, string TileId, string TileName, TileSpec Tile, Pattern Pattern, RoomMaterials Materials);

/// <summary>各房間明細：工資以顯示的兩位小數坪數計算（決策 D2）。</summary>
public sealed record QuoteRoomLine(
    string RoomName, decimal AreaM2, decimal AreaPing, string TileName, string PatternName,
    int TilesWithWaste, double AdhesiveKg, double GroutKg, decimal LaborPerPing, decimal Labor);

/// <summary>叫貨彙總：同一種磁磚跨房間合併片數後才換算箱數（決策 D1）。</summary>
public sealed record QuoteTileLine(string TileName, int Pieces, int PiecesPerBox, int Boxes, decimal PricePerBox, decimal Amount);

/// <summary>耗材依包裝計價，整個專案合併後包數無條件進位（決策 D4）。</summary>
public sealed record QuoteConsumableLine(string Name, double Kg, double KgPerBag, int Bags, decimal PricePerBag, decimal Amount);

public sealed record Quote(
    IReadOnlyList<QuoteRoomLine> Rooms,
    IReadOnlyList<QuoteTileLine> Tiles,
    IReadOnlyList<QuoteConsumableLine> Consumables)
{
    public decimal TotalAreaM2 => Rooms.Sum(r => r.AreaM2);
    public decimal TotalAreaPing => Rooms.Sum(r => r.AreaPing);
    public decimal MaterialTotal => Tiles.Sum(t => t.Amount) + Consumables.Sum(c => c.Amount);
    public decimal LaborTotal => Rooms.Sum(r => r.Labor);

    /// <summary>總價 = 各行金額加總（各行已四捨五入到元），報價單上的數字自己加得起來。</summary>
    public decimal GrandTotal => MaterialTotal + LaborTotal;
}

public static class QuoteBuilder
{
    public static Quote Build(IReadOnlyList<QuoteRoomInput> rooms, Pricing pricing)
    {
        var roomLines = rooms.Select(r =>
        {
            var area = r.Materials.TileableArea;
            decimal rate = pricing.LaborPerPing(r.Pattern.Kind);
            return new QuoteRoomLine(r.RoomName, area.M2Rounded, area.PingRounded, r.TileName, r.Pattern.DisplayName,
                r.Materials.TilesWithWaste, r.Materials.AdhesiveKg, r.Materials.GroutKg, rate, Money(area.PingRounded * rate));
        }).ToList();

        var tileLines = rooms
            .GroupBy(r => r.TileId)
            .Select(g =>
            {
                var first = g.First();
                int pieces = g.Sum(r => r.Materials.TilesWithWaste);
                int boxes = MaterialEstimator.Boxes(pieces, first.Tile.PiecesPerBox);
                return new QuoteTileLine(first.TileName, pieces, first.Tile.PiecesPerBox, boxes, first.Tile.PricePerBox, Money(boxes * first.Tile.PricePerBox));
            })
            .ToList();

        double adhesiveKg = rooms.Sum(r => r.Materials.AdhesiveKg);
        double groutKg = rooms.Sum(r => r.Materials.GroutKg);
        var consumables = new List<QuoteConsumableLine>
        {
            Consumable("黏著劑", adhesiveKg, pricing.AdhesiveKgPerBag, pricing.AdhesivePricePerBag),
            Consumable("填縫劑", groutKg, pricing.GroutKgPerBag, pricing.GroutPricePerBag),
        };
        return new Quote(roomLines, tileLines, consumables);
    }

    private static QuoteConsumableLine Consumable(string name, double kg, double kgPerBag, decimal pricePerBag)
    {
        int bags = kg > 0 ? MaterialEstimator.Bags(kg, kgPerBag) : 0;
        return new QuoteConsumableLine(name, kg, kgPerBag, bags, pricePerBag, Money(bags * pricePerBag));
    }

    public static decimal Money(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);
}
