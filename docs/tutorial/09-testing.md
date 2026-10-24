# 09 測試

這個專案的測試有一個共同目標：**不讓測試只是驗證自己的假設**。所以除了一般的單元測試，還有手算測試、性質測試、獨立驗證腳本與蒙地卡羅，每一種都在防不同的盲點。

## 測試策略

| 層級 | 專案／檔案 | 數量 | 測什麼 | 防的盲點 |
|---|---|---|---|---|
| 手算測試 | `tests/TilePlan.Core.Tests` 中各 `*Tests.cs` | 包含在 97 個 Core 測試中 | 驗收標準要求的具體數字（6.00 m²、整片 24 切割 11、37 片 5 箱……），手算過程寫在測試註解裡 | 程式碼與規格對同一件事的理解不同 |
| 單元測試 | 同上 | 同上 | 邊界與例外：三角不等式不成立、版本過新、存檔損壞、`decimal` 陷阱 | 錯誤路徑沒人走過 |
| 性質測試 | `LayoutPropertyTests`、`OffcutPropertyTests`、部分 `OffsetOptimizerTests` | 240／90／40 個隨機房間 | 不變量：面積守恆、不重疊、不超出、重用不增加原磚數、顯示數字與判定一致 | 只測到自己想得到的房間 |
| 統計驗證 | `SurveyUncertaintyTests.Predicted_95_percent_range_matches_monte_carlo` | 2,000 次模擬 | 推算的 95% 範圍與實際分布一致 | 公式推導錯了但「看起來合理」 |
| 元件測試 | `tests/TilePlan.Web.Tests`（bUnit） | 10 | 自動儲存與還原、面積顯示、實測提示不出現「0.00 cm」 | 介面邏輯沒人測 |
| 端對端測試 | `tests/TilePlan.E2E.Tests`（Playwright） | 5 | 以**發佈版**在 Chromium 跑關鍵流程 | 本機正常、發佈（trim）後才壞；CSS 列印樣式 |
| 獨立驗證 | `tests/verification/verify.cs` | 500＋167＋200＋150 個 | 用**不同的種子、不同的房間產生器**重跑性質 | 測試與實作共用同一個產生器的偏差 |

合計 `dotnet test` 107 個（Core 97、Web 10），加上 E2E 5 個。題目要求至少 40 個（驗收 14）。

## 執行指令

```bash
# 根目錄：Core 與 Web 測試（不需要瀏覽器）
dotnet test

# 瀏覽器端對端測試：第一次會自動 dotnet publish 並下載 Chromium
dotnet test tests/TilePlan.E2E.Tests

# 已經有發佈版時，指定目錄可以跳過發佈
TILEPLAN_SITE=/path/to/publish/wwwroot dotnet test tests/TilePlan.E2E.Tests

# 獨立驗證腳本（.NET 10 的單檔執行）
dotnet run tests/verification/verify.cs

# 覆蓋率
dotnet test -c Release --collect:"XPlat Code Coverage"
```

E2E 專案不在 `TilePlan.slnx` 中（📄 決策 D12），所以根目錄的 `dotnet test` 不會跑它，沒裝瀏覽器的人也能直接跑測試。

## 各測試類別

| 測試類別 | 數量 | 驗收 |
|---|---|---|
| `AreaTests` | 9 | 2 |
| `SurveySolverTests` | 16 | 3、4 |
| `SurveyUncertaintyTests` | 5 | 3 |
| `LayoutEngineTests` | 10 | 5、6 |
| `LayoutPropertyTests` | 5 | 8 |
| `OffsetOptimizerTests` | 9 | 7 |
| `ExclusionEdgeTests` | 5 | 7 |
| `SmallPieceTests` | 7 | 7 |
| `OffcutPlannerTests`、`OffcutPropertyTests` | 7、2 | 9 |
| `MaterialEstimatorTests` | 9 | 10 |
| `QuoteBuilderTests` | 5 | 12 |
| `ProjectTests` | 8 | 13 |
| `AutoSaveTests`（Web） | 6 | 13 |
| `ComponentTests`、`SurveyFormTests`（Web，bUnit） | 2、2 | 2、3 |
| `CriticalFlowTests`（E2E） | 5 | 1、11、12、13 |

