using TilePlan.Core.Projects;

namespace TilePlan.Web.Services;

public static class RoomFactory
{
    /// <summary>新房間預設為 300 × 200 的長方形，使用專案中的第一種磁磚（沒有就建立一種）。</summary>
    public static Room NewRectangle(Project project, string name)
    {
        if (project.Tiles.Count == 0) project.Tiles.Add(new TileItem { Name = "磁磚 60×30" });
        return new Room
        {
            Name = name,
            TileId = project.Tiles[0].Id,
            Shape = new RoomShape
            {
                Kind = ShapeKind.Polygon,
                Vertices = [new(0, 0), new(300, 0), new(300, 200), new(0, 200)],
            },
        };
    }
}
