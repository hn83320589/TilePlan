using TilePlan.Core.Estimation;
using TilePlan.Core.Geometry;
using TilePlan.Core.Quoting;
using TilePlan.Core.Tiles;
using static TilePlan.Core.Tests.TestShapes;

namespace TilePlan.Core.Tests.Quoting;

public class QuoteBuilderTests
{
    private static RoomMaterials Materials(double areaCm2, int tilesWithWaste, double adhesiveKg = 0, double groutKg = 0) =>
        new(tilesWithWaste, 0, 0, tilesWithWaste, Area.FromCm2(areaCm2), adhesiveKg, 0, 0, groutKg);

    [Fact]
    public void Same_tile_in_two_rooms_is_boxed_together()
    {
        // 各 9 片、每箱 8 片：分開算 2 + 2 = 4 箱；合併 ceil(18 / 8) = 3 箱
        var tile = Tile(60, 30, perBox: 8, price: 1000m);
        var quote = QuoteBuilder.Build(
        [
            new("浴室", "t1", "霧面磚", tile, Pattern.Straight, Materials(60_000, 9)),
            new("廚房", "t1", "霧面磚", tile, Pattern.Straight, Materials(60_000, 9)),
        ], Pricing.Default);

        var line = Assert.Single(quote.Tiles);
        Assert.Equal(18, line.Pieces);
        Assert.Equal(3, line.Boxes);
        Assert.Equal(3000m, line.Amount);
    }

    [Fact]
    public void Different_tiles_are_listed_separately()
    {
        var quote = QuoteBuilder.Build(
        [
            new("浴室", "t1", "霧面磚", Tile(60, 30, perBox: 8), Pattern.Straight, Materials(60_000, 9)),
            new("客廳", "t2", "木紋磚", Tile(120, 20, perBox: 6), Pattern.RunningBond(BondRatio.Half), Materials(60_000, 13)),
        ], Pricing.Default);

        Assert.Equal(2, quote.Tiles.Count);
        Assert.Equal(3, quote.Tiles.Single(t => t.TileName == "木紋磚").Boxes);
    }

    [Fact]
    public void Labor_uses_displayed_ping_and_pattern_rate()
    {
        // 6.00 m² = 1.8150… 坪 → 顯示 1.82 → 1.82 × 3500 = 6370（正排）、1.82 × 4500 = 8190（斜貼）
        var quote = QuoteBuilder.Build(
        [
            new("浴室", "t1", "霧面磚", Tile(60, 30), Pattern.Straight, Materials(60_000, 40)),
            new("玄關", "t1", "霧面磚", Tile(60, 30), Pattern.Diagonal, Materials(60_000, 44)),
        ], Pricing.Default);

        Assert.Equal(1.82m, quote.Rooms[0].AreaPing);
        Assert.Equal(6370m, quote.Rooms[0].Labor);
        Assert.Equal(8190m, quote.Rooms[1].Labor);
    }

    [Fact]
    public void Consumables_are_combined_then_bagged()
    {
        // 黏著劑 30 + 15 = 45 kg → 20 kg/包 → 3 包 × 280；填縫劑 0.75 + 0.5 = 1.25 kg → 2 kg/包 → 1 包 × 150
        var quote = QuoteBuilder.Build(
        [
            new("浴室", "t1", "霧面磚", Tile(60, 30), Pattern.Straight, Materials(60_000, 40, 30, 0.75)),
            new("廚房", "t1", "霧面磚", Tile(60, 30), Pattern.Straight, Materials(30_000, 20, 15, 0.5)),
        ], Pricing.Default);

        var adhesive = quote.Consumables.Single(c => c.Name == "黏著劑");
        var grout = quote.Consumables.Single(c => c.Name == "填縫劑");
        Assert.Equal((3, 840m), (adhesive.Bags, adhesive.Amount));
        Assert.Equal((1, 150m), (grout.Bags, grout.Amount));
    }

    [Fact]
    public void Grand_total_is_the_sum_of_printed_lines()
    {
        var quote = QuoteBuilder.Build(
        [
            new("浴室", "t1", "霧面磚", Tile(60, 30, perBox: 8, price: 980m), Pattern.Straight, Materials(56_400, 37, 28.2, 0.7)),
            new("廚房", "t2", "復古磚", Tile(20, 20, perBox: 25, price: 1250m), Pattern.Diagonal, Materials(81_000, 230, 40.5, 2.1)),
        ], Pricing.Default);

        decimal printed = quote.Tiles.Sum(t => t.Amount) + quote.Consumables.Sum(c => c.Amount) + quote.Rooms.Sum(r => r.Labor);
        Assert.Equal(printed, quote.GrandTotal);
        Assert.All(quote.Rooms, r => Assert.Equal(QuoteBuilder.Money(r.AreaPing * r.LaborPerPing), r.Labor));
        Assert.All(quote.Tiles, t => Assert.Equal(t.Boxes * t.PricePerBox, t.Amount));
    }
}
