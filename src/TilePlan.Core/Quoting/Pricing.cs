using TilePlan.Core.Tiles;

namespace TilePlan.Core.Quoting;

/// <summary>專案的單價與材料參數。</summary>
public sealed record Pricing
{
    public double AdhesiveKgPerM2 { get; init; } = 5;
    public double AdhesiveKgPerBag { get; init; } = 20;
    public decimal AdhesivePricePerBag { get; init; } = 280;

    /// <summary>填縫劑乾密度（kg/m³）。</summary>
    public double GroutDensityKgPerM3 { get; init; } = 1600;
    public double GroutKgPerBag { get; init; } = 2;
    public decimal GroutPricePerBag { get; init; } = 150;

    public decimal LaborStraightPerPing { get; init; } = 3500;
    public decimal LaborRunningBondPerPing { get; init; } = 3800;
    public decimal LaborDiagonalPerPing { get; init; } = 4500;

    public decimal WasteStraight { get; init; } = 0.05m;
    public decimal WasteRunningBond { get; init; } = 0.05m;
    public decimal WasteDiagonal { get; init; } = 0.10m;

    public static readonly Pricing Default = new();

    public decimal LaborPerPing(PatternKind kind) => kind switch
    {
        PatternKind.Straight => LaborStraightPerPing,
        PatternKind.RunningBond => LaborRunningBondPerPing,
        PatternKind.Diagonal => LaborDiagonalPerPing,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public decimal DefaultWasteRate(PatternKind kind) => kind switch
    {
        PatternKind.Straight => WasteStraight,
        PatternKind.RunningBond => WasteRunningBond,
        PatternKind.Diagonal => WasteDiagonal,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
