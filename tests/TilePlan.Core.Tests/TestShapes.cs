using TilePlan.Core.Geometry;
using TilePlan.Core.Tiles;

namespace TilePlan.Core.Tests;

internal static class TestShapes
{
    public static Polygon Rect(double x, double y, double w, double h) =>
        new([new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h)]);

    public static Region RectRegion(double w, double h) => new(Rect(0, 0, w, h));

    public static TileSpec Tile(double length, double width, double jointMm = 2, double thicknessMm = 9, int perBox = 8, decimal price = 1000m) =>
        new(length, width, thicknessMm, jointMm, perBox, price);
}
