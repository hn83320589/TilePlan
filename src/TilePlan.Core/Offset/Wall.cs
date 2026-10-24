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

/// <summary>
/// 要檢查切條寬度的一條邊：房間的牆，或不鋪設區露在可鋪區域內的邊緣（例如廚具底座前緣）。
/// 同時保存世界座標與磚格座標的幾何資料。
/// </summary>
internal sealed class Wall
{
    /// <summary>夾角小於此值（度）視為與磚縫平行。</summary>
    public const double AlignToleranceDeg = 0.5;

    /// <summary>不鋪設區邊緣的索引從這裡開始，與房間牆的索引區隔。</summary>
    public const int ExclusionIndexBase = 1000;

    public required int Index { get; init; }
    public required string Name { get; init; }
    public required bool IsExclusionEdge { get; init; }
    public required Vec2 A { get; init; }
    public required Vec2 B { get; init; }
    /// <summary>指向可鋪區域內側的單位法向量（世界座標）。</summary>
    public required Vec2 Inward { get; init; }
    public required Vec2 LatticeA { get; init; }
    public required Vec2 LatticeDir { get; init; }
    public required Vec2 LatticeInward { get; init; }
    public required double Length { get; init; }
    public required WallAxis Axis { get; init; }
    public required double AngleToGridDeg { get; init; }
    public required double Threshold { get; init; }

    /// <summary>沿邊界相鄰的邊（判斷轉角片用）。</summary>
    public Wall? Previous { get; set; }
    public Wall? Next { get; set; }

    public Vec2 LatticeB => LatticeA + LatticeDir * Length;

    /// <summary>給人看的名稱：牆寫成「牆 A–B」，不鋪設區邊緣寫成「廚具底座 第 2 邊」。</summary>
    public string Display => IsExclusionEdge ? Name : $"牆 {Name}";

    public static List<Wall> Of(Polygon room, Lattice lattice)
    {
        bool ccw = room.IsCounterClockwise;
        int n = room.Count;
        var walls = new List<Wall>();
        for (int i = 0; i < n; i++)
        {
            var (a, b) = (room[i], room[(i + 1) % n]);
            if (a.DistanceTo(b) < 1e-6) continue;
            var dir = (b - a).Normalized();
            walls.Add(Create(i, SurveySolver.LabelPair(i, (i + 1) % n), false, a, b, ccw ? dir.Perp() : -dir.Perp(), lattice));
        }
        LinkRing(walls);
        return walls;
    }

    /// <summary>
    /// 不鋪設區露在可鋪區域內的邊：取可鋪區域的邊界中「不落在房間牆上」的邊，
    /// 再對回它屬於哪個不鋪設區的第幾邊。只列出 <paramref name="exclusions"/> 中的不鋪設區。
    /// Clipper 輸出的外框逆時針、洞順時針，可鋪區域一律在邊的左側。
    /// </summary>
    public static List<Wall> ExclusionEdges(Polygon room, Region region, IReadOnlyList<(string Name, Polygon Shape)> exclusions, Lattice lattice)
    {
        var result = new List<Wall>();
        if (exclusions.Count == 0) return result;
        int index = ExclusionIndexBase;
        foreach (var ring in region.Polygons)
        {
            var ringWalls = new List<Wall>();
            for (int i = 0, n = ring.Count; i < n; i++)
            {
                var (a, b) = (ring[i], ring[(i + 1) % n]);
                if (a.DistanceTo(b) < 1e-6) continue;
                var source = OnRoomWall(room, a, b) ? null : FindExclusionEdge(exclusions, (a + b) / 2);
                string name = source is { } s ? $"{s.Name} 第 {s.Edge + 1} 邊" : "";
                ringWalls.Add(Create(source is null ? -1 : index++, name, true, a, b, (b - a).Normalized().Perp(), lattice));
            }
            LinkRing(ringWalls);
            // 只回傳屬於要檢查的不鋪設區、且長度至少 1 cm 的邊；其他邊只當作轉角判斷用的鄰邊
            result.AddRange(ringWalls.Where(w => w.Index >= 0 && w.Length >= 1));
        }
        return result;
    }

    private static Wall Create(int index, string name, bool exclusion, Vec2 a, Vec2 b, Vec2 inward, Lattice lattice)
    {
        double length = a.DistanceTo(b);
        var la = lattice.ToLattice(a);
        var ldir = (lattice.ToLattice(b) - la) / length;
        var linward = new Vec2(inward.Dot(lattice.AxisU), inward.Dot(lattice.AxisV));

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

        return new Wall
        {
            Index = index,
            Name = name,
            IsExclusionEdge = exclusion,
            A = a,
            B = b,
            Inward = inward,
            LatticeA = la,
            LatticeDir = ldir,
            LatticeInward = linward,
            Length = length,
            Axis = axis,
            AngleToGridDeg = angle,
            Threshold = threshold,
        };
    }

    private static void LinkRing(List<Wall> ring)
    {
        for (int i = 0; i < ring.Count; i++)
        {
            ring[i].Previous = ring[(i + ring.Count - 1) % ring.Count];
            ring[i].Next = ring[(i + 1) % ring.Count];
        }
    }

    private const double OnLine = 1e-3;

    private static bool OnRoomWall(Polygon room, Vec2 a, Vec2 b) =>
        room.Edges().Any(e => OnSegment(e.A, e.B, a) && OnSegment(e.A, e.B, b));

    private static (string Name, int Edge)? FindExclusionEdge(IReadOnlyList<(string Name, Polygon Shape)> exclusions, Vec2 p)
    {
        foreach (var (name, shape) in exclusions)
            for (int i = 0; i < shape.Count; i++)
                if (OnSegment(shape[i], shape[(i + 1) % shape.Count], p)) return (name, i);
        return null;
    }

    private static bool OnSegment(Vec2 a, Vec2 b, Vec2 p)
    {
        double len = a.DistanceTo(b);
        if (len < 1e-9) return false;
        if (Math.Abs(Segment.SignedDistanceToLine(a, b, p)) > OnLine) return false;
        double t = (p - a).Dot(b - a) / (len * len);
        return t >= -OnLine / len && t <= 1 + OnLine / len;
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
