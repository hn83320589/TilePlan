using TilePlan.Core.Geometry;

namespace TilePlan.Core.Tiles;

public enum PatternKind
{
    /// <summary>正排</summary>
    Straight,
    /// <summary>交丁（錯縫）</summary>
    RunningBond,
    /// <summary>45 度斜貼</summary>
    Diagonal,
}

public enum BondRatio
{
    Half,
    Third,
}

/// <param name="LongSideAlongBaseline">磚的長邊是否沿基準線方向（交丁的錯縫也沿此方向）。</param>
public sealed record Pattern(PatternKind Kind, BondRatio Bond = BondRatio.Half, bool LongSideAlongBaseline = true)
{
    public static readonly Pattern Straight = new(PatternKind.Straight);
    public static readonly Pattern Diagonal = new(PatternKind.Diagonal);
    public static Pattern RunningBond(BondRatio bond) => new(PatternKind.RunningBond, bond);

    /// <summary>每隔幾排錯縫循環一次：正排與斜貼為 1，交丁 1/2 為 2，交丁 1/3 為 3。</summary>
    public int BondPeriod => Kind == PatternKind.RunningBond ? (Bond == BondRatio.Half ? 2 : 3) : 1;

    public string DisplayName => Kind switch
    {
        PatternKind.Straight => "正排",
        PatternKind.RunningBond => Bond == BondRatio.Half ? "交丁 1/2" : "交丁 1/3",
        PatternKind.Diagonal => "45° 斜貼",
        _ => Kind.ToString(),
    };
}

/// <summary>排版基準線：通過 Origin、方向為 AngleDeg（0° = +x）。磚格的 u 軸沿此方向。</summary>
public sealed record Baseline(Vec2 Origin, double AngleDeg)
{
    public static readonly Baseline Default = new(Vec2.Zero, 0);
}
