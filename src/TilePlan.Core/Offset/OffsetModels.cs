namespace TilePlan.Core.Offset;

/// <summary>某面牆的切條狀況。</summary>
/// <param name="MinWidth">貼著這面牆的切割片中，最小的切片深度（cm）；沒有切割片時為 null。</param>
/// <param name="AngleToGridDeg">牆與磚縫的夾角（0°–45°）。</param>
public sealed record WallStrip(int Index, string Name, double Threshold, double? MinWidth, bool Oblique, double AngleToGridDeg)
{
    public bool Satisfied => MinWidth is null || MinWidth >= Threshold - 1e-6;
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