## 走讀一：面積守恆的性質測試

```csharp
// tests/TilePlan.Core.Tests/Layout/LayoutPropertyTests.cs
[Fact]
public void Pieces_plus_joints_equal_tileable_area_within_0_1_percent()
{
    var failures = Results
        .Select(r => (r.Case, Error: Math.Abs(r.Layout.PieceArea + JointNetwork.Area(r.Layout) - r.Case.Region.Area) / r.Case.Region.Area))
        .Where(x => x.Error >= 1e-3)
        .Select(x => $"{x.Case}：相對誤差 {x.Error:P4}")
        .ToList();

    Assert.True(failures.Count == 0, string.Join("\n", failures));
}
```

**資料準備**：`RandomRooms.Generate(240)`（`tests/TilePlan.Core.Tests/RandomRooms.cs`）以固定種子 `20261024` 產生三種房間輪流出現：

- 直角多邊形：2–3 個互相重疊的長方形取聯集（L、T、U 形）；
- 星形多邊形：隨機角度排序、隨機半徑，保證不自交，常有內凹角與斜牆；
- 斜牆多邊形：長方形四角隨機切斜角。

每個房間再加 0–2 個可能旋轉、可能超出房間的不鋪設區，並隨機選擇磚的尺寸、縫寬、四種鋪法、基準線角度與偏移。

**斷言的設計**有三個值得學的地方：

1. **縫面積必須獨立計算。** 如果縫面積用「可鋪面積 − 磚片面積」算，這個斷言就變成恆等式 `a + (b − a) = b`，永遠通過，什麼都驗不到。`JointNetwork.Area()` 從磚格**另外**產生每一格的右側縫與上方縫矩形，再與區域取交集。磚片與縫是兩條完全獨立的計算路徑，兩者加起來等於區域面積才有意義。
2. **收集全部失敗再一次斷言。** 不在第一個失敗就停下來，而是把所有失敗的房間連同參數（`Case.ToString()` 印出磚尺寸、縫寬、鋪法、基準線角度）一起列出。性質測試失敗時，最需要的資訊是「哪些輸入會失敗、它們有什麼共通點」。
3. **固定種子。** 失敗可以重現；`verify.cs` 再用不同的種子補足「只測這 240 個」的偏差。

實測結果：240 個房間的相對誤差都在 10⁻⁵ 以下，來源是 Clipper 的 1 µm 整數化（📄 `README.md`）。門檻 0.1% 留了兩個數量級的餘裕。

## 走讀二：自動儲存的合併寫入

```csharp
// tests/TilePlan.Web.Tests/AutoSaveTests.cs（節錄）
var storage = new GatedStorage();
var (store, autoSave) = Boot(storage);
autoSave.Start();

store.Project.Name = "第 1 版";
store.NotifyChanged();                 // 開始儲存，卡在 storage 裡
for (int i = 2; i <= 10; i++)
{
    store.Project.Name = $"第 {i} 版";
    store.NotifyChanged();             // 儲存進行中：只做標記
}
storage.Release();
await autoSave.WhenIdle().WaitAsync(TimeSpan.FromSeconds(5));

Assert.Equal(2, storage.Writes.Count);
Assert.Contains("第 1 版", storage.Writes[0]);
Assert.Contains("第 10 版", storage.Writes[1]);
```

時序相關的邏輯最難測。這個測試的做法是**把時間換成閘門**：`GatedStorage` 的第一次 `SetAsync()` 會等一個 `TaskCompletionSource`，直到測試呼叫 `Release()`。於是「儲存很慢、期間又修改了 9 次」這個情境可以被精確製造出來，不需要 `Task.Delay`，也不會因為 CI 機器慢而偶發失敗。

`Boot()` 每次建立新的 `ProjectStore` 與 `AutoSaveService`、接上同一個儲存，模擬「重新整理頁面」。這讓 `Project_survives_a_page_reload` 不需要瀏覽器就能驗證驗收 13。

## 走讀三：期望值由 Core 算出的 E2E 測試

