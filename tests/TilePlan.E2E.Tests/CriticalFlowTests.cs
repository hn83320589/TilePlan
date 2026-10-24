using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using TilePlan.Core.Projects;

namespace TilePlan.E2E.Tests;

/// <summary>
/// 關鍵流程的瀏覽器端對端測試：開啟範例、修改縫寬、列印報價單、自動儲存、實測提示。
/// 期望值一律由 Core 直接計算，範例專案改變時測試不必跟著改數字。
/// </summary>
[Collection(SiteCollection.Name)]
public class CriticalFlowTests(SiteFixture site)
{
    private static Project Sample() =>
        ProjectSerializer.Deserialize(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", "sample-project.json")));

    private static string Money(decimal v) => v.ToString("#,0", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Sample_project_opens_and_quote_matches_core_calculation()
    {
        var expected = ProjectCalculator.Compute(Sample());
        var page = await site.NewPageAsync();

        await page.GotoAsync(site.BaseUrl);
        await page.GetByText("單價與材料參數").WaitForAsync();
        var rows = await page.Locator("section.panel table.grid").First.Locator("tbody tr").AllInnerTextsAsync();
        for (int i = 0; i < expected.Rooms.Count; i++)
            Assert.Contains($"{expected.Rooms[i].Area.M2Rounded:0.00} m²", rows[i]);

        await page.GotoAsync(site.BaseUrl + "quote");
        await page.GetByText("材料費小計").WaitForAsync();
        Assert.Equal($"NT$ {Money(expected.Quote.GrandTotal)}", await page.Locator(".total strong").InnerTextAsync());
    }

    [Fact]
    public async Task Changing_joint_width_relayouts_to_the_core_result()
    {
        var project = Sample();
        var bath = project.Rooms.Single(r => r.Id == "bath");
        project.FindTile(bath.TileId)!.JointMm = 5;
        var expected = ProjectCalculator.ComputeRoom(project, bath);
        int reused = expected.Cuts!.ReusedCount;

        var page = await site.NewPageAsync();
        await page.GotoAsync(site.BaseUrl + "room/bath");
        await page.GetByText("本間材料").WaitForAsync();
        var joint = page.GetByLabel("縫寬（mm）");
        await joint.FillAsync("5");
        await joint.PressAsync("Tab");

        var legend = page.Locator(".legend");
        await Assertions.Expect(legend).ToContainTextAsync($"整片 {expected.Layout!.FullCount}");
        await Assertions.Expect(legend).ToContainTextAsync($"切割片 {expected.Layout.CutCount - reused}");
        await Assertions.Expect(legend).ToContainTextAsync($"使用餘料的切割片 {reused}");

        // 驗收 11 的回歸防護：CI 機器較慢，門檻放寬為 3 秒（本機 Release 約 0.3 秒）
        var timing = await page.Locator("p.small.muted.num").InnerTextAsync();
        var ms = Regex.Match(timing, @"畫面更新 (\d+) ms");
        Assert.True(ms.Success, timing);
        Assert.True(int.Parse(ms.Groups[1].Value) < 3000, timing);
    }

    [Fact]
    public async Task Quote_prints_on_A4_portrait_with_repeating_header_and_unsplit_rows()
    {
        var page = await site.NewPageAsync();
        await page.GotoAsync(site.BaseUrl + "quote");
        await page.GetByText("材料費小計").WaitForAsync();
        await page.EmulateMediaAsync(new() { Media = Media.Print });

        var styles = await page.EvaluateAsync<string[]>(@"() => {
            const s = el => getComputedStyle(document.querySelector(el));
            return [s('.quote-sheet thead').display, s('.quote-sheet tfoot').display, s('.quote-sheet tbody tr').breakInside,
                    getComputedStyle(document.querySelector('.sidebar')).display];
        }");
        Assert.Equal(["table-header-group", "table-row-group", "avoid", "none"], styles);

        var pdf = await page.PdfAsync(new() { PreferCSSPageSize = true, PrintBackground = true });
        var boxes = Regex.Matches(Encoding.Latin1.GetString(pdf), @"/MediaBox\s*\[\s*0 0 ([\d.]+) ([\d.]+)\s*\]")
            .Select(m => (W: double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), H: double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)))
            .ToList();
        Assert.NotEmpty(boxes);
        Assert.All(boxes, b =>
        {
            Assert.InRange(b.W * 25.4 / 72, 209, 211);   // A4 寬 210 mm
            Assert.InRange(b.H * 25.4 / 72, 296, 298);   // A4 高 297 mm（直向）
        });
    }

    [Fact]
    public async Task Edits_survive_an_immediate_reload()
    {
        var page = await site.NewPageAsync();
        await page.GotoAsync(site.BaseUrl);
        await page.GetByText("單價與材料參數").WaitForAsync();

        var name = page.GetByLabel("專案名稱");
        await name.FillAsync("端對端測試專案");
        await name.PressAsync("Tab");
        await page.ReloadAsync();                // 不等待：修改必須立即寫入
        await page.GetByText("單價與材料參數").WaitForAsync();

        Assert.Equal("端對端測試專案", await page.Locator("h1").First.InnerTextAsync());
    }

    [Fact]
    public async Task Survey_without_redundant_measurements_does_not_claim_zero_residual()
    {
        var page = await site.NewPageAsync();
        await page.GotoAsync(site.BaseUrl + "room/living");
        await page.GetByText("本間材料").WaitForAsync();
        await page.Locator(".steps button", new() { HasText = "房間形狀" }).ClickAsync();

        var form = page.Locator(".columns > div").Nth(1);
        await Assertions.Expect(form).ToContainTextAsync("沒有多餘量測");
        await Assertions.Expect(form).ToContainTextAsync("各角位置誤差最大約");
        Assert.DoesNotContain("最大殘差 0.0", await form.InnerTextAsync());
    }
}
