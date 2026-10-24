using System.Text;
using System.Text.Json;
using TilePlan.Core.Projects;
using TilePlan.Core.Tiles;

namespace TilePlan.Core.Tests.Projects;

public class ProjectTests
{
    private static Project Sample() =>
        ProjectSerializer.Deserialize(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", "sample-project.json")));

    /// <summary>把排版與報價結果攤平成字串：任何一片磚的位置、形狀或任何一個金額不同都會反映出來。</summary>
    private static string Fingerprint(ProjectResult result)
    {
        var sb = new StringBuilder();
        foreach (var r in result.Rooms)
        {
            sb.AppendLine($"{r.Room.Id} {r.Error} off={r.Offset?.OffsetU:R},{r.Offset?.OffsetV:R} stock={r.Cuts?.StockCount}");
            foreach (var p in r.Layout?.Pieces ?? [])
                sb.AppendLine($"  {p.I},{p.J} {p.Kind} {p.CutNo} {string.Join(";", p.Shape.Polygons.SelectMany(q => q.Vertices).Select(v => $"{v.X:R},{v.Y:R}"))}");
            foreach (var a in r.Cuts?.Assignments ?? [])
                sb.AppendLine($"  cut {a.Piece.CutNo}->S{a.StockNo} rot={a.RotationDeg} reuse={a.FromOffcut}");
        }
        sb.AppendLine(JsonSerializer.Serialize(result.Quote));
        return sb.ToString();
    }

    [Fact]
    public void Sample_project_contains_bathroom_kitchen_and_L_shaped_living_room()
    {
        var result = ProjectCalculator.Compute(Sample());

        Assert.Equal(["浴室", "廚房", "L 形客廳"], result.Rooms.Select(r => r.Room.Name));
        Assert.All(result.Rooms, r => Assert.True(r.Ok, $"{r.Room.Name}：{r.Error}"));
        Assert.Equal(ShapeKind.Survey, result.Rooms[2].Room.Shape.Kind);
        Assert.Equal(6, result.Rooms[2].Polygon!.Count);
        Assert.Equal(3, result.Rooms.Select(r => r.Room.Pattern).Distinct().Count());
    }

    [Fact]
    public void Export_then_import_gives_identical_layout_and_quote()
    {
        var original = Sample();
        var before = Fingerprint(ProjectCalculator.Compute(original));

        var imported = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(original));
        var after = Fingerprint(ProjectCalculator.Compute(imported));

        Assert.Equal(before, after);
    }

    [Fact]
    public void Serialization_round_trip_is_stable_text()
    {
        var json = ProjectSerializer.Serialize(Sample());
        Assert.Equal(json, ProjectSerializer.Serialize(ProjectSerializer.Deserialize(json)));
        Assert.Contains("\"pattern\": \"runningBond\"", json);
        Assert.Contains("浴室", json);   // 中文不轉成 \uXXXX
    }

    [Fact]
    public void Quote_numbers_match_room_results()
    {
        var result = ProjectCalculator.Compute(Sample());

        for (int i = 0; i < result.Rooms.Count; i++)
        {
            var room = result.Rooms[i];
            var line = result.Quote.Rooms[i];
            Assert.Equal(room.Area.M2Rounded, line.AreaM2);
            Assert.Equal(room.Area.PingRounded, line.AreaPing);
            Assert.Equal(room.Materials!.TilesWithWaste, line.TilesWithWaste);
        }
        foreach (var tile in result.Quote.Tiles)
            Assert.Equal(result.Quote.Rooms.Where(r => r.TileName == tile.TileName).Sum(r => r.TilesWithWaste), tile.Pieces);
    }

    [Fact]
    public void Newer_schema_version_is_rejected_with_a_clear_message()
    {
        var json = ProjectSerializer.Serialize(new Project { SchemaVersion = 99 });

        var ex = Assert.Throws<ProjectFormatException>(() => ProjectSerializer.Deserialize(json));
        Assert.Contains("版本 99", ex.Message);
    }

    [Fact]
    public void Broken_json_is_rejected_with_a_clear_message()
    {
        var ex = Assert.Throws<ProjectFormatException>(() => ProjectSerializer.Deserialize("{ \"rooms\": [ "));
        Assert.Contains("不是有效的 TilePlan 專案檔", ex.Message);
    }

    [Fact]
    public void Survey_room_missing_diagonal_reports_error_instead_of_guessing()
    {
        var project = new Project { Tiles = [new TileItem { Id = "t" }] };
        project.Rooms.Add(new Room
        {
            TileId = "t",
            Shape = new RoomShape { Kind = ShapeKind.Survey, Walls = [300, 200, 300, 200] },
        });

        var room = ProjectCalculator.Compute(project).Rooms[0];

        Assert.False(room.Ok);
        Assert.Null(room.Layout);
        Assert.Contains("缺少 1 條", room.Error);
        Assert.Empty(ProjectCalculator.Compute(project).Quote.Rooms);
    }

    [Fact]
    public void Manual_offset_is_respected()
    {
        var project = Sample();
        var bath = project.Rooms[0];
        bath.OffsetMode = OffsetMode.Manual;
        bath.OffsetU = 5;
        bath.OffsetV = 7;
        bath.Pattern = PatternKind.Straight;

        var room = ProjectCalculator.ComputeRoom(project, bath);

        Assert.Equal(5, room.Layout!.Lattice.OffsetU, 9);
        Assert.Equal(7, room.Layout.Lattice.OffsetV, 9);
    }
}
