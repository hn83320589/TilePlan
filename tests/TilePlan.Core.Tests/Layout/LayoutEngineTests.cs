using TilePlan.Core.Geometry;
using TilePlan.Core.Layout;
using TilePlan.Core.Tiles;
using static TilePlan.Core.Tests.TestShapes;

namespace TilePlan.Core.Tests.Layout;

public class LayoutEngineTests
{
    private static LayoutResult Run(Region region, TileSpec tile, Pattern pattern, Baseline? baseline = null, double ou = 0, double ov = 0) =>
        LayoutEngine.Layout(region, Lattice.Create(tile, pattern, baseline ?? Baseline.Default, ou, ov));

    [Fact]
    public void Straight_30x60_in_300x200_from_corner_matches_hand_count()
    {
        // x：每 60.2 cm 一片，第 5 片從 240.8 開始 → 切成 59.2 cm
        // y：每 30.2 cm 一排，第 7 排從 181.2 開始 → 切成 18.8 cm
        // 整片 = 4 × 6 = 24；切割 = 第 5 欄 7 片 + 第 7 排前 4 片 = 11
        var layout = Run(RectRegion(300, 200), Tile(60, 30), Pattern.Straight);

        Assert.Equal(24, layout.FullCount);
        Assert.Equal(11, layout.CutCount);
    }

    [Fact]
    public void Straight_cut_pieces_have_hand_computed_sizes()
    {
        var layout = Run(RectRegion(300, 200), Tile(60, 30), Pattern.Straight);

        var endColumn = layout.CutPieces.Where(p => p.I == 4 && p.J < 6).ToList();
        Assert.Equal(6, endColumn.Count);
        Assert.All(endColumn, p =>
        {
            Assert.Equal(59.2, p.LocalBounds.Width, 3);
            Assert.Equal(30, p.LocalBounds.Height, 3);
        });

        var corner = Assert.Single(layout.CutPieces, p => p.I == 4 && p.J == 6);
        Assert.Equal(59.2, corner.LocalBounds.Width, 3);
        Assert.Equal(18.8, corner.LocalBounds.Height, 3);
    }

    [Fact]
    public void Running_bond_half_matches_hand_count()
    {
        // 偶數排：4 整 + 1 切；奇數排右移 30.1：兩端各切一片（29.9、29.1）+ 4 整
        // 第 0–5 排整高：整片 6 × 4 = 24，切割 3 × 1 + 3 × 2 = 9；第 6 排（偶數、高 18.8）5 片全切
        var layout = Run(RectRegion(300, 200), Tile(60, 30), Pattern.RunningBond(BondRatio.Half));

        Assert.Equal(24, layout.FullCount);
        Assert.Equal(14, layout.CutCount);
        var oddRowStart = Assert.Single(layout.CutPieces, p => p.J == 1 && p.I == -1);
        Assert.Equal(29.9, oddRowStart.Shape.Bounds.Width, 3);
    }

    [Fact]
    public void Running_bond_third_matches_hand_count()
    {
        // 三種排的位移：0、20.067、40.133 → 每排分別切 1、2、2 片
        // 第 0–5 排：整片 24，切割 1+2+2+1+2+2 = 10；第 6 排 5 片全切 → 15
        var layout = Run(RectRegion(300, 200), Tile(60, 30), Pattern.RunningBond(BondRatio.Third));

        Assert.Equal(24, layout.FullCount);
        Assert.Equal(15, layout.CutCount);
    }

    [Fact]
    public void Diagonal_30x30_in_120x120_matches_hand_count()
    {
        // 磚格轉 45°，令 p = i − j、s = i + j（同奇偶），m = 30.2：
        //   整片：p·m ≥ 30 且 p·m + 30 ≤ 120√2 → p ∈ 1..4；s ≥ 0 且 s·m + 60 ≤ 120√2 → s ∈ 0..3 → 8 片
        //   與房間相交：菱形中心到房間的 L1 距離 < 15√2 → p ∈ 0..6、s ∈ −1..5 → 同奇偶共 24 片
        var layout = Run(RectRegion(120, 120), Tile(30, 30), Pattern.Diagonal);

        Assert.Equal(8, layout.FullCount);
        Assert.Equal(16, layout.CutCount);
    }

    [Fact]
    public void Exclusion_is_not_tiled()
    {
        // 60 × 60 的柱子剛好佔掉 (0..60.2, 0..60.4) 附近的磚
        var region = Region.Tileable(Rect(0, 0, 300, 200), [Rect(0, 0, 60, 60)]);
        var layout = Run(region, Tile(60, 30), Pattern.Straight);

        Assert.DoesNotContain(layout.Pieces, p => p.Shape.Contains(new Vec2(30, 30)));
        Assert.Equal(region.Area, layout.PieceArea + JointNetwork.Area(layout), region.Area * 1e-6);
    }

    [Fact]
    public void Pillar_splitting_a_tile_yields_one_piece_with_two_parts()
    {
        // 寬 10 cm 的管道間從上到下穿過第一片磚（磚高 30）
        var region = Region.Tileable(Rect(0, 0, 300, 200), [Rect(25, -10, 10, 50)]);
        var layout = Run(region, Tile(60, 30), Pattern.Straight);

        var split = Assert.Single(layout.Pieces, p => p.I == 0 && p.J == 0);
        Assert.Equal(PieceKind.Cut, split.Kind);
        Assert.Equal(2, split.Shape.Polygons.Count);
    }

    [Fact]
    public void Baseline_rotation_rotates_the_grid()
    {
        // 基準線轉 90° 等於把磚的長邊改成沿 y 軸：300 × 200 變成 x 方向 10 片 30、y 方向 4 片 60
        var layout = Run(RectRegion(300, 200), Tile(60, 30), Pattern.Straight, new Baseline(Vec2.Zero, 90));

        var full = layout.Pieces.Where(p => p.Kind == PieceKind.Full).ToList();
        Assert.All(full, p =>
        {
            Assert.Equal(30, p.Shape.Bounds.Width, 3);
            Assert.Equal(60, p.Shape.Bounds.Height, 3);
        });
    }

    [Fact]
    public void Joint_length_times_width_equals_joint_area_in_rectangle()
    {
        var layout = Run(RectRegion(300, 200), Tile(60, 30), Pattern.Straight);

        // 縫中心線剛好落在牆上時只會差半個縫寬，相對誤差很小
        double jointArea = JointNetwork.Area(layout);
        Assert.Equal(jointArea, layout.JointLength * layout.Lattice.Joint, jointArea * 0.02);
    }

    [Fact]
    public void Area_is_conserved_for_hand_examples()
    {
        foreach (var pattern in new[] { Pattern.Straight, Pattern.RunningBond(BondRatio.Half), Pattern.RunningBond(BondRatio.Third), Pattern.Diagonal })
        {
            var region = RectRegion(300, 200);
            var layout = Run(region, Tile(60, 30), pattern, ou: 7.3, ov: 11.1);
            Assert.Equal(region.Area, layout.PieceArea + JointNetwork.Area(layout), region.Area * 1e-5);
        }
    }
}