```csharp
// tests/TilePlan.E2E.Tests/CriticalFlowTests.cs（節錄）
var project = Sample();
var bath = project.Rooms.Single(r => r.Id == "bath");
project.FindTile(bath.TileId)!.JointMm = 5;
var expected = ProjectCalculator.ComputeRoom(project, bath);
// ...
await joint.FillAsync("5");
await joint.PressAsync("Tab");

var legend = page.Locator(".legend");
await Assertions.Expect(legend).ToContainTextAsync($"整片 {expected.Layout!.FullCount}");
```

E2E 測試**不寫死任何數字**。期望值由 `ProjectCalculator` 直接算出，測試只驗證「畫面上顯示的與 Core 算的一致」。好處是範例專案或演算法改變時（例如外部審查後總計從 NT$ 68,240 變成 67,060），E2E 測試不必跟著改。Core 的正確性由 Core 測試負責，E2E 只負責「介面有沒有把 Core 的結果正確呈現」，兩層的責任分得很清楚。

`SiteFixture`（`tests/TilePlan.E2E.Tests/SiteFixture.cs`）用 ASP.NET Core 在測試程序內架一個靜態檔伺服器，找不到的路徑回 `index.html`，行為與 GitHub Pages 加上 404.html 相同。載入的是 `dotnet publish` 的發佈版，所以會經過 trim，能抓到「本機正常、發佈後才壞」的問題（見 11 章的 JSON source generator）。

列印測試用 `page.EmulateMediaAsync(new() { Media = Media.Print })` 切到列印媒體，再用 `getComputedStyle` 檢查 `thead`、`tfoot`、`tr` 的計算後樣式。這比產生 PDF 再解析快得多，而且直接驗證 F4 的修正。

效能只做寬鬆的回歸防護：E2E 要求修改縫寬到畫面更新 < 3 秒（測試註解：CI 機器較慢，本機 Release 約 0.3 秒）。真正的 1 秒驗收以介面內建的「量測 10 次」手動量測，結果記在 README。

## 覆蓋範圍

> 📄 依據：`docs/test-report.md` §4.2（初測時的數字，之後新增的測試未重新量測）

| 專案 | 行覆蓋率 | 分支覆蓋率 |
|---|---|---|
| TilePlan.Core | 88.3% | 81.1% |
| TilePlan.Web | 7.3% | 2.2% |

Core 各模組：Offset 98.5%、Cutting 97.6%、Quoting 97.1%、Layout 96.1%、Geometry 95.2%、Survey 93.0%、Tiles 92.5%、Estimation 92.3%、Projects 83.5%（包含 JSON source generator 自動產生的程式碼）。

**照實寫出的缺口**：

- **Web 層的單元覆蓋率很低。** 畫布互動（拖曳頂點、點選磚片）、匯入匯出、切割清單的畫面主要靠 E2E 與當初手動執行的 Playwright 驗證（測試報告第 6 節的 22 項），只有 5 項自動化進了 CI。
- **`tileplan.js` 沒有獨立測試。** 平移縮放與拖曳只在手動測試中驗證過。
- **視覺品質沒有自動化。** 細長磚標籤重疊（11 章）是用截圖肉眼檢查的。
- **`SurveySolver` 的 `MaxLeaves` 上限沒有測試**，也沒有對應的使用者訊息。
- **只在 Chromium 上測。** README 宣稱支援 Chrome／Edge，兩者同核心；Firefox、Safari 沒有驗證過。

## 建議補強的測試

以下標為**建議**，目前不存在：

1. **建議**：`LabelPlacement` 的性質測試：對隨機切割片，回傳的位置必須在形狀內、字級 × 0.8 不超過到邊界的距離。
2. **建議**：Web 層的 bUnit 測試補上 `PieceInspector` 的「同樣長度的邊只標一次」。
3. **建議**：E2E 加入匯出 → 匯入的流程（目前由 Core 的 `Export_then_import_gives_identical_layout_and_quote` 與 Web 的 `AutoSaveTests` 分段覆蓋）。
4. **建議**：凹形切片的細碎片測試，記錄目前「凸包高估」的行為；之後改用侵蝕法時可以直接翻轉斷言。
5. **建議**：Playwright 加跑 Firefox 與 WebKit，至少確認報價單列印與畫布互動。
