namespace TilePlan.Core.Geometry;

/// <summary>面積；內部以 cm² 保存，顯示時換算為 m² 與坪（1 坪 = 400/121 m²）。</summary>
public readonly record struct Area(double Cm2)
{
    public const double Cm2PerM2 = 10_000;

    public static Area FromCm2(double cm2) => new(cm2);

    public double M2 => Cm2 / Cm2PerM2;

    public double Ping => M2 * 121 / 400;

    public decimal M2Rounded => RoundForDisplay(M2);

    /// <summary>報價的工資以此值計算（決策 D2：使用顯示的兩位小數坪數）。</summary>
    public decimal PingRounded => RoundForDisplay(Ping);

    private static decimal RoundForDisplay(double value) =>
        // 先轉 decimal 再捨入：避免 0.125 在 double 中是 0.12499999… 而捨成 0.12
        Math.Round(Math.Round((decimal)value, 9), 2, MidpointRounding.AwayFromZero);
}
