using TilePlan.Core.Geometry;

namespace TilePlan.Core.Tests.Geometry;

public class AreaTests
{
    private static Polygon Rect(double x, double y, double w, double h) =>
        new([new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h)]);

    [Fact]
    public void Rectangle_300x200_is_6_m2_and_1_82_ping()
    {
        var area = Area.FromCm2(Rect(0, 0, 300, 200).Area);

        Assert.Equal(6.00m, area.M2Rounded);
        Assert.Equal(1.82m, area.PingRounded);
    }

    [Fact]
    public void Rectangle_minus_60x60_exclusion_is_5_64_m2()
    {
        var region = Region.Tileable(Rect(0, 0, 300, 200), [Rect(100, 50, 60, 60)]);

        Assert.Equal(5.64m, Area.FromCm2(region.Area).M2Rounded);
    }

    [Fact]
    public void Ping_conversion_uses_400_over_121()
    {
        var area = Area.FromCm2(400.0 / 121.0 * 10_000);

        Assert.Equal(1.00m, area.PingRounded);
    }

    [Fact]
    public void Shoelace_area_is_independent_of_orientation()
    {
        var ccw = Rect(0, 0, 10, 20);
        var cw = new Polygon(ccw.Vertices.Reverse().ToArray());

        Assert.Equal(200, ccw.Area, 9);
        Assert.Equal(200, cw.Area, 9);
        Assert.True(ccw.IsCounterClockwise);
        Assert.False(cw.IsCounterClockwise);
    }

    [Fact]
    public void L_shape_area_is_sum_of_rectangles()
    {
        var l = new Polygon([new(0, 0), new(400, 0), new(400, 150), new(250, 150), new(250, 300), new(0, 300)]);

        Assert.Equal(400 * 150 + 250 * 150, l.Area, 9);
    }

    [Fact]
    public void Exclusion_partly_outside_room_only_removes_the_overlap()
    {
        // 柱子一半在牆外：只扣掉房間內的 30×60
        var region = Region.Tileable(Rect(0, 0, 300, 200), [Rect(-30, 0, 60, 60)]);

        Assert.Equal(300 * 200 - 30 * 60, region.Area, 6);
    }

    [Fact]
    public void Overlapping_exclusions_are_not_double_counted()
    {
        var region = Region.Tileable(Rect(0, 0, 300, 200), [Rect(50, 50, 60, 60), Rect(80, 50, 60, 60)]);

        Assert.Equal(300 * 200 - 90 * 60, region.Area, 6);
    }

    [Fact]
    public void Exclusion_in_the_middle_leaves_a_hole()
    {
        var region = Region.Tileable(Rect(0, 0, 300, 200), [Rect(100, 50, 60, 60)]);

        Assert.Equal(2, region.Polygons.Count);
        Assert.Single(region.Polygons, p => !p.IsCounterClockwise);
    }

    [Fact]
    public void Rounding_is_half_away_from_zero()
    {
        // 0.125 m² → 0.13（銀行家捨入會得到 0.12）
        Assert.Equal(0.13m, Area.FromCm2(1250).M2Rounded);
    }
}
