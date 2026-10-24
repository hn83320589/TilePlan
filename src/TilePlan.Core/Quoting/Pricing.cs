using TilePlan.Core.Tiles;

namespace TilePlan.Core.Quoting;

/// <summary>專案的單價與材料參數（可由 UI 直接繫結編輯）。</summary>
public sealed class Pricing
{
    public double AdhesiveKgPerM2 { get; set; } = 5;
    public double AdhesiveKgPerBag { get; set; } = 20;
    public decimal AdhesivePricePerBag { get; set; } = 280;

    /// <summary>填縫劑乾密度（kg/m³）。</summary>
    public double GroutDensityKgPerM3 { get; set; } = 1600;
    public double GroutKgPerBag { get; set; } = 2;
    public decimal GroutPricePerBag { get; set; } = 150;

    public decimal LaborStraightPerPing { get; set; } = 3500;
    public decimal LaborRunningBondPerPing { get; set; } = 3800;
    public decimal LaborDiagonalPerPing { get; set; } = 4500;

    public decimal WasteStraight { get; set; } = 0.05m;
    public decimal WasteRunningBond { get; set; } = 0.05m;
    public decimal WasteDiagonal { get; set; } = 0.10m;

    /// <summary>每次回傳新實例，避免共用的預設值被某處修改。</summary>
    public static Pricing Default => new();

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
