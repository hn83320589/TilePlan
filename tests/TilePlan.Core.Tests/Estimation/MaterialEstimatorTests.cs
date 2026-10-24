using TilePlan.Core.Cutting;
using TilePlan.Core.Estimation;
using TilePlan.Core.Layout;
using TilePlan.Core.Tiles;
using static TilePlan.Core.Tests.TestShapes;

namespace TilePlan.Core.Tests.Estimation;

public class MaterialEstimatorTests
{
    private static RoomMaterials Estimate300x200(bool reuse = true)
    {
        var tile = Tile(60, 30, jointMm: 2, thicknessMm: 9, perBox: 8);
        var layout = LayoutEngine.Layout(RectRegion(300, 200), Lattice.Create(tile, Pattern.Straight, Baseline.Default));
        var plan = OffcutPlanner.Plan(layout, new CutSettings(ReuseOffcuts: reuse));
        return MaterialEstimator.Estimate(layout, plan, tile, 0.05m, adhesiveKgPerM2: 5, groutDensityKgPerM3: 1600);
    }

    [Fact]
    public void Tiles_include_full_and_stock_for_cuts_then_waste_rounded_up()
    {
        // 整片 24、切割 11：59.2 cm 欄剩 0.8 cm、18.8 cm 排剩 11.2 cm，都放不下其他切割片 → 原磚 11
        // (24 + 11) × 1.05 = 36.75 → 37 片
        var m = Estimate300x200();

        Assert.Equal(24, m.FullTiles);
        Assert.Equal(11, m.StockForCuts);
        Assert.Equal(37, m.TilesWithWaste);
        Assert.Equal(5, MaterialEstimator.Boxes(m.TilesWithWaste, 8));   // 37 / 8 = 4.625 → 5 箱
    }

    [Fact]
    public void Waste_uses_decimal_so_exact_products_are_not_rounded_up()
    {
        // double：100 × 1.05 = 105.00000000000001，進位會變成 106
        Assert.Equal(105, MaterialEstimator.TilesWithWaste(100, 0.05m));
        Assert.Equal(110, MaterialEstimator.TilesWithWaste(100, 0.10m));
        Assert.Equal(11, MaterialEstimator.TilesWithWaste(10, 0.05m));     // 10.5 → 11
    }

    [Theory]
    [InlineData(16, 8, 2)]
    [InlineData(17, 8, 3)]
    [InlineData(1, 8, 1)]
    [InlineData(0, 8, 0)]
    public void Boxes_are_rounded_up(int tiles, int perBox, int expected) =>
        Assert.Equal(expected, MaterialEstimator.Boxes(tiles, perBox));

    [Fact]
    public void Adhesive_is_area_times_usage()
    {
        // 6.00 m² × 5 kg/m² = 30 kg → 20 kg/包 → 2 包
        var m = Estimate300x200();

        Assert.Equal(30, m.AdhesiveKg, 6);
        Assert.Equal(2, MaterialEstimator.Bags(m.AdhesiveKg, 20));
    }

    [Fact]
    public void Grout_from_joint_length_width_and_thickness()
    {
        // 直縫 4 條 × (6 × 30 + 18.8) = 795.2；橫縫 6 條 × 300 = 1800 → 2595.2 cm
        // 體積 = 2595.2 × 0.2 × 0.9 = 467.136 cm³；重量 = 467.136 × 1.6 g/cm³ = 0.7474176 kg
        var m = Estimate300x200();

        Assert.Equal(2595.2, m.JointLengthCm, 3);
        Assert.Equal(467.136, m.GroutVolumeCm3, 3);
        Assert.Equal(0.7474176, m.GroutKg, 5);
    }

    [Fact]
    public void Bags_ignore_floating_point_noise()
    {
        Assert.Equal(2, MaterialEstimator.Bags(40.0000000001, 20));
        Assert.Equal(3, MaterialEstimator.Bags(40.001, 20));
    }
}
