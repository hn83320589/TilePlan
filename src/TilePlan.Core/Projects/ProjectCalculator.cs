using TilePlan.Core.Cutting;
using TilePlan.Core.Estimation;
using TilePlan.Core.Geometry;
using TilePlan.Core.Layout;
using TilePlan.Core.Offset;
using TilePlan.Core.Quoting;
using TilePlan.Core.Survey;
using TilePlan.Core.Tiles;

namespace TilePlan.Core.Projects;

/// <summary>一間房的計算結果；無法計算時 <see cref="Error"/> 說明原因，其餘欄位可能為 null。</summary>
public sealed record RoomResult(
    Room Room,
    string? Error,
    Polygon? Polygon,
    SurveyResult? Survey,
    Region? Region,
    TileSpec? Tile,
    LayoutResult? Layout,
    OffsetReport? Offset,
    CutPlan? Cuts,
    RoomMaterials? Materials)
{
    public bool Ok => Error is null;
    public Area Area => Area.FromCm2(Region?.Area ?? 0);
}

public sealed record ProjectResult(IReadOnlyList<RoomResult> Rooms, Quote Quote);

/// <summary>整個計算流程的入口：房間形狀 → 可鋪區域 → 偏移 → 排版 → 切割 → 材料 → 報價。純函數。</summary>
public static class ProjectCalculator
{
    public static ProjectResult Compute(Project project)
    {
        var rooms = project.Rooms.Select(r => ComputeRoom(project, r)).ToList();
        return new ProjectResult(rooms, BuildQuote(project, rooms));
    }

    public static Quote BuildQuote(Project project, IEnumerable<RoomResult> rooms) =>
        QuoteBuilder.Build(rooms
            .Where(r => r.Ok)
            .Select(r => new QuoteRoomInput(r.Room.Name, r.Room.TileId, project.FindTile(r.Room.TileId)!.Name, r.Tile!, r.Room.ToPattern(), r.Materials!))
            .ToList(), project.Pricing);

    public static RoomResult ComputeRoom(Project project, Room room)
    {
        var (polygon, survey, shapeError) = ResolveShape(room.Shape);
        if (polygon is null)
            return Fail(room, shapeError!, survey: survey);
        if (!polygon.IsSimple())
            return Fail(room, "房間的邊互相交叉，請調整頂點順序。", polygon, survey);

        var tileItem = project.FindTile(room.TileId);
        if (tileItem is null)
            return Fail(room, "尚未選擇磁磚。", polygon, survey);
        var tile = tileItem.ToSpec();
        try { tile.Validate(); }
        catch (ArgumentException ex) { return Fail(room, ex.Message, polygon, survey); }

        var exclusions = room.Exclusions
            .Where(e => e.Vertices.Count >= 3)
            .Select(e => (Source: e, Shape: new Polygon(e.Vertices.Select(v => v.ToVec()).ToArray())))
            .ToList();
        var region = Region.Tileable(polygon, exclusions.Select(e => e.Shape));
        var checkedEdges = exclusions.Where(e => e.Source.CheckEdges).Select(e => (e.Source.Name, e.Shape)).ToList();
        if (region.IsEmpty)
            return Fail(room, "扣除不鋪設區後沒有可鋪設的面積。", polygon, survey) with { Region = region };

        var template = Lattice.Create(tile, room.ToPattern(), room.ToBaseline(), room.OffsetU, room.OffsetV);
        LayoutResult layout;
        OffsetReport report;
        if (room.OffsetMode == OffsetMode.Auto)
        {
            var optimized = OffsetOptimizer.Optimize(polygon, region, template, checkedEdges);
            (layout, report) = (optimized.Layout, optimized.Report);
        }
        else
        {
            layout = LayoutEngine.Layout(region, template);
            report = OffsetOptimizer.Report(polygon, layout, checkedEdges);
        }

        var cuts = OffcutPlanner.Plan(layout, new CutSettings(room.ReuseOffcuts, room.AllowQuarterTurns, room.KerfMm / 10));
        decimal waste = room.WasteRateOverride ?? project.Pricing.DefaultWasteRate(room.Pattern);
        var materials = MaterialEstimator.Estimate(layout, cuts, tile, waste, project.Pricing.AdhesiveKgPerM2, project.Pricing.GroutDensityKgPerM3);

        return new RoomResult(room, null, polygon, survey, region, tile, layout, report, cuts, materials);
    }

    private static RoomResult Fail(Room room, string error, Polygon? polygon = null, SurveyResult? survey = null) =>
        new(room, error, polygon, survey, null, null, null, null, null, null);

    /// <summary>座標模式直接取頂點；實測模式交給 <see cref="SurveySolver"/>。</summary>
    public static (Polygon? Polygon, SurveyResult? Survey, string? Error) ResolveShape(RoomShape shape)
    {
        if (shape.Kind == ShapeKind.Polygon)
        {
            if (shape.Vertices.Count < 3) return (null, null, "房間至少需要 3 個頂點。");
            var poly = new Polygon(shape.Vertices.Select(v => v.ToVec()).ToArray());
            return poly.Area < 1e-6 ? (null, null, "房間面積為 0。") : (poly, null, null);
        }

        if (shape.Walls.Count < 3) return (null, null, "實測模式至少需要 3 面牆。");
        try
        {
            var result = SurveySolver.Solve(shape.ToSurvey());
            return (result.Polygon, result, result.Status == SurveyStatus.Solved ? null : result.Message);
        }
        catch (ArgumentException ex)
        {
            return (null, null, ex.Message);
        }
    }
}
