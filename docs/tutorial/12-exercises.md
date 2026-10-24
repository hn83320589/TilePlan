# 12 延伸練習與自我檢核

## 理解檢核

1. 只量了長方形房間的四面牆長，為什麼系統不能「先假設直角」？它怎麼知道缺幾條、該量哪條？
   <details><summary>答案</summary>
   四面牆長相同的平行四邊形有無限多個，假設直角就是在編造資料（題目驗收 4 明確禁止）。`RigidityAnalyzer` 在固定種子的一般位置上組剛性矩陣，秩比 2n − 3 少多少就缺幾條；`SuggestDiagonals()` 再從未量的頂點對中貪婪挑選能提高秩的對角線。見 08 章第 1 節。
   </details>

2. 面積守恆的性質測試裡，為什麼縫面積不能用「可鋪面積 − 磚片面積」計算？
   <details><summary>答案</summary>
   那樣斷言就變成 a + (b − a) = b 的恆等式，永遠通過。`JointNetwork.Area()` 從磚格另外產生縫的矩形，與磚片走完全獨立的計算路徑。見 09 章走讀一。
   </details>

3. `LayoutEngine` 為什麼把整個區域轉到磚格座標，而不是把每片磚轉到世界座標？
   <details><summary>答案</summary>
   兩個原因：(a) 在磚格座標中磚是軸對齊長方形，可以用 Clipper 最快的 `RectClip`；(b) 旋轉每片磚會讓相鄰磚的共用邊被整數化到不同位置而重疊。只旋轉區域一次，誤差只出現在房間邊界上。見 08 章第 3 節與 11 章 P2。
   </details>

4. 量測剛好足夠時，殘差為什麼必然是 0？系統改顯示什麼？
   <details><summary>答案</summary>
   方程式數（2n − 3）等於未知數，最小平方一定能讓每條量測都完全吻合，殘差不含任何資訊。系統改為回報多餘量測數為 0、以 Σ = σ²(JᵀJ)⁻¹ 推算各角 95% 範圍，並建議一條推算誤差最大的檢核對角線。見 08 章第 2 節與 11 章 P11。
   </details>

5. 自動偏移的評分為什麼是字典序，而不是加權總分？每一層的容許量（1e-6、0.05）有什麼作用？
   <details><summary>答案</summary>
   各準則的單位與重要性完全不同（不足量、對稱差、磚數、距離），加權總分需要調權重，而且低優先準則的大數值可能壓過高優先準則。字典序保證「先滿足門檻、再談對稱」。容許量避免浮點雜訊讓後面的準則失去作用：沒有容許量時，0.0000001 cm 的不足量差異就會決定結果。見 08 章第 4 節與 `OffsetOptimizer` 的 `Score.BetterThan()`。
   </details>

6. 為什麼開啟餘料重用後，原磚數「保證」不會比關閉時多？這個保證依賴 Best-Fit 策略的好壞嗎？
   <details><summary>答案</summary>
   不依賴。關閉時每片切割片各開一片新磚；開啟時每片切割片要嘛用餘料（不開新磚）、要嘛開一片新磚。所以開啟時新磚數 ≤ 切割片數 = 關閉時的新磚數。這是結構上的保證。策略好壞只影響「省多少」。見 08 章第 5 節。
   </details>

7. `ProjectStore.Result()` 為什麼用序列化字串當快取鍵，而 `LayoutStep.OnParametersSet()` 卻可以用 `ReferenceEquals` 判斷要不要重建 SVG？
   <details><summary>答案</summary>
   存檔模型是可變類別，同一個 `Room` 物件內容可能已經變了，只能比較內容。`RoomResult` 是不可變的 record，同一個參考一定是同一份內容，而且 `ProjectStore` 快取命中時回傳同一個物件。見 07 章第 2、4 站。
   </details>

8. 部署到 GitHub Pages 時，base href、404.html、.nojekyll 各解決什麼問題？
   <details><summary>答案</summary>
   `base href` 讓 app 在 /TilePlan/ 子路徑下找得到 _framework/ 與路由；404.html 讓直接開啟或重新整理子路由時仍回傳 app；.nojekyll 避免 Jekyll 忽略底線開頭的 `_framework` 目錄。見 10 章。
   </details>

## 動手練習

### 練習 1：新增一種鋪法「人字貼」（herringbone）

需要改動哪些地方？哪個假設會被打破？

<details><summary>提示</summary>

