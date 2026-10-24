# 名詞對照表

## 業務術語（泥作與磁磚）

| 名詞 | 說明 | 出現位置 |
|---|---|---|
| 坪 | 台灣面積單位，1 坪 = 400/121 m² ≈ 3.3058 m² | `Area.Ping`、報價單 |
| 正排 | 磚縫縱橫都對齊成直線 | `PatternKind.Straight` |
| 交丁（1/2、1/3） | 錯縫：每一排沿磚長方向錯開 1/2 或 1/3 磚長，又稱「丁字貼」 | `PatternKind.RunningBond`、`BondRatio` |
| 45° 斜貼 | 磚縫與牆夾 45° | `PatternKind.Diagonal` |
| 基準線 | 決定磚縫方向的參考線，通常拉在某面牆或房間中線 | `Baseline`、`Room.BaselineOrigin` |
| 偏移 | 磚格相對於基準線的起始位置 (oᵤ, oᵥ) | `Lattice.OffsetU`、`OffsetV` |
| 切條／邊條 | 牆邊被切窄的磚；本專案定義為「貼牆切片的頂點到牆的最大距離」 | `WallStrip.MinWidth` |
| 整片 | 完全不用切的磚 | `PieceKind.Full` |
| 切割片 | 需要裁切的磚，可能由多塊組成（例如被柱子切成兩塊） | `PieceKind.Cut` |
| 原磚 | 拿來切割的一整片磚，切割清單編號 S01、S02… | `StockTile` |
| 餘料 | 原磚切下一塊後剩下、還能用的部分 | `OffcutPlanner` 的 `Offcut` |
| 刀口 | 切割時被鋸片吃掉的寬度 | `CutSettings.KerfCm`、`Room.KerfMm` |
| 不鋪設區 | 房間內不鋪磚的地方：柱子、浴缸、廚具底座 | `Exclusion` |
| 縫寬 | 磚與磚之間的間隙，以 mm 輸入 | `TileSpec.JointMm` |
| 黏著劑 | 把磚黏在地面的材料，用量依面積 | `Pricing.AdhesiveKgPerM2` |
| 填縫劑 | 填滿磚縫的材料，用量依縫長 × 縫寬 × 磚厚 × 密度 | `MaterialEstimator.Estimate()` |
| 損耗率 | 預留破損與切錯的比例：正排、交丁 5%，斜貼 10% | `Pricing.DefaultWasteRate()` |
| 批號 | 同一批生產的磚，不同批號可能有色差；跨房間合併叫貨的理由（D1） | `QuoteBuilder` |
| 細碎片 | 最窄處小於磚短邊 1/3 的切割片 | `LayoutResult.SmallPieces()` |
| 轉角片 | 同時貼著兩面不平行牆的切割片 | `EdgeStripEvaluator`、`OffsetOptimizer` 的 `ActualMinWidth()` |

## 專案內的自訂名稱

| 名詞 | 說明 | 出現位置 |
|---|---|---|
| 可鋪區域 | 房間 − ∪不鋪設區 | `Region.Tileable()` |
| 磚格座標 (u, v) | 以基準線為 u 軸（斜貼再轉 45°）的座標系 | `Lattice` |
| 格（cell） | 一片磚 + 右側縫 + 上方縫，剛好鋪滿平面 | `Lattice.CellOrigin()`、`CellQuad()` |
| 模數 Mu、Mv | 磚長 + 縫寬、磚寬 + 縫寬 | `Lattice.Mu`、`Mv` |
| 邊界格 | 有區域邊界經過的格子 | `LayoutEngine.BoundaryCells()` |
| 縫網路 | 從磚格獨立產生的縫矩形，用來驗證面積守恆 | `JointNetwork` |
| 快速評估 | 偏移最佳化時把牆當半平面、只算跨牆磚的近似評估 | `EdgeStripEvaluator` |
| 精確排版 | 完整的 `LayoutEngine.Layout()`，報告一律用它 | `OffsetOptimizer.Report()` |
| 解析候選 | 從平行於磚縫的牆直接算出的偏移（貼齊、剛好門檻、對稱） | `OffsetOptimizer` 的 `AnalyticCandidates()` |
| 多餘量測數（Redundancy） | 量測數 − (2n − 3)；為 0 時無法檢核 | `SurveyResult.Redundancy` |
| 檢核對角線 | 推算誤差最大、建議再量的一條對角線 | `SurveyResult.CheckDiagonal` |
| 其他可能形狀 | 長度都吻合、只因對角線穿牆被排除的形狀 | `SurveyResult.Alternatives` |
| 房間快取鍵 | 房間 + 磁磚 + 單價的壓縮 JSON | `ProjectSerializer.RoomKey()` |
| F1–F6 | 測試報告中發現的問題編號 | `docs/test-report.md` 第 8 節 |
| D1–D13 | 架構決策編號 | `docs/architecture.md` 第 0 節 |
| 驗收 1–14（AC1–AC14） | 題目的驗收標準 | `docs/architecture.md` §1.3、`docs/test-report.md` 第 5 節 |

