# 04 技術選型

版本以各 `*.csproj` 與 `global.json` 為準。

## 執行期

| 技術／套件 | 版本 | 用途 | 選用理由 | 可能的替代方案 |
|---|---|---|---|---|
| .NET SDK／目標框架 | SDK 10.0.100 以上（`global.json`，`rollForward: latestFeature`）；`net10.0` | 全部專案 | 📄 題目要求 .NET 9 以上；決策 D5：本機只有 .NET 10 SDK，且 10 是 LTS | `net9.0`（STS，支援期較短） |
| Blazor WebAssembly standalone | `Microsoft.AspNetCore.Components.WebAssembly` 10.0.13 | 介面，整個 app 跑在瀏覽器 | 📄 題目指定 | Blazor Server（需要伺服器）、React／Vue（要放棄 C# 單一技術棧） |
| Clipper2 | 2.0.0 | 多邊形布林運算：差集、聯集、交集、矩形裁切、擴張、開放路徑裁切 | 📄 題目允許使用，但限定只能做布林運算；📄 `docs/architecture.md` §2.3：整數座標版本（Clipper64）運算穩健 | NetTopologySuite（功能多但較重）、自寫（多邊形布林運算的退化情況非常難處理） |
| System.Text.Json（source generator） | 隨 .NET 10 | 專案 JSON 序列化 | 📄 `docs/architecture.md` §2.3：內建、`double` 預設以最短可往返格式輸出；改用 source generator 的原因見下方 | Newtonsoft.Json（反射式，trim 後同樣有風險，而且要多一個套件） |
| 自寫 JS 模組 | — | localStorage、下載檔案、列印、畫布平移縮放與拖曳、量測重繪時間 | 📄 `docs/architecture.md` §2.3：不引入 Blazored.LocalStorage 等套件；平移縮放直接改 SVG `viewBox`，不經過 .NET 重繪 | Blazored.LocalStorage、panzoom 類 JS 套件 |

### 為什麼 JSON 要用 source generator

> 📄 依據：`src/TilePlan.Core/Projects/ProjectSerializer.cs` 的類別註解；開發對話中的踩坑紀錄（見 11 章）

Blazor WebAssembly 發佈時會做 IL trimming，把「看起來沒被用到」的程式碼刪掉以縮小下載量。反射式序列化在執行期才用反射找屬性，trimmer 看不出這些屬性會被用到，可能把它們刪掉。`ProjectJsonContext`（`[JsonSerializable(typeof(Project))]` 等）讓序列化程式碼在編譯期產生，trim 之後依然完整。

這是 Blazor WASM 專案很典型的「本機 `dotnet run` 正常、發佈後才壞」的問題。本機開發不會 trim，所以測不出來；這也是為什麼 E2E 測試刻意載入 `dotnet publish` 的發佈版，而不是開發伺服器（見 09 章）。

### 為什麼 Clipper 的單位是 1 µm

> 📄 依據：`src/TilePlan.Core/Geometry/PolygonOps.cs` 的類別註解；commit `9c545af` 的說明；`docs/architecture.md` §3.1（實作修正）

Clipper64 用 64 位元整數座標，必須選一個「1 個整數單位代表多長」。原本設計是 0.01 mm，實作時發現 `RectClip` 求交點有 ±1 單位的誤差，一片 60 cm 的磚會差到 0.03 cm²，累積起來會讓面積守恆測試（NFR-05）失敗。改成 1 µm（每公分 10,000 單位）後，一片磚的誤差降到 0.01 cm² 以下。100 m 的房間也只有 10⁸ 單位，離 Int64 上限很遠。

## 測試與工具

| 技術／套件 | 版本 | 用途 | 選用理由 |
|---|---|---|---|
| xUnit | 2.9.3（runner 3.1.4） | 所有測試專案 | 📄 題目指定 |
| bUnit | 2.12.5 | 在沒有瀏覽器的情況下渲染 Razor 元件並檢查輸出 | 📄 `docs/architecture.md` §2.3：少量使用，驗證自動儲存還原與面積顯示 |
| Microsoft.Playwright | 1.64.1 | 瀏覽器端對端測試 | 📄 外部審查意見要求 E2E 進 CI；選 Playwright for .NET 是對話中使用者的選擇（決策 D12：維持 C# 單一技術棧） |
| Microsoft.AspNetCore.App（FrameworkReference） | 隨 SDK | E2E 測試內架靜態檔伺服器 | 📄 `tests/TilePlan.E2E.Tests/TilePlan.E2E.Tests.csproj` 的註解：SDK 內建，不是額外套件 |
| coverlet.collector | 6.0.4 | 程式碼覆蓋率 | 🔍 測試專案範本的預設；測試報告 §4.2 用它量了覆蓋率 |
| GitHub Actions | `actions/checkout@v4`、`setup-dotnet@v4`、`upload-pages-artifact@v3`、`deploy-pages@v4` | CI 與部署 | 📄 題目建議（CI 為加分項）；免費 |
| GitHub Pages | — | 線上 Demo | 📄 `docs/architecture.md` §2.3：免費、題目建議 |

整個專案引入的外部套件只有 **Clipper2**（執行期）以及 **xUnit、bUnit、Playwright**（只在測試專案）。

> 📄 依據：使用者的全域規則「沒告知就不引入新的 library 或工具」；Playwright 是在對話中詢問使用者後才加入

## 刻意沒有用的東西

| 沒用 | 理由 |
|---|---|
| WebAssembly AOT 編譯 | 📄 決策 D8：先量測再決定。結果未使用 AOT 就達標（正排約 265 ms、斜貼約 592 ms），省下 CI 安裝 `wasm-tools` workload 的時間與複雜度 |
| UI 元件庫（MudBlazor 等） | 🔍 未記載。可能的理由：介面需求單純（表單、表格、SVG），元件庫會增加下載量；而且排版畫布必須自己寫 |
| 狀態管理套件（Fluxor 等） | 🔍 未記載。單一 `ProjectStore` 加一個 `Changed` 事件就夠用 |
| 圖形函式庫（Canvas、WebGL） | 🔍 未記載。SVG 可以直接點選、縮放不失真、列印清楚；1,200 片磚合成 3 條 path 後效能也足夠 |

## 版本注意事項

- `TilePlan.slnx` 是 .NET 9 起支援的新版 XML 方案格式，舊版 Visual Studio 或 SDK 開不了。
- `TilePlan.Web.csproj` 設定了 `OverrideHtmlAssetPlaceholders`，這是 .NET 10 新增、用於 `index.html` 資源指紋（fingerprint）的選項；🔍 降版到 .NET 9 時需要移除。
- `Directory.Build.props` 開啟 `InvariantGlobalization`：WASM 不必下載 ICU 文化資料，下載量較小，而且所有機器（開發機、CI、瀏覽器）的數字格式都一樣。🔍 程式碼裡仍然在 `src/TilePlan.Web/Rendering/Svg.cs` 的 `Svg.N()` 與 `Fmt` 明確指定 `CultureInfo.InvariantCulture`，推測是雙重保險：SVG 座標一旦出現逗號小數點就會整個畫壞，不想依賴全域設定。
