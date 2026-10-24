using TilePlan.Core.Geometry;
using TilePlan.Core.Layout;
using TilePlan.Core.Offset;
using TilePlan.Core.Tiles;
using static TilePlan.Core.Tests.TestShapes;

namespace TilePlan.Core.Tests.Offset;

public class OffsetOptimizerTests
{
    private static OffsetResult Optimize(Polygon room, TileSpec tile, Pattern pattern, Baseline? baseline = null) =>
        OffsetOptimizer.Optimize(room, new Region(room), Lattice.Create(tile, pattern, baseline ?? Baseline.Default));

    private static double? Width(OffsetReport report, string wall) => report.Walls.Single(w => w.Name == wall).MinWidth;

    [Fact]
    public void Rectangle_300x200_gets_symmetric_strips_above_one_third()
    {
        // x：300 = 2a + 3×60 + 4×0.2 → a = 59.6（兩側幾乎整片，跨牆的磚最少）
        // y：放 6 排時 b = 9.3 < 10（門檻），所以放 5 排：200 = 2b + 5×30 + 6×0.2 → b = 24.4
        var result = Optimize(Rect(0, 0, 300, 200), Tile(60, 30), Pattern.Straight);

        Assert.True(result.Report.Satisfied, string.Join("\n", result.Report.Reasons));
        Assert.Equal(24.4, Width(result.Report, "A–B")!.Value, 3);
        Assert.Equal(24.4, Width(result.Report, "C–D")!.Value, 3);
        Assert.Equal(59.6, Width(result.Report, "B–C")!.Value, 3);
        Assert.Equal(59.6, Width(result.Report, "D–A")!.Value, 3);
        Assert.Equal(15, result.Layout.FullCount);
        Assert.Equal(20, result.Layout.CutCount);
    }

    [Fact]
    public void Every_wall_strip_is_at_least_one_third_after_auto_offset()
    {
        var result = Optimize(Rect(0, 0, 300, 200), Tile(60, 30), Pattern.Straight);

        // 獨立檢查：長方形房間的切割片都是牆邊條，直接看它在原磚上的尺寸
        Assert.All(result.Layout.CutPieces, p =>
        {
            Assert.True(p.LocalBounds.Width >= 20 - 1e-6, $"{p.Label} 寬 {p.LocalBounds.Width}");
            Assert.True(p.LocalBounds.Height >= 10 - 1e-6, $"{p.Label} 高 {p.LocalBounds.Height}");
        });
    }

    [Fact]
    public void Straight_layout_in_random_rectangles_always_satisfies_the_threshold()
    {
        // 正排在長方形房間一定做得到：剩餘量不夠時少放一整片，兩側各 (r + m)/2 ≥ m/2 > m/3
        var rng = new Random(RandomRooms.Seed);
        for (int n = 0; n < 40; n++)
        {
            double w = Math.Round(100 + rng.NextDouble() * 400, 1), h = Math.Round(100 + rng.NextDouble() * 400, 1);
            double len = Math.Round(15 + rng.NextDouble() * 65, 1), wid = Math.Round(15 + rng.NextDouble() * (len - 15), 1);
            var tile = Tile(len, wid, jointMm: Math.Round(rng.NextDouble() * 4, 1));
            var baseline = new Baseline(new Vec2(Math.Round(rng.NextDouble() * w, 1), 0), n % 2 == 0 ? 0 : 90);

            var result = Optimize(Rect(0, 0, w, h), tile, Pattern.Straight, baseline);

            string context = $"#{n} 房間 {w}×{h} 磚 {len}×{wid} 縫 {tile.JointMm}mm 基準 {baseline.AngleDeg}°";
            Assert.True(result.Report.Satisfied, context + "\n" + string.Join("\n", result.Report.Reasons));
            double tu = result.Lattice.TileU, tv = result.Lattice.TileV;
            Assert.All(result.Layout.CutPieces, p => Assert.True(
                p.LocalBounds.Width >= Math.Min(tu / 3, w) - 1e-3 && p.LocalBounds.Height >= Math.Min(tv / 3, h) - 1e-3,
                $"{context}：{p.Label} {p.LocalBounds.Width:0.###}×{p.LocalBounds.Height:0.###}"));
        }
    }

