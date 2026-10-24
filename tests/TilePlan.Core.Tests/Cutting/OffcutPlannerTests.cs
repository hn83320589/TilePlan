using TilePlan.Core.Cutting;
using TilePlan.Core.Geometry;
using TilePlan.Core.Layout;
using TilePlan.Core.Tiles;
using static TilePlan.Core.Tests.TestShapes;

namespace TilePlan.Core.Tests.Cutting;

public class OffcutPlannerTests
{
    private static LayoutResult SymmetricStrips() =>
        // 左右各切 29.5（oᵤ = 29.7）、上下各切 24.4（oᵥ = 24.6）→ 6 欄 × 7 排，整片 20、切割 22
        LayoutEngine.Layout(RectRegion(300, 200), Lattice.Create(Tile(60, 30), Pattern.Straight, Baseline.Default, 29.7, 24.6));

    [Fact]
    public void Without_reuse_every_cut_piece_uses_its_own_tile()
    {
        var layout = SymmetricStrips();
        var plan = OffcutPlanner.Plan(layout, new CutSettings(ReuseOffcuts: false));

        Assert.Equal(22, layout.CutCount);
        Assert.Equal(22, plan.StockCount);
        Assert.Equal(0, plan.ReusedCount);
    }

    [Fact]
    public void Reuse_matches_hand_count()
    {
        // 上下排 60×24.4 共 8 片：餘 60×5.6 太小，各用一片 → 8
        // 左右欄 29.5×30 共 10 片：60 − 29.5 = 30.5 ≥ 29.5，一片磚切兩條 → 5
        // 轉角 29.5×24.4 共 4 片：切完剩 L 形，放得下另一個轉角片 → 2
        var plan = OffcutPlanner.Plan(SymmetricStrips(), CutSettings.Default);

        Assert.Equal(15, plan.StockCount);
        Assert.Equal(7, plan.ReusedCount);
    }

    [Fact]
    public void Every_reused_piece_fits_inside_the_offcut_it_came_from()
    {
        var plan = OffcutPlanner.Plan(SymmetricStrips(), CutSettings.Default);

        Assert.All(plan.Assignments.Where(a => a.FromOffcut), a =>
        {
            Assert.True(PolygonOps.Difference(a.Placed, a.Source).Area <= OffcutPlanner.FitTolerance(a.Placed), $"{a.Piece.Label} 超出餘料");
            Assert.Equal(a.Piece.Area, a.Placed.Area, 3);
        });
    }

    [Fact]
    public void Pieces_cut_from_the_same_tile_do_not_overlap_and_stay_inside_it()
    {
        var plan = OffcutPlanner.Plan(SymmetricStrips(), CutSettings.Default);
        var tile = RectRegion(plan.TileU, plan.TileV);

        Assert.All(plan.StockTiles, s =>
        {
            var union = PolygonOps.Union(s.Cuts.Select(c => c.Placed));
            Assert.True(s.Cuts.Sum(c => c.Placed.Area) - union.Area <= s.Cuts.Sum(c => OffcutPlanner.FitTolerance(c.Placed)));
            Assert.True(PolygonOps.Difference(union, tile).Area <= s.Cuts.Sum(c => OffcutPlanner.FitTolerance(c.Placed)));
        });
    }

    [Fact]
    public void Every_cut_piece_is_assigned_exactly_once()
    {
        var layout = SymmetricStrips();
        var plan = OffcutPlanner.Plan(layout, CutSettings.Default);

        Assert.Equal(layout.CutPieces.Select(p => p.CutNo).Order(), plan.Assignments.Select(a => a.Piece.CutNo).Order());
    }

    [Fact]
    public void Quarter_turn_is_only_used_when_allowed()
    {
        // 一片磚切出 40×30 後剩 20×30；另一片需要 30×20，只有轉 90° 才放得進去
        var lattice = Lattice.Create(Tile(60, 30), Pattern.Straight, Baseline.Default);
        var a = CutPiece(1, Rect(0, 0, 40, 30));
        var b = CutPiece(2, Rect(0, 0, 30, 20));
        var layout = new LayoutResult(lattice, RectRegion(100, 100), [a, b], [], 0);

        Assert.Equal(2, OffcutPlanner.Plan(layout, CutSettings.Default).StockCount);
        var turned = OffcutPlanner.Plan(layout, CutSettings.Default with { AllowQuarterTurns = true });
        Assert.Equal(1, turned.StockCount);
        Assert.Contains(turned.Assignments, x => x.FromOffcut && x.RotationDeg is 90 or 270);
    }

    [Fact]
    public void Kerf_can_prevent_a_tight_reuse()
    {
        // 60 − 29.5 = 30.5：刀口 0.5 時剩 30.25 仍放得下 29.5；刀口 2 cm 時剩 29.5 剛好放得下；刀口 3 cm 就放不下
        var lattice = Lattice.Create(Tile(60, 30), Pattern.Straight, Baseline.Default);
        var layout = new LayoutResult(lattice, RectRegion(100, 100),
            [CutPiece(1, Rect(30.5, 0, 29.5, 30)), CutPiece(2, Rect(0, 0, 29.5, 30))], [], 0);

        Assert.Equal(1, OffcutPlanner.Plan(layout, CutSettings.Default with { KerfCm = 0.5 }).StockCount);
        Assert.Equal(2, OffcutPlanner.Plan(layout, CutSettings.Default with { KerfCm = 3 }).StockCount);
    }

    private static Piece CutPiece(int no, Polygon local) =>
        new(no, 0, PieceKind.Cut, no, new Region(local), new Region(local));
}
