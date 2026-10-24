# TilePlan 教學文件：導覽

TilePlan 是給泥作工班用的地坪排版工具：輸入房間形狀（在畫布上畫，或填現場量的牆長與對角線），自動排磚、產生切割清單、估算材料，最後印出 A4 報價單。整個系統跑在瀏覽器裡，沒有後端。

**技術棧**：.NET 10、Blazor WebAssembly（standalone）、Clipper2（多邊形布林運算）、xUnit、bUnit、Playwright for .NET、GitHub Actions、GitHub Pages。

**文件對應版本**：commit `0078dbe`（2026-10-24）

這份文件是寫給「之後回來看的自己」：基礎語法與通用概念不解釋，篇幅花在「為什麼這樣做」與「可以帶走的技巧」。涉及需求或設計理由的段落都有來源標記：

> 📄 依據：有文件、commit 或對話紀錄可查
> 🔍 推斷：從程式碼反推，當初不一定這樣想，請自行確認

---

## 閱讀路線

| 路線 | 章節 | 適合 |
|---|---|---|
| 快速掌握（約 30 分鐘） | 00 → 03 → 07 | 想先知道程式碼怎麼分、從哪裡讀起 |
| 完整學習 | 依序閱讀 01 → 12 | 想把整個專案當教材從頭走一遍 |
| 想學計算幾何與數值方法 | 08（實測推算、排版、偏移、餘料、最小寬度） | 演算法是這個專案最有技術含量的部分 |
| 想學「怎麼證明程式是對的」 | 09 → 08 | 手算測試、性質測試、獨立驗證、蒙地卡羅 |
| 想學 Blazor WASM 的實務細節 | 07 後半 → 10 → 11 | JS interop、效能、trim、GitHub Pages 子路徑 |
| 想學怎麼記錄設計決策 | 11 → 01 | 輕量 ADR 與踩坑紀錄 |

---

## 章節清單

| 章節 | 檔案 | 狀態 |
|---|---|---|
| 01 需求分析 | [01-requirements.md](01-requirements.md) | 已產生 |
| 02 系統分析 | [02-system-analysis.md](02-system-analysis.md) | 已產生 |
| 03 架構設計 | [03-architecture.md](03-architecture.md) | 已產生 |
| 04 技術選型 | [04-tech-stack.md](04-tech-stack.md) | 已產生 |
| 05 資料庫設計 | — | **略過**：本專案沒有資料庫。資料只存在瀏覽器 localStorage 與匯出的 JSON 檔，存檔格式的設計併入 06 章「專案檔格式」 |
| 06 輸入／輸出介面與資料流 | [06-api-dataflow.md](06-api-dataflow.md) | 已產生（沒有 HTTP API，改寫專案檔格式、Core 對外 API、JS interop 與四條核心流程的時序圖） |
| 07 程式內容導讀 | [07-code-walkthrough.md](07-code-walkthrough.md) | 已產生 |
| 08 核心演算法 | [08-algorithms.md](08-algorithms.md) | 已產生 |
| 09 測試 | [09-testing.md](09-testing.md) | 已產生 |
| 10 部署與環境設定 | [10-deployment.md](10-deployment.md) | 已產生 |
| 11 設計取捨與踩坑紀錄 | [11-tradeoffs-pitfalls.md](11-tradeoffs-pitfalls.md) | 已產生 |
| 12 延伸練習與自我檢核 | [12-exercises.md](12-exercises.md) | 已產生 |
| 名詞對照表 | [glossary.md](glossary.md) | 已產生 |

---

## 與既有文件的關係

repo 裡已經有三份文件，這套教學文件不重複它們的內容，而是補上「讀程式碼的路線」與「為什麼」：

| 文件 | 內容 | 本教學文件的對應 |
|---|---|---|
| `README.md` | 使用方式、公式、效能數字、已知限制 | 公式與數字以 README 為準，這裡解釋背後的程式碼 |
| `docs/architecture.md` | 開發前的系統分析與決策紀錄 D1–D13 | 那是**設計稿**，部分類別名稱在實作時改了（見 03 章「設計稿與實作的差異」） |
| `docs/test-report.md` | 驗收測試報告、發現的問題 F1–F6、外部審查回應 | 11 章的踩坑紀錄大量引用它 |

---

## 修訂紀錄

| 日期 | 對應 commit | 更新內容 |
|---|---|---|
| 2026-10-24 | `0078dbe` | 初版：產生 00–04、06–12 與名詞對照表；05 略過 |
