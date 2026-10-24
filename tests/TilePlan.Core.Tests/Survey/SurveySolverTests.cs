using TilePlan.Core.Geometry;
using TilePlan.Core.Survey;

namespace TilePlan.Core.Tests.Survey;

public class SurveySolverTests
{
    // L 形客廳的已知座標（逆時針）：A B C D E F
    private static readonly Vec2[] LShape =
        [new(0, 0), new(400, 0), new(400, 150), new(250, 150), new(250, 300), new(0, 300)];

    private static double[] WallsOf(Vec2[] pts) =>
        pts.Select((p, i) => p.DistanceTo(pts[(i + 1) % pts.Length])).ToArray();

    private static Diagonal Diag(Vec2[] pts, int i, int j) => new(i, j, pts[i].DistanceTo(pts[j]));

    private static void AssertMatches(Vec2[] expected, Polygon actual, double tolerance)
    {
        Assert.Equal(expected.Length, actual.Count);
        for (int i = 0; i < expected.Length; i++)
            Assert.True(expected[i].DistanceTo(actual[i]) < tolerance,
                $"頂點 {SurveySolver.Label(i)} 誤差 {expected[i].DistanceTo(actual[i]):0.###} cm：預期 {expected[i]}，實際 {actual[i]}");
    }

    [Fact]
    public void L_shape_from_fan_diagonals_matches_known_coordinates()
    {
        var input = new SurveyInput(WallsOf(LShape), [Diag(LShape, 0, 2), Diag(LShape, 0, 3), Diag(LShape, 0, 4)]);

        var result = SurveySolver.Solve(input);

        Assert.Equal(SurveyStatus.Solved, result.Status);
        AssertMatches(LShape, result.Polygon!, 0.5);
    }

    [Fact]
    public void L_shape_from_mixed_diagonals_matches_known_coordinates()
    {
        // 師傅不一定從同一個角量：B–D、A–D、D–F
        var input = new SurveyInput(WallsOf(LShape), [Diag(LShape, 1, 3), Diag(LShape, 0, 3), Diag(LShape, 3, 5)]);

        var result = SurveySolver.Solve(input);

        Assert.Equal(SurveyStatus.Solved, result.Status);
        AssertMatches(LShape, result.Polygon!, 0.5);
    }

    [Fact]
    public void L_shape_with_tape_measure_rounding_stays_within_half_centimetre()
    {
        // 現場量測只到 0.1 cm
        var walls = WallsOf(LShape).Select(w => Math.Round(w, 1)).ToArray();
        var diags = new[] { Diag(LShape, 0, 2), Diag(LShape, 0, 3), Diag(LShape, 0, 4) }
            .Select(d => d with { Length = Math.Round(d.Length, 1) }).ToArray();

        var result = SurveySolver.Solve(new SurveyInput(walls, diags));

        Assert.Equal(SurveyStatus.Solved, result.Status);
        AssertMatches(LShape, result.Polygon!, 0.5);
    }

    [Fact]
    public void Rectangle_without_diagonal_is_underdetermined_and_names_the_missing_measurement()
    {
        var result = SurveySolver.Solve(new SurveyInput([300, 200, 300, 200], []));

        Assert.Equal(SurveyStatus.Underdetermined, result.Status);
        Assert.Null(result.Polygon);
        Assert.Equal(1, result.MissingCount);
        Assert.Contains(result.SuggestedDiagonals, d => (d.From, d.To) is (0, 2) or (1, 3));
        Assert.Contains("缺少 1 條", result.Message);
        Assert.Contains("A–C", result.Message);
    }

    [Fact]
    public void L_shape_with_one_diagonal_reports_two_missing()
    {
        var result = SurveySolver.Solve(new SurveyInput(WallsOf(LShape), [Diag(LShape, 0, 3)]));

        Assert.Equal(SurveyStatus.Underdetermined, result.Status);
        Assert.Equal(2, result.MissingCount);
        Assert.Equal(2, result.SuggestedDiagonals.Count);
    }

    [Fact]
    public void Rectangle_with_one_diagonal_is_a_rectangle()
    {
        var result = SurveySolver.Solve(new SurveyInput([300, 200, 300, 200], [new(0, 2, Math.Sqrt(300 * 300 + 200 * 200))]));

        Assert.Equal(SurveyStatus.Solved, result.Status);
        AssertMatches([new(0, 0), new(300, 0), new(300, 200), new(0, 200)], result.Polygon!, 0.01);
    }

    [Fact]
    public void Out_of_square_room_is_not_forced_to_right_angles()
    {
        // 老公寓常見：對角線比直角時短 3 cm
        var square = Math.Sqrt(300 * 300 + 200 * 200);
        var result = SurveySolver.Solve(new SurveyInput([300, 200, 300, 200], [new(0, 2, square - 3)]));

        Assert.Equal(SurveyStatus.Solved, result.Status);
        var p = result.Polygon!;
        Assert.Equal(square - 3, p[0].DistanceTo(p[2]), 3);
        Assert.True(Math.Abs(p[3].X) > 1, "D 點不應落在 x = 0（那代表系統假設了直角）");
    }

    [Fact]
    public void Trapezoid_with_slanted_wall()
    {
        Vec2[] pts = [new(0, 0), new(320, 0), new(280, 210), new(0, 210)];
        var result = SurveySolver.Solve(new SurveyInput(WallsOf(pts), [Diag(pts, 1, 3)]));

        Assert.Equal(SurveyStatus.Solved, result.Status);
        AssertMatches(pts, result.Polygon!, 0.01);
    }

