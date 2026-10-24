using TilePlan.Core.Geometry;
using TilePlan.Core.Layout;
using TilePlan.Core.Tiles;

namespace TilePlan.Core.Tests.Layout;

/// <summary>
/// 驗收 8：以 240 個隨機房間（固定種子）驗證排版的不變量。
/// </summary>
public class LayoutPropertyTests
{
    private const int RoomCount = 240;

    private static readonly List<(RandomRooms.Case Case, LayoutResult Layout)> Results =
        RandomRooms.Generate(RoomCount)
            .Select(c => (c, LayoutEngine.Layout(c.Region, Lattice.Create(c.Tile, c.Pattern, c.Baseline, c.OffsetU, c.OffsetV))))
            .ToList();

    [Fact]
    public void Generates_at_least_200_rooms_covering_all_patterns_and_shapes()
    {
        Assert.True(Results.Count >= 200);
        Assert.Equal(4, Results.Select(r => r.Case.Pattern.DisplayName).Distinct().Count());
        Assert.Equal(3, Results.Select(r => r.Case.Kind).Distinct().Count());
        Assert.All(Results, r => Assert.True(r.Layout.Pieces.Count > 0, r.Case.ToString()));
    }

    [Fact]
    public void Pieces_plus_joints_equal_tileable_area_within_0_1_percent()
    {
        var failures = Results
            .Select(r => (r.Case, Error: Math.Abs(r.Layout.PieceArea + JointNetwork.Area(r.Layout) - r.Case.Region.Area) / r.Case.Region.Area))
            .Where(x => x.Error >= 1e-3)
            .Select(x => $"{x.Case}：相對誤差 {x.Error:P4}")
            .ToList();

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void Pieces_do_not_overlap_and_stay_inside_the_region()
    {
        var failures = new List<string>();
        foreach (var (c, layout) in Results)
        {
            var union = PolygonOps.Union(layout.Pieces.Select(p => p.Shape));
            double overlap = layout.PieceArea - union.Area;
            double outside = PolygonOps.Difference(union, c.Region).Area;
            // 容許量：0.01 mm 整數化在每條磚邊上造成的雜訊，與房間大小成正比
            double tolerance = Math.Max(0.5, c.Region.Area * 1e-5);
            if (overlap > tolerance || outside > tolerance)
                failures.Add($"{c}：重疊 {overlap:0.###} cm²，超出 {outside:0.###} cm²");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void Full_pieces_are_exactly_one_tile_and_cut_pieces_are_not_larger()
    {
        var failures = new List<string>();
        foreach (var (c, layout) in Results)
        {
            double tileArea = layout.Lattice.TileU * layout.Lattice.TileV;
            foreach (var p in layout.Pieces)
            {
                if (p.Kind == PieceKind.Full && Math.Abs(p.Area - tileArea) > 1e-6)
                    failures.Add($"{c}：整片 ({p.I},{p.J}) 面積 {p.Area}");
                if (p.Kind == PieceKind.Cut && p.Area > tileArea + 0.01)
                    failures.Add($"{c}：切割片 ({p.I},{p.J}) 面積 {p.Area} 大於整片");
            }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(20)));
    }

    [Fact]
    public void Cut_pieces_local_shape_lies_within_the_tile()
    {
        var failures = new List<string>();
        foreach (var (c, layout) in Results)
        {
            var tile = new Region(TestShapes.Rect(0, 0, layout.Lattice.TileU, layout.Lattice.TileV));
            foreach (var p in layout.CutPieces)
            {
                double outside = PolygonOps.Difference(p.LocalShape, tile).Area;
                if (outside > 0.01 || Math.Abs(p.LocalShape.Area - p.Area) > 0.01)
                    failures.Add($"{c}：{p.Label} 局部形狀超出原磚 {outside:0.###} cm²");
            }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(20)));
    }
}