## 技術縮寫與術語

| 名詞 | 說明 | 出現位置 |
|---|---|---|
| WASM | WebAssembly，瀏覽器內執行的二進位格式；Blazor WASM 把 .NET 執行環境跑在上面 | `src/TilePlan.Web` |
| AOT | Ahead-of-Time 編譯；未啟用（D8） | — |
| Trim（IL trimming） | 發佈時移除未使用的程式碼，可能讓反射失效 | `ProjectSerializer` 的 source generator |
| Source generator | 編譯期產生程式碼；這裡用於 JSON 序列化 | `ProjectJsonContext` |
| JS interop | .NET 與 JavaScript 互相呼叫 | `TilePlanJs`、`tileplan.js` |
| bUnit | Blazor 元件的單元測試函式庫 | `tests/TilePlan.Web.Tests` |
| E2E | End-to-End，端對端測試 | `tests/TilePlan.E2E.Tests` |
| 剛性（rigidity） | 一組距離限制能否鎖住形狀；秩 = 2n − 3 時剛性 | `RigidityAnalyzer` |
| 一般位置（generic position） | 沒有三點共線等巧合的座標，剛性分析需要它 | `RigidityAnalyzer` 的 `GenericPositions()` |
| 三邊定位（trilateration） | 由到兩個已知點的距離求位置（兩圓交點） | `SurveySolver` 的 `CircleIntersections()` |
| LM | Levenberg–Marquardt，介於高斯牛頓法與梯度下降之間的非線性最小平方法 | `LeastSquares.Refine()` |
| Jacobian | 量測對座標的偏微分矩陣 | `LeastSquares`、`Uncertainty` |
| 信賴橢圓 | 二維常態分布的等機率線；95% 對應卡方 5.991 | `Uncertainty` |
| 蒙地卡羅 | 以大量隨機模擬驗證統計推算 | `SurveyUncertaintyTests` |
| Liang–Barsky | 參數化的線段對長方形裁切演算法 | `LayoutEngine.CrossesOpenRect()` |
| RectClip | Clipper 的「區域 ∩ 軸對齊長方形」快速裁切 | `PolygonOps.RectClip()` |
| NonZero 填色規則 | 依環繞數判斷內外；逆時針 +1、順時針 −1 | `Region` |
| 鞋帶公式 | 由頂點座標算多邊形面積 | `Polygon.SignedArea` |
| BFD | Best-Fit Decreasing，裝箱的貪婪法 | `OffcutPlanner` |
| 旋轉卡尺 | 凸多邊形最小寬度的方向必垂直於某一條邊 | `PieceGeometry` |
| 性質測試 | 對大量隨機輸入檢查不變量 | `LayoutPropertyTests`、`RandomRooms` |
| Memoization | 以輸入為鍵快取函數結果 | `ProjectStore`、`OffsetOptimizer` 的 `_memo` |
| debounce | 延遲到一段時間沒有新事件才執行；本專案刻意不用（F3） | `AutoSaveService` |
