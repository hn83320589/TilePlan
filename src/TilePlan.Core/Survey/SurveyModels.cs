using TilePlan.Core.Geometry;

namespace TilePlan.Core.Survey;

/// <summary>對角線量測：頂點索引（A = 0）與長度（cm）。</summary>
public sealed record Diagonal(int From, int To, double Length);

/// <summary>
/// 現場實測：第 i 面牆連接頂點 i 與 i+1。預設沿房間逆時針量（房間在左手邊）。
/// </summary>
public sealed record SurveyInput(IReadOnlyList<double> Walls, IReadOnlyList<Diagonal> Diagonals, bool Clockwise = false);

public enum SurveyStatus
{
    Solved,
    /// <summary>量測不足，形狀不唯一。</summary>
    Underdetermined,
    /// <summary>量測互相矛盾，無解。</summary>
    Inconsistent,
    /// <summary>剛性足夠，但有多個合法形狀。</summary>
    Ambiguous,
}

/// <summary>某條量測在解出的形狀上的殘差（實際距離 − 量測值）。</summary>
public sealed record MeasurementResidual(int From, int To, double Measured, double Residual);

public sealed record SurveyResult(
    SurveyStatus Status,
    Polygon? Polygon,
    string Message,
    int MissingCount,
    IReadOnlyList<(int From, int To)> SuggestedDiagonals,
    IReadOnlyList<MeasurementResidual> Residuals)
{
    public double MaxResidual => Residuals.Count == 0 ? 0 : Residuals.Max(r => Math.Abs(r.Residual));

    /// <summary>
    /// 長度同樣吻合、但需要對角線穿過牆外才量得到的其他形狀。
    /// 若現場其實是其中一個，代表對角線兩端的角落記錯了。
    /// </summary>
    public IReadOnlyList<Polygon> Alternatives { get; init; } = [];
}