    [Fact]
    public void Running_bond_third_cannot_satisfy_both_side_walls_and_explains_why()
    {
        // 左牆三種排的切條相差 20.07 cm：只有 oᵤ ≈ 0.2 時左側才都 ≥ 20；
        // 此時右牆（300 = 4 × 60.2 + 59.2）的切條為 59.0、38.9、18.9 → 必有一側不足
        var result = Optimize(Rect(0, 0, 300, 200), Tile(60, 30), Pattern.RunningBond(BondRatio.Third));
        var report = result.Report;

        Assert.False(report.Satisfied);
        Assert.Contains(report.Reasons, r => r.Contains("交丁 1/3"));
        Assert.NotNull(report.MinWidth);
        Assert.True(report.MinWidth < 20);
        Assert.Contains($"{report.Violations.Min(v => v.DisplayMinWidth):0.0} cm", report.Reasons[0]);
        Assert.Equal(report.MinWidth, report.Violations.Min(v => v.MinWidth));
    }

    [Fact]
    public void Slightly_slanted_wall_produces_unavoidable_slivers()
    {
        // 牆 A–B 斜 3°：沿牆跨過水平縫的地方一定會出現楔形小片
        var room = new Polygon([new(0, 0), new(400, 21), new(400, 300), new(0, 300)]);
        var result = Optimize(room, Tile(20, 20), Pattern.Straight);

        var ab = result.Report.Walls.Single(w => w.Name == "A–B");
        Assert.True(ab.Oblique);
        Assert.False(ab.Satisfied);
        Assert.Contains(result.Report.Reasons, r => r.Contains("斜牆 A–B"));
    }

    [Fact]
    public void Manual_offset_report_measures_actual_strips()
    {
        var region = RectRegion(300, 200);
        var layout = LayoutEngine.Layout(region, Lattice.Create(Tile(60, 30), Pattern.Straight, Baseline.Default));

        var report = OffsetOptimizer.Report(Rect(0, 0, 300, 200), layout);

        // 磚邊貼齊 A–B 與 D–A，貼著它們的只有轉角片 → 以轉角片的深度計：整片高 30、整片長 60
        Assert.Equal(30, Width(report, "A–B")!.Value, 3);
        Assert.Equal(60, Width(report, "D–A")!.Value, 3);
        Assert.Equal(59.2, Width(report, "B–C")!.Value, 3);
        Assert.Equal(18.8, Width(report, "C–D")!.Value, 3);
        Assert.True(report.Satisfied);
    }

    [Fact]
    public void L_shape_report_is_self_consistent()
    {
        var room = new Polygon([new(0, 0), new(400, 0), new(400, 150), new(250, 150), new(250, 300), new(0, 300)]);
        var result = Optimize(room, Tile(60, 30), Pattern.Straight);
        var report = result.Report;

        Assert.Equal(6, report.Walls.Count);
        Assert.Equal(report.Satisfied, report.Reasons.Count == 0);
        Assert.Equal(report.Walls.Where(w => w.MinWidth is not null).Min(w => w.MinWidth), report.MinWidth);
    }

    [Fact]
    public void Reported_minimum_is_never_rounded_up_to_the_threshold()
    {
        // 39.96 cm 若四捨五入顯示成 40.0，會出現「門檻 40.0，實際最小 40.0」卻判定不足的矛盾
        var pattern = new System.Text.RegularExpressions.Regex(@"門檻 (?<t>[\d.]+) cm，實際最小 (?<m>[\d.]+) cm");
        var failures = new List<string>();
        foreach (var c in RandomRooms.Generate(90))
        {
            var room = c.Region.Polygons.Where(p => p.IsCounterClockwise).MaxBy(p => p.Area)!;
            var result = OffsetOptimizer.Optimize(room, c.Region, Lattice.Create(c.Tile, c.Pattern, c.Baseline));
            foreach (System.Text.RegularExpressions.Match m in result.Report.Reasons.SelectMany(r => pattern.Matches(r)))
                if (double.Parse(m.Groups["m"].Value) >= double.Parse(m.Groups["t"].Value))
                    failures.Add($"{c}：{m.Value}");
            // 表格上顯示的數字比大小，必須與判定一致
            foreach (var w in result.Report.Walls.Where(w => w.MinWidth is not null))
                if ((w.DisplayMinWidth >= w.DisplayThreshold) != w.Satisfied)
                    failures.Add($"{c}：{w.Display} 顯示 {w.DisplayMinWidth} / {w.DisplayThreshold}，判定 {w.Satisfied}");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(10)));
    }

    [Fact]
    public void Diagonal_layout_in_rectangle_avoids_small_triangles()
    {
        // 斜貼時牆與磚縫夾 45°；讓牆剛好通過磚的對角線時，牆邊都是半片三角形（深度 21.2 cm ≥ 10）
        var result = Optimize(Rect(0, 0, 300, 200), Tile(30, 30), Pattern.Diagonal);

        Assert.True(result.Report.Satisfied, string.Join("\n", result.Report.Reasons));
        Assert.True(result.Report.MinWidth >= 10);
    }
}
