using TilePlan.Core.Geometry;
using TilePlan.Core.Tiles;

namespace TilePlan.Core.Tests;

/// <summary>性質測試用的隨機房間（固定種子，結果可重現）。</summary>
internal static class RandomRooms
{
    public const int Seed = 20261024;

    public sealed record Case(int No, string Kind, Region Region, TileSpec Tile, Pattern Pattern, Baseline Baseline, double OffsetU, double OffsetV)
    {
        public override string ToString() =>
            $"#{No} {Kind} 磚 {Tile.LengthCm:0.#}×{Tile.WidthCm:0.#} 縫 {Tile.JointMm:0.#}mm {Pattern.DisplayName} 基準 {Baseline.AngleDeg:0.#}°";
    }

    public static IEnumerable<Case> Generate(int count)
    {
        var rng = new Random(Seed);
        for (int n = 0; n < count; n++)
        {
            var (kind, room) = (n % 3) switch
            {
                0 => ("直角多邊形", Orthogonal(rng)),
                1 => ("星形多邊形", StarShaped(rng)),
                _ => ("斜牆多邊形", Chamfered(rng)),
            };
            var exclusions = Enumerable.Range(0, rng.Next(0, 3)).Select(_ => RandomExclusion(rng, room.Bounds)).ToList();
            var region = PolygonOps.Difference(room, PolygonOps.Union(exclusions.Select(e => new Region(e))));

            double length = Round(rng, 15, 80);
            double width = Math.Min(length, Round(rng, 15, 60));
            var tile = new TileSpec(length, width, 9, Math.Round(rng.NextDouble() * 5, 1), 10, 1000m);
            Pattern pattern = (n / 3 % 4) switch
            {
                0 => Pattern.Straight,
                1 => Pattern.RunningBond(BondRatio.Half),
                2 => Pattern.RunningBond(BondRatio.Third),
                _ => Pattern.Diagonal,
            };
            var baseline = new Baseline(new Vec2(Round(rng, 0, 200), Round(rng, 0, 200)), Math.Round(rng.NextDouble() * 180, 1));
            yield return new Case(n, kind, region, tile, pattern, baseline, Round(rng, 0, 100), Round(rng, 0, 100));
        }
    }

    private static double Round(Random rng, double min, double max) => Math.Round(min + rng.NextDouble() * (max - min), 1);

    /// <summary>2–3 個互相重疊的長方形聯集：L、T、U 形。</summary>
    private static Region Orthogonal(Random rng)
    {
        double w = Round(rng, 150, 450), h = Round(rng, 150, 400);
        var rects = new List<Region> { new(TestShapes.Rect(0, 0, w, h)) };
        for (int k = rng.Next(1, 3); k > 0; k--)
        {
            double rw = Round(rng, 60, 250), rh = Round(rng, 60, 250);
            double x = Round(rng, -rw + 30, w - 30), y = Round(rng, -rh + 30, h - 30);
            rects.Add(new Region(TestShapes.Rect(x, y, rw, rh)));
        }
        return PolygonOps.Union(rects);
    }

    /// <summary>角度排序、半徑隨機：保證不自交，常有內凹角與斜牆。</summary>
    private static Region StarShaped(Random rng)
    {
        int k = rng.Next(5, 11);
        var angles = Enumerable.Range(0, k).Select(_ => rng.NextDouble() * 2 * Math.PI).Order().ToList();
        var pts = angles.Select(a => Vec2.FromAngle(a) * Round(rng, 100, 280)).ToArray();
        return new Region(new Polygon(pts));
    }

    /// <summary>長方形四角隨機切斜角。</summary>
    private static Region Chamfered(Random rng)
    {
        double w = Round(rng, 150, 450), h = Round(rng, 150, 400);
        double c() => Round(rng, 0, Math.Min(w, h) / 3);
        double c0 = c(), c1 = c(), c2 = c(), c3 = c();
        var pts = new List<Vec2>
        {
            new(c0, 0), new(w - c1, 0), new(w, c1), new(w, h - c2), new(w - c2, h), new(c3, h), new(0, h - c3), new(0, c0),
        };
        var dedup = pts.Where((p, i) => p.DistanceTo(pts[(i + 1) % pts.Count]) > 1e-9).ToArray();
        return new Region(new Polygon(dedup));
    }

    private static Polygon RandomExclusion(Random rng, Bounds b)
    {
        double w = Round(rng, 20, 90), h = Round(rng, 20, 90);
        var center = new Vec2(Round(rng, b.MinX, b.MaxX), Round(rng, b.MinY, b.MaxY));
        double angle = rng.NextDouble() < 0.5 ? 0 : rng.NextDouble() * Math.PI;
        Vec2[] corners = [new(-w / 2, -h / 2), new(w / 2, -h / 2), new(w / 2, h / 2), new(-w / 2, h / 2)];
        return new Polygon(corners.Select(c => center + c.Rotate(angle)).ToArray());
    }
}