- `PatternKind`（`src/TilePlan.Core/Tiles/Pattern.cs`）加列舉值，`Pricing` 加工資與損耗率。
- 真正的難點在 `Lattice`：人字貼的磚**不是**同一個方向，「一格 = 一片磚 + 右側縫 + 上方縫」的定義不再成立。`LayoutEngine` 假設每片磚在磚格座標中都是 [u, u + TileU] × [v, v + TileV]。
- 可以考慮把人字貼看成「兩組互相垂直的交丁」疊加：兩個 `Lattice`，各自排版後合併。`JointNetwork` 的縫面積也要跟著改。
- `OffsetOptimizer` 的 `WallAxis` 與解析候選都假設磚只有一個方向。

</details>

### 練習 2：讓餘料可以跨房間重用

同一種磚用在浴室和客廳時，浴室切剩的料可以拿到客廳用。

<details><summary>提示</summary>

- `OffcutPlanner.Plan()` 目前收一個 `LayoutResult`。改成收「同一種磚的多個房間的切割片」，排序與配置邏輯不變。
- 切割片的編號（`CutNo`）只在單一房間內唯一，跨房間需要加上房間識別。
- `ProjectCalculator.ComputeRoom()` 是逐房間計算，`ProjectStore` 也是逐房間快取。跨房間重用會讓「改 A 房間會影響 B 房間的原磚數」，快取鍵要包含所有用同一種磚的房間。
- 箱數已經是跨房間合併（D1），所以材料估算的修改相對小。

</details>

### 練習 3：用形態學侵蝕改進細碎片偵測

目前 `PieceGeometry.MinWidth()` 用凸包，L 形切片會被高估。

<details><summary>提示</summary>

- 「最小寬度 < t」等價於「把形狀往內縮 t/2 後會斷開或消失的部分存在」。`PolygonOps.Inflate(region, -t / 2)` 就能做往內縮。
- 判斷方式之一：往內縮再往外擴（開運算，opening），比較與原形狀的面積差；差異大代表有細於 t 的部分。
- 先在 `tests/TilePlan.Core.Tests/Layout/SmallPieceTests.cs` 寫一個 L 形切片的測試（細腳 3 cm、整體 20 × 20），目前的實作應該判定它不是細碎片，新實作要判定是。
- 這仍然只用 Clipper 做布林運算（擴張），符合題目限制 NFR-03。

</details>

### 練習 4：讓量測方向自動判斷

目前使用者要勾選「我是順時針量的」。能不能自動判斷？

<details><summary>提示</summary>

- `SurveySolver` 的 `IsSimpleWithOrientation()` 用 `input.Clockwise` 排除鏡像。如果兩個方向都試，會得到兩個互為鏡像的解，它們的所有距離都相同。
- 所以單靠長度**無法**判斷方向：鏡像的房間量起來完全一樣。這是一個「看起來可以做、其實資訊不足」的需求。
- 實務上可以改問一個不需要專業知識的問題，例如「量的時候房間在你的左手邊還是右手邊？」，或在預覽圖上提供「左右翻轉」按鈕。

</details>

## 延伸學習

| 主題 | 本專案中的位置 | 搜尋關鍵字 |
|---|---|---|
| 剛性理論 | `RigidityAnalyzer` | `rigidity matrix generic rigidity Laman theorem` |
| 非線性最小平方 | `LeastSquares.Refine()` | `Levenberg-Marquardt algorithm`、`Gauss-Newton` |
| 誤差傳播 | `Uncertainty` | `error propagation covariance Jacobian`、`confidence ellipse chi-square` |
| 多邊形布林運算 | `PolygonOps` | `Clipper2 polygon clipping`、`Vatti clipping algorithm` |
| 線段裁切 | `LayoutEngine.CrossesOpenRect()` | `Liang-Barsky line clipping` |
| 2D 裝箱 | `OffcutPlanner` | `2D bin packing heuristics`、`best fit decreasing`、`nesting problem irregular shapes` |
| 凸包與最小寬度 | `PieceGeometry` | `Andrew monotone chain`、`rotating calipers minimum width` |
| 標籤位置 | `Svg.LabelPlacement()` | `pole of inaccessibility polylabel` |
| 性質測試 | `LayoutPropertyTests` | `property-based testing`、`FsCheck` |
| Blazor WASM 效能 | `LayoutStep.razor`、`tileplan.js` | `Blazor WebAssembly performance best practices`、`requestAnimationFrame measure paint` |
| Blazor trimming | `ProjectSerializer` | `Blazor trimming System.Text.Json source generator` |
| 浮點數與金額 | `MaterialEstimator`、`Area` | `floating point decimal rounding money`、`banker's rounding vs away from zero` |
