using TilePlan.Core.Geometry;

namespace TilePlan.Core.Survey;

/// <summary>
/// 誤差傳播：在解出的形狀上線性化，座標共變異數 Σ = σ²(JᵀJ)⁻¹，σ = 捲尺精度 / 1.96（精度視為 95% 範圍）。
/// 每個角的不確定範圍取 95% 信賴橢圓的長軸半徑 √(5.991 · λmax)；
/// 任一頂點對距離的變異數為 gᵀΣg，g 為兩點連線方向的梯度。
/// </summary>
internal static class Uncertainty
{
    /// <summary>自由度 2 的卡方分布 95% 分位數。</summary>
    private const double ChiSquare2Dof95 = 5.991;

    public sealed record CheckSuggestion(int From, int To, double Expected, double Range);

    public static (IReadOnlyList<double> Vertices, CheckSuggestion? Check) Of(
        Vec2[] p, IReadOnlyList<(int From, int To, double Length)> bars, SurveyInput input)
    {
        var inverse = LeastSquares.InverseNormalMatrix(p, bars);
        if (inverse is null || !(input.TapePrecisionCm > 0)) return (p.Select(_ => 0.0).ToList(), null);
        double variance = Math.Pow(input.TapePrecisionCm / 1.96, 2);

        var vertices = new List<double>(p.Length);
        for (int v = 0; v < p.Length; v++)
        {
            double a = Cov(inverse, LeastSquares.VarX(v), LeastSquares.VarX(v));
            double b = Cov(inverse, LeastSquares.VarX(v), LeastSquares.VarY(v));
            double c = Cov(inverse, LeastSquares.VarY(v), LeastSquares.VarY(v));
            double lambdaMax = (a + c) / 2 + Math.Sqrt(Math.Pow((a - c) / 2, 2) + b * b);
            vertices.Add(Math.Sqrt(ChiSquare2Dof95 * variance * Math.Max(0, lambdaMax)));
        }

        // 最能檢核的對角線：在室內量得到、尚未量過、推算誤差最大（量它最容易發現前面量錯）
        var polygon = new Polygon(p);
        var measured = bars.Select(b => (Math.Min(b.From, b.To), Math.Max(b.From, b.To))).ToHashSet();
        CheckSuggestion? best = null;
        for (int i = 0; i < p.Length; i++)
            for (int j = i + 2; j < p.Length; j++)
            {
                if (i == 0 && j == p.Length - 1) continue;
                if (measured.Contains((i, j)) || !SurveySolver.DiagonalIsInterior(polygon, i, j)) continue;
                var g = (p[i] - p[j]).Normalized();
                double var = variance * Quadratic(inverse, [(LeastSquares.VarX(i), g.X), (LeastSquares.VarY(i), g.Y),
                                                            (LeastSquares.VarX(j), -g.X), (LeastSquares.VarY(j), -g.Y)]);
                // 比對時，新量的這一條本身也有捲尺誤差，兩者的變異數相加
                double range = 1.96 * Math.Sqrt(Math.Max(0, var) + variance);
                if (best is null || range > best.Range) best = new CheckSuggestion(i, j, p[i].DistanceTo(p[j]), range);
            }
        return (vertices, best);
    }

    private static double Cov(double[,] m, int r, int c) => r < 0 || c < 0 ? 0 : m[r, c];

    private static double Quadratic(double[,] m, (int Index, double Value)[] g)
    {
        double sum = 0;
        foreach (var (r, gr) in g)
            foreach (var (c, gc) in g)
                sum += gr * gc * Cov(m, r, c);
        return sum;
    }
}