    [Fact]
    public void Triangle_needs_no_diagonal()
    {
        Vec2[] pts = [new(0, 0), new(300, 0), new(100, 200)];
        var result = SurveySolver.Solve(new SurveyInput(WallsOf(pts), []));

        Assert.Equal(SurveyStatus.Solved, result.Status);
        AssertMatches(pts, result.Polygon!, 0.01);
    }

    [Fact]
    public void Clockwise_walk_produces_clockwise_polygon_with_same_labels()
    {
        Vec2[] cw = [new(0, 0), new(0, 200), new(300, 200), new(300, 0)];
        var result = SurveySolver.Solve(new SurveyInput(WallsOf(cw), [Diag(cw, 0, 2)], Clockwise: true));

        Assert.Equal(SurveyStatus.Solved, result.Status);
        Assert.False(result.Polygon!.IsCounterClockwise);
        Assert.Equal(60_000, result.Polygon.Area, 3);
    }

    [Fact]
    public void Impossible_triangle_is_reported_as_inconsistent()
    {
        var result = SurveySolver.Solve(new SurveyInput([100, 100, 300], []));

        Assert.Equal(SurveyStatus.Inconsistent, result.Status);
        Assert.Contains("三角不等式", result.Message);
    }

    [Fact]
    public void Redundant_measurement_reports_residuals()
    {
        var d = Math.Sqrt(300 * 300 + 200 * 200);
        // 兩條對角線都量，但其中一條量錯 4 cm
        var result = SurveySolver.Solve(new SurveyInput([300, 200, 300, 200], [new(0, 2, d), new(1, 3, d + 4)]));

        Assert.Equal(SurveyStatus.Solved, result.Status);
        Assert.True(result.MaxResidual > 1);
        Assert.Contains("量測可能有誤", result.Message);
    }

    /// <summary>對角線 i–j 是否完全在房間內（捲尺量得到）。</summary>
    private static bool IsInterior(Polygon poly, int i, int j)
    {
        for (int k = 0; k < poly.Count; k++)
        {
            int k2 = (k + 1) % poly.Count;
            if (k == i || k == j || k2 == i || k2 == j) continue;
            if (Segment.Intersects(poly[i], poly[j], poly[k], poly[k2])) return false;
        }
        return poly.Contains((poly[i] + poly[j]) / 2);
    }

    /// <summary>把已知座標轉到求解器的座標系：A 在原點、B 在 +x 軸。</summary>
    private static Vec2[] ToSolverFrame(Vec2[] pts)
    {
        var dir = (pts[1] - pts[0]).Normalized();
        return pts.Select(p => new Vec2((p - pts[0]).Dot(dir), (p - pts[0]).Dot(dir.Perp()))).ToArray();
    }

    [Fact]
    public void L_shape_lists_alternative_shapes_that_need_exterior_diagonals()
    {
        // 光看長度，L 形還能組成另一種形狀，只是那種形狀的對角線會穿過牆外
        var diags = new[] { Diag(LShape, 0, 2), Diag(LShape, 0, 3), Diag(LShape, 0, 4) };
        var result = SurveySolver.Solve(new SurveyInput(WallsOf(LShape), diags));

        Assert.Equal(SurveyStatus.Solved, result.Status);
        AssertMatches(LShape, result.Polygon!, 0.5);
        Assert.NotEmpty(result.Alternatives);
        Assert.All(result.Alternatives, alt =>
            Assert.Contains(diags, d => !IsInterior(alt, d.From, d.To)));
    }

    [Fact]
    public void Mislabelled_diagonals_keep_the_real_room_among_the_alternatives()
    {
        // 從 B 角量扇形對角線：C–E 會穿過 L 的缺口，現實中捲尺量不到（使用者把角落記錯了）。
        // 求解器只能依「對角線在室內」選出另一個形狀，但真實的房間必須出現在候選形狀中，讓使用者認得出來
        var fromB = LShape.Skip(1).Concat(LShape.Take(1)).ToArray();
        var input = new SurveyInput(WallsOf(fromB), [Diag(fromB, 0, 2), Diag(fromB, 0, 3), Diag(fromB, 0, 4)]);

        var result = SurveySolver.Solve(input);

        var truth = ToSolverFrame(fromB);
        Assert.Equal(SurveyStatus.Solved, result.Status);
        Assert.True(truth.Zip(result.Polygon!.Vertices, (e, g) => e.DistanceTo(g)).Max() > 50, "這組輸入解出的主要形狀本來就不是真實的房間");
        Assert.Contains(result.Alternatives, alt => truth.Zip(alt.Vertices, (e, g) => e.DistanceTo(g)).Max() < 0.5);
    }

    [Fact]
    public void Rectangle_has_no_alternative_shapes()
    {
        var result = SurveySolver.Solve(new SurveyInput([300, 200, 300, 200], [new(0, 2, Math.Sqrt(300 * 300 + 200 * 200))]));

        Assert.Empty(result.Alternatives);
    }

    [Fact]
    public void Diagonal_referencing_missing_vertex_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => SurveySolver.Solve(new SurveyInput([300, 200, 300, 200], [new(0, 7, 100)])));
    }
}
