using TilePlan.Core.Cutting;
using TilePlan.Core.Geometry;
using TilePlan.Core.Layout;
using TilePlan.Core.Tiles;

namespace TilePlan.Core.Tests.Cutting;

/// <summary>驗收 9：以隨機房間驗證餘料重用的兩個保證。</summary>
public class OffcutPropertyTests
{
    private static readonly List<(RandomRooms.Case Case, LayoutResult Layout, CutPlan Off, CutPlan On)> Results =
        RandomRooms.Generate(90).Select(c =>
        {
            var layout = LayoutEngine.Layout(c.Region, Lattice.Create(c.Tile, c.Pattern, c.Baseline, c.OffsetU, c.OffsetV));
            return (c, layout,
                OffcutPlanner.Plan(layout, new CutSettings(ReuseOffcuts: false)),
                OffcutPlanner.Plan(layout, CutSettings.Default));
        }).ToList();

    [Fact]
    public void Reuse_never_needs_more_tiles()
    {
        Assert.All(Results, r => Assert.True(r.On.StockCount <= r.Off.StockCount, $"{r.Case}：重用 {r.On.StockCount} > 不重用 {r.Off.StockCount}"));
        Assert.True(Results.Sum(r => r.Off.StockCount - r.On.StockCount) > 0, "隨機房間中至少要有一些重用");
    }

    [Fact]
    public void Every_reuse_actually_fits_and_pieces_on_one_tile_do_not_overlap()
    {
        var failures = new List<string>();
        foreach (var (c, _, _, plan) in Results)
        {
            foreach (var a in plan.Assignments.Where(a => a.FromOffcut))
            {
                double outside = PolygonOps.Difference(a.Placed, a.Source).Area;
                if (outside > OffcutPlanner.FitTolerance(a.Placed) || Math.Abs(a.Placed.Area - a.Piece.Area) > 1e-3)
                    failures.Add($"{c}：{a.Piece.Label} 超出餘料 {outside:0.####} cm²");
            }
            foreach (var s in plan.StockTiles)
            {
                // 每片放入時最多可超出餘料 FitTolerance（整數化誤差），重疊上限即為各片容許量的總和
                double overlap = s.Cuts.Sum(x => x.Placed.Area) - PolygonOps.Union(s.Cuts.Select(x => x.Placed)).Area;
                if (overlap > s.Cuts.Sum(x => OffcutPlanner.FitTolerance(x.Placed))) failures.Add($"{c}：{s.Label} 內的切割片重疊 {overlap:0.####} cm²");
            }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(20)));
    }
}
