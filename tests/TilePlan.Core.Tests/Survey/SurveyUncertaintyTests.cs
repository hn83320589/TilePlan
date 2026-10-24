using TilePlan.Core.Geometry;
using TilePlan.Core.Survey;

namespace TilePlan.Core.Tests.Survey;

/// <summary>
/// 量測剛好足夠時殘差必然為 0，不能拿來判斷量得準不準；改以捲尺精度推算各角位置的不確定範圍。
/// </summary>
public class SurveyUncertaintyTests
{
    private static readonly Vec2[] LShape =
        [new(0, 0), new(400, 0), new(400, 150), new(250, 150), new(250, 300), new(0, 300)];

    private static double[] WallsOf(Vec2[] pts) =>
        pts.Select((p, i) => p.DistanceTo(pts[(i + 1) % pts.Length])).ToArray();

    private static Diagonal Diag(Vec2[] pts, int i, int j) => new(i, j, pts[i].DistanceTo(pts[j]));

    private static SurveyInput ExactL(double precisionCm = 0.5) =>
        new(WallsOf(LShape), [Diag(LShape, 0, 2), Diag(LShape, 0, 3), Diag(LShape, 0, 4)], TapePrecisionCm: precisionCm);

    [Fact]
    public void Just_enough_measurements_say_errors_cannot_be_checked_instead_of_showing_zero_residual()
    {
        var result = SurveySolver.Solve(ExactL());

        Assert.Equal(SurveyStatus.Solved, result.Status);
        Assert.Equal(0, result.Redundancy);
        Assert.Contains("沒有多餘量測", result.Message);
        Assert.DoesNotContain("殘差", result.Message);
    }

    [Fact]
    public void Extra_measurement_counts_as_redundancy()
    {
        var d = Math.Sqrt(300 * 300 + 200 * 200);
        var result = SurveySolver.Solve(new SurveyInput([300, 200, 300, 200], [new(0, 2, d), new(1, 3, d)]));

        Assert.Equal(1, result.Redundancy);
        Assert.DoesNotContain("沒有多餘量測", result.Message);
    }

    [Fact]
    public void Suggested_check_diagonal_is_measurable_and_reduces_uncertainty()
    {
        var input = ExactL();
        var result = SurveySolver.Solve(input);

        var (from, to) = Assert.IsType<(int, int)>(result.CheckDiagonal);
        Assert.True(SurveySolver.DiagonalIsInterior(new Polygon(LShape), from, to), "建議的檢核對角線必須在室內量得到");
        Assert.DoesNotContain(input.Diagonals, d => (d.From, d.To) == (from, to) || (d.From, d.To) == (to, from));
        Assert.Contains($"{SurveySolver.LabelPair(from, to)}", result.Message);

        var checkedResult = SurveySolver.Solve(input with { Diagonals = [.. input.Diagonals, Diag(LShape, from, to)] });
        Assert.Equal(1, checkedResult.Redundancy);
        Assert.True(checkedResult.VertexUncertainty.Max() < result.VertexUncertainty.Max(),
            $"多量一條後最大不確定範圍應變小：{result.VertexUncertainty.Max():0.00} → {checkedResult.VertexUncertainty.Max():0.00}");
    }

    [Fact]
    public void Uncertainty_scales_with_tape_precision_and_is_zero_at_the_fixed_corner()
    {
        var coarse = SurveySolver.Solve(ExactL(1.0));
        var fine = SurveySolver.Solve(ExactL(0.5));

        Assert.Equal(0, fine.VertexUncertainty[0], 9);                 // A 固定在原點（座標系的基準）
        Assert.Equal(2 * fine.VertexUncertainty.Max(), coarse.VertexUncertainty.Max(), 6);
    }

    [Fact]
    public void Predicted_95_percent_range_matches_monte_carlo()
    {
        // 每條量測加上常態誤差（95% 落在 ±0.5 cm 內），重複求解 2,000 次，
        // 比對各角位置的實際分布與 Jacobian 推算的範圍
        const double precision = 0.5;
        var predicted = SurveySolver.Solve(ExactL(precision));
        var truth = predicted.Polygon!.Vertices.ToArray();
        var rng = new Random(20261024);
        double sigma = precision / 1.96;
        double Gauss() => Math.Sqrt(-2 * Math.Log(1 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble()) * sigma;

        var input = ExactL(precision);
        var samples = new List<Vec2[]>();
        for (int t = 0; t < 2000; t++)
        {
            var noisy = new SurveyInput(
                input.Walls.Select(w => w + Gauss()).ToArray(),
                input.Diagonals.Select(d => d with { Length = d.Length + Gauss() }).ToList());
            var r = SurveySolver.Solve(noisy);
            if (r.Status == SurveyStatus.Solved) samples.Add(r.Polygon!.Vertices.ToArray());
        }
        Assert.True(samples.Count > 1990);

        for (int v = 1; v < LShape.Length; v++)
        {
            // 實際偏移量的 95 百分位，應與推算的 95% 範圍相近（推算採用橢圓長軸，略為保守）
            var offsets = samples.Select(s => s[v].DistanceTo(truth[v])).Order().ToList();
            double empirical95 = offsets[(int)(offsets.Count * 0.95)];
            double estimate = predicted.VertexUncertainty[v];
            Assert.True(estimate >= empirical95 * 0.9 && estimate <= empirical95 * 1.35,
                $"頂點 {SurveySolver.Label(v)}：推算 ±{estimate:0.00} cm，模擬 95% 為 {empirical95:0.00} cm");
        }
    }
}
