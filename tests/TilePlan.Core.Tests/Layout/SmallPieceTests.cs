using TilePlan.Core.Geometry;
using TilePlan.Core.Layout;
using TilePlan.Core.Offset;
using TilePlan.Core.Tiles;
using static TilePlan.Core.Tests.TestShapes;

namespace TilePlan.Core.Tests.Layout;

/// <summary>細碎片：不論貼著牆、不鋪設區或在轉角，最小寬度低於短邊 1/3 的切割片都要被標出來。</summary>
public class SmallPieceTests
{
    [Fact]
    public void Min_width_of_a_strip_is_its_narrow_side()
    {
        Assert.Equal(5, PieceGeometry.MinWidth(new Region(Rect(0, 0, 30, 5))), 9);
    }

    [Fact]
    public void Min_width_of_a_right_triangle_is_its_altitude_to_the_hypotenuse()
    {
        var triangle = new Region(new Polygon([new(0, 0), new(10, 0), new(0, 10)]));

        Assert.Equal(10 / Math.Sqrt(2), PieceGeometry.MinWidth(triangle), 9);
    }

    [Fact]
    public void Min_width_of_a_rotated_rectangle_ignores_its_orientation()
    {
        Vec2[] corners = [new(0, 0), new(40, 0), new(40, 7), new(0, 7)];
        var rotated = new Region(new Polygon(corners.Select(c => c.Rotate(0.6) + new Vec2(13, -4)).ToArray()));

        Assert.Equal(7, PieceGeometry.MinWidth(rotated), 6);
    }

    [Fact]
    public void Piece_split_in_two_reports_its_narrowest_part()
    {
        var parts = new Region([Rect(0, 0, 30, 20), Rect(40, 0, 3, 20)]);

        Assert.Equal(3, PieceGeometry.MinWidth(parts), 9);
    }

    [Fact]
    public void Slanted_wall_slivers_are_flagged()
    {
        var room = new Polygon([new(0, 0), new(400, 21), new(400, 300), new(0, 300)]);
        var layout = OffsetOptimizer.Optimize(room, new Region(room), Lattice.Create(Tile(20, 20), Pattern.Straight, Baseline.Default)).Layout;

        var small = layout.SmallPieces();

        Assert.NotEmpty(small);
        Assert.All(small, s => Assert.True(s.MinWidth < 20.0 / 3));
    }

    [Fact]
    public void Auto_offset_rectangle_has_no_small_pieces()
    {
        var layout = OffsetOptimizer.Optimize(Rect(0, 0, 300, 200), RectRegion(300, 200),
            Lattice.Create(Tile(60, 30), Pattern.Straight, Baseline.Default)).Layout;

        Assert.Empty(layout.SmallPieces());
    }

    [Fact]
    public void Corner_sliver_from_an_exclusion_is_flagged()
    {
        // 櫃子上緣在 y = 55，偏移 0 時第 2 排磚 30.2–60.2 → 櫃子上方只剩 5.2 cm 的細條
        var region = Region.Tileable(Rect(0, 0, 300, 200), [Rect(0, 0, 100, 55)]);
        var layout = LayoutEngine.Layout(region, Lattice.Create(Tile(60, 30), Pattern.Straight, Baseline.Default));

        var small = layout.SmallPieces();

        Assert.Contains(small, s => Math.Abs(s.MinWidth - 5.2) < 1e-3);
    }
}
