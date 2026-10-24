using TilePlan.Core.Geometry;
using TilePlan.Core.Survey;
using TilePlan.Core.Tiles;

namespace TilePlan.Core.Offset;

public enum WallAxis
{
    /// <summary>牆沿 u 軸（磚的長向）：切條沿 v 方向量，門檻 = TileV / 3。</summary>
    AlongU,
    /// <summary>牆沿 v 軸：切條沿 u 方向量，門檻 = TileU / 3。</summary>
    AlongV,
    /// <summary>與磚縫不平行：門檻 = 短邊 / 3。</summary>
    Oblique,
}

/// <summary>房間的一面牆，同時保存世界座標與磚格座標的幾何資料。</summary>
internal sealed class Wall
{
    /// <summary>夾角小於此值（度）視為與磚縫平行。</summary>
    public const double AlignToleranceDeg = 0.5;

    public required int Index { get; init; }
    public required string Name { get; init; }
    public required Vec2 A { get; init; }
    public required Vec2 B { get; init; }
    /// <summary>指向房間內側的單位法向量（世界座標）。</summary>
    public required Vec2 Inward { get; init; }
    public required Vec2 LatticeA { get; init; }
    public required Vec2 LatticeDir { get; init; }
    public required Vec2 LatticeInward { get; init; }
    public required double Length { get; init; }
    public required WallAxis Axis { get; init; }
    public required double AngleToGridDeg { get; init; }
    public required double Threshold { get; init; }

    public Vec2 LatticeB => LatticeA + LatticeDir * Length;

    public static List<Wall> Of(Polygon room, Lattice lattice)
    {
        bool ccw = room.IsCounterClockwise;
        var walls = new List<Wall>();
        for (int i = 0, n = room.Count; i < n; i++)
        {
            var a = room[i];
            var b = room[(i + 1) % n];
            double length = a.DistanceTo(b);
            if (length < 1e-6) continue;
            var dir = (b - a) / length;
            var inward = ccw ? dir.Perp() : -dir.Perp();
            var la = lattice.ToLattice(a);
            var ldir = (lattice.ToLattice(b) - la) / length;

            double theta = Math.Atan2(ldir.Y, ldir.X) * 180 / Math.PI;
            double m = Lattice.Mod(theta, 90);
            double angle = Math.Min(m, 90 - m);
            var axis = angle >= AlignToleranceDeg ? WallAxis.Oblique
                : Math.Abs(ldir.X) > Math.Abs(ldir.Y) ? WallAxis.AlongU : WallAxis.AlongV;
            double threshold = axis switch
            {
                WallAxis.AlongU => lattice.TileV / 3,
                WallAxis.AlongV => lattice.TileU / 3,
                _ => Math.Min(lattice.TileU, lattice.TileV) / 3,
            };

            walls.Add(new Wall
            {
                Index = i,
                Name = SurveySolver.LabelPair(i, (i + 1) % n),
                A = a,
                B = b,
                Inward = inward,
                LatticeA = la,
                LatticeDir = ldir,
                LatticeInward = ccw ? ldir.Perp() : -ldir.Perp(),
                Length = length,
                Axis = axis,
                AngleToGridDeg = angle,
                Threshold = threshold,
            });
        }
        return walls;
    }

    /// <summary>兩面牆是否平行（不論朝向），容許 2°。</summary>
    public bool IsParallelTo(Wall other) => Math.Abs(LatticeDir.Cross(other.LatticeDir)) < Math.Sin(2 * Math.PI / 180);

    /// <summary>兩面牆是否互相面對（平行、朝向相反、沿牆方向的投影有重疊）。</summary>
    public bool Faces(Wall other)
    {
        if (!IsParallelTo(other) || LatticeInward.Dot(other.LatticeInward) > 0) return false;
        double t0 = (other.LatticeA - LatticeA).Dot(LatticeDir);
        double t1 = (other.LatticeB - LatticeA).Dot(LatticeDir);
        return Math.Max(t0, t1) > 1e-3 && Math.Min(t0, t1) < Length - 1e-3
               && (other.LatticeA - LatticeA).Dot(LatticeInward) > 0;
    }
}
