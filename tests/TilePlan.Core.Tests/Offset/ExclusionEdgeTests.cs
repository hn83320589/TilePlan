using TilePlan.Core.Geometry;
using TilePlan.Core.Layout;
using TilePlan.Core.Offset;
using TilePlan.Core.Tiles;
using static TilePlan.Core.Tests.TestShapes;

namespace TilePlan.Core.Tests.Offset;

/// <summary>不鋪設區露在可鋪區域內的邊緣（例如廚具底座前緣），也要能檢查切條寬度。</summary>
public class ExclusionEdgeTests
{
    // 300 × 200 房間，左下角 100 × 55 的櫃子：第 1、4 邊貼著牆，第 2 邊（x = 100）與第 3 邊（y = 55）露在室內
    private static readonly Polygon Room = Rect(0, 0, 300, 200);
    private static readonly Polygon Cabinet = Rect(0, 0, 100, 55);
    private static readonly Region Tileable = Region.Tileable(Room, [Cabinet]);
    private static readonly (string, Polygon)[] Checked = [("櫃子", Cabinet)];

    private static LayoutResult LayoutAtCorner() =>
        LayoutEngine.Layout(Tileable, Lattice.Create(Tile(60, 30), Pattern.Straight, Baseline.Default));

    [Fact]
    public void Only_edges_exposed_inside_the_room_are_checked()
    {
        var report = OffsetOptimizer.Report(Room, LayoutAtCorner(), Checked);

        var edges = report.Walls.Where(w => w.IsExclusionEdge).Select(w => w.Name).Order().ToList();
        Assert.Equal(["櫃子 第 2 邊", "櫃子 第 3 邊"], edges);
    }

    [Fact]
    public void Sliver_above_the_cabinet_is_reported_with_the_cabinet_name()
    {
        // 偏移 0：第 2 排磚 30.2–60.2 → 櫃子上緣 y = 55 上方只剩 5.2 cm（門檻 10）；
        // 第 2 欄磚 60.2–120.2 → 櫃子右緣 x = 100 右側 20.2 cm（門檻 20）
        var report = OffsetOptimizer.Report(Room, LayoutAtCorner(), Checked);

        var top = report.Walls.Single(w => w.Name == "櫃子 第 3 邊");
        var side = report.Walls.Single(w => w.Name == "櫃子 第 2 邊");
        Assert.Equal(5.2, top.MinWidth!.Value, 3);
        Assert.False(top.Satisfied);
        Assert.Equal(20.2, side.MinWidth!.Value, 3);
        Assert.True(side.Satisfied);
        Assert.Contains("1 處不鋪設區邊緣", report.Reasons[0]);
        Assert.Contains(report.Reasons, r => r.Contains("櫃子 第 3 邊"));
    }

    [Fact]
    public void Unchecked_exclusion_edges_are_not_reported()
    {
        var report = OffsetOptimizer.Report(Room, LayoutAtCorner());

        Assert.DoesNotContain(report.Walls, w => w.IsExclusionEdge);
    }

    [Fact]
    public void Pillar_in_the_middle_contributes_all_four_edges()
    {
        var pillar = Rect(120, 70, 40, 40);
        var region = Region.Tileable(Room, [pillar]);
        var layout = LayoutEngine.Layout(region, Lattice.Create(Tile(60, 30), Pattern.Straight, Baseline.Default));

        var report = OffsetOptimizer.Report(Room, layout, [("柱子", pillar)]);

        Assert.Equal(4, report.Walls.Count(w => w.IsExclusionEdge));
    }

    [Fact]
    public void Auto_offset_takes_cabinet_edges_into_account()
    {
        var template = Lattice.Create(Tile(60, 30), Pattern.Straight, Baseline.Default);
        var withEdges = OffsetOptimizer.Optimize(Room, Tileable, template, Checked);
        var withoutEdges = OffsetOptimizer.Optimize(Room, Tileable, template);

        // 用同一把尺（含櫃子邊緣）衡量兩種結果：納入櫃子邊緣後，總不足量不能更差
        static double Shortfall(OffsetReport r) => r.Walls.Sum(w => w.MinWidth is { } m ? Math.Max(0, w.Threshold - m) : 0);
        var judgedWithout = OffsetOptimizer.Report(Room, withoutEdges.Layout, Checked);
        Assert.True(Shortfall(withEdges.Report) <= Shortfall(judgedWithout) + 1e-6,
            $"含櫃子邊緣 {Shortfall(withEdges.Report):0.00}，不含 {Shortfall(judgedWithout):0.00}");
        Assert.True(withEdges.Report.Walls.Single(w => w.Name == "櫃子 第 3 邊").Satisfied, string.Join("\n", withEdges.Report.Reasons));
    }
}
