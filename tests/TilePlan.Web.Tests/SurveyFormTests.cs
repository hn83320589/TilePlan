using Bunit;
using TilePlan.Core.Projects;
using TilePlan.Core.Survey;
using TilePlan.Web.Components;

namespace TilePlan.Web.Tests;

public class SurveyFormTests : BunitContext
{
    private static (Room Room, RoomResult Result) LShapeRoom(bool extraDiagonal)
    {
        // L 形：A(0,0) B(400,0) C(400,150) D(250,150) E(250,300) F(0,300)，從 A 量扇形對角線
        var diagonals = new List<Diagonal> { new(0, 2, 427.2), new(0, 3, 291.5), new(0, 4, 390.5) };
        if (extraDiagonal) diagonals.Add(new Diagonal(1, 5, 500.0));
        var project = new Project { Tiles = [new TileItem { Id = "t" }] };
        var room = new Room
        {
            TileId = "t",
            Shape = new RoomShape { Kind = ShapeKind.Survey, Walls = [400, 150, 150, 150, 250, 300], Diagonals = diagonals },
        };
        project.Rooms.Add(room);
        return (room, ProjectCalculator.ComputeRoom(project, room));
    }

    [Fact]
    public void Just_enough_measurements_do_not_show_a_misleading_zero_residual()
    {
        var (room, result) = LShapeRoom(extraDiagonal: false);

        var cut = Render<SurveyForm>(p => p.Add(x => x.Room, room).Add(x => x.Result, result));

        Assert.Equal(0, result.Survey!.Redundancy);
        Assert.DoesNotContain("殘差", cut.Markup);
        Assert.DoesNotContain("0.00 cm", cut.Markup);
        Assert.Contains("沒有多餘量測", cut.Markup);
        Assert.Contains("加入 B–F 作為檢核", cut.Markup);
        Assert.Contains("各角位置誤差最大約", cut.Markup);
    }

    [Fact]
    public void Redundant_measurements_show_residuals()
    {
        var (room, result) = LShapeRoom(extraDiagonal: true);

        var cut = Render<SurveyForm>(p => p.Add(x => x.Room, room).Add(x => x.Result, result));

        Assert.Equal(1, result.Survey!.Redundancy);
        Assert.Contains("最大殘差", cut.Markup);
    }
}
