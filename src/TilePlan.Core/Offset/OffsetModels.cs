using TilePlan.Core.Geometry;

namespace TilePlan.Core.Offset;

/// <summary>某面牆的切條狀況。</summary>
/// <param name="MinWidth">貼著這面牆的切割片中，最小的切片深度（cm）；沒有切割片時為 null。</param>
/// <param name="AngleToGridDeg">牆與磚縫的夾角（0°–45°）。</param>
public sealed record WallStrip(int Index, string Name, double Threshold, double? MinWidth, bool Oblique, double AngleToGridDeg)
{
    public bool Satisfied => MinWidth is null || MinWidth >= Threshold - 1e-6;

    /// <summary>邊的兩端（世界座標），畫面標示用。</summary>
    public Vec2 A { get; init; }
    public Vec2 B { get; init; }

    /// <summary>true 表示這是不鋪設區的邊緣，而不是房間的牆。</summary>
    public bool IsExclusionEdge { get; init; }

    public string Display => IsExclusionEdge ? Name : $"牆 {Name}";

    /// <summary>
    /// 顯示用的數值（0.1 cm）：門檻一律進位；實際值不足時捨去、達標時進位。
    /// 這樣顯示的大小關係永遠與判定一致，不會出現「門檻 40.0、實際 40.0」卻判定不足（39.96 四捨五入）的情況。
    /// </summary>
    public double DisplayThreshold => Tenths.Ceiling(Threshold);

    public double? DisplayMinWidth => MinWidth is { } m ? (Satisfied ? Tenths.Ceiling(m) : Tenths.Floor(m)) : null;
}

public static class Tenths
{
    // 加減 1e-6 吸收浮點誤差，讓剛好是 0.1 倍數的值（例如 19.999999999）不會被多捨或多進一格
    public static double Floor(double v) => Math.Floor(v * 10 + 1e-6) / 10;
    public static double Ceiling(double v) => Math.Ceiling(v * 10 - 1e-6) / 10;
}

public sealed record OffsetReport(double OffsetU, double OffsetV, IReadOnlyList<WallStrip> Walls, IReadOnlyList<string> Reasons)
{
    public bool Satisfied => Walls.All(w => w.Satisfied);

    /// <summary>所有牆邊切條中最窄的寬度（cm）；沒有任何牆邊切割片時為 null。</summary>
    public double? MinWidth
    {
        get
        {
            var widths = Walls.Where(w => w.MinWidth is not null).Select(w => w.MinWidth!.Value).ToList();
            return widths.Count == 0 ? null : widths.Min();
        }
    }

    public IEnumerable<WallStrip> Violations => Walls.Where(w => !w.Satisfied);
}
