# 07 程式內容導讀

這一章帶你實際讀一遍程式碼：先讀哪些檔案、程式怎麼啟動、一次修改如何穿過每一層，最後逐個模組整理值得學的寫法與可以改進的地方。演算法本身的細節留到 08 章。

## 建議的閱讀順序

| 順序 | 檔案 | 為什麼先讀它 |
|---|---|---|
| 1 | `src/TilePlan.Core/Projects/Project.cs` | 存檔模型就是「使用者能輸入的全部東西」，讀完就知道系統的邊界 |
| 2 | `src/TilePlan.Core/Projects/ProjectCalculator.cs` | 115 行就是整條計算管線，每一步呼叫哪個模組一目了然 |
| 3 | `src/TilePlan.Core/Geometry/`（`Vec2`、`Polygon`、`Region`、`PolygonOps`） | 其他模組都用這些型別；`PolygonOps` 是唯一碰 Clipper 的地方 |
| 4 | `src/TilePlan.Core/Tiles/Lattice.cs` | 磚格座標系是排版、偏移、切割三個模組的共同語言 |
| 5 | `src/TilePlan.Core/Layout/LayoutEngine.cs` | 系統最核心的計算，也是效能的關鍵 |
| 6 | `src/TilePlan.Web/Program.cs`、`src/TilePlan.Web/Services/ProjectStore.cs` | 介面層怎麼持有狀態、怎麼快取 |
| 7 | `src/TilePlan.Web/Pages/RoomPage.razor`、`src/TilePlan.Web/Components/LayoutStep.razor` | 一個分頁怎麼拿結果、怎麼畫 |
| 8 | 其餘模組（Survey、Offset、Cutting） | 演算法密集，搭配 08 章讀 |

## 啟動：`Program.cs`

```csharp
// src/TilePlan.Web/Program.cs
builder.Services.AddSingleton<TilePlanJs>();
builder.Services.AddSingleton<IBrowserStorage, LocalStorage>();
builder.Services.AddSingleton<ProjectStore>();
builder.Services.AddSingleton<AutoSaveService>();

var host = builder.Build();

// 重新整理後還原自動存檔；第一次使用（或存檔損壞）時開啟範例專案
var store = host.Services.GetRequiredService<ProjectStore>();
var autoSave = host.Services.GetRequiredService<AutoSaveService>();
if (!await autoSave.RestoreAsync())
    store.Replace(SampleProject.Load());
autoSave.Start();

await host.RunAsync();
```

- 四個服務都是 singleton。在 Blazor WebAssembly 裡，singleton 的範圍就是「這個瀏覽器分頁」，等於全域狀態，這正是 `ProjectStore` 需要的。
- 還原放在 `host.Build()` 之後、`RunAsync()` 之前：第一個頁面渲染時專案已經就緒，不需要在每個頁面處理「還在載入中」的狀態。
- `SampleProject.Load()` 讀的是**內嵌資源**。`TilePlan.Web.csproj` 用 `<EmbeddedResource Include="..\..\samples\*.json" LogicalName="TilePlan.Web.%(Filename)%(Extension)" />` 把 repo 根目錄的範例檔編進組件。好處是範例只有一份來源：Core 測試、E2E 測試、網頁讀的都是 `samples/` 下同一個檔案。

## 一次修改的完整生命週期：改縫寬

這是 06 章時序圖的程式碼版本。從使用者在排版畫布上改縫寬開始。

### 第 1 站：元件修改輸入、通知變更

```csharp
// src/TilePlan.Web/Components/LayoutStep.razor
private async Task ApplyJoint(double mm)
{
    _changeStartedAt = await (await Js.Module()).InvokeAsync<double>("now");
    Tile!.JointMm = mm;
    await OnChanged.InvokeAsync();
}
```

元件**直接修改** `TileItem.JointMm`，然後透過 `OnChanged` 一路傳到 `RoomPage` 的 `Changed()`，呼叫 `Store.NotifyChanged()`。存檔模型是可變類別（`Project.cs` 的註解：「方便 UI 直接繫結編輯」），所以不需要「建立新物件、替換舊物件」的流程。代價是任何持有參考的地方都看得到變化，所以結果的快取不能依賴物件參考，必須依賴內容（下一站）。

計時從 `now()` 開始，用的是瀏覽器的 `performance.now()`，與最後的 `afterPaint()` 在同一個時鐘上。

### 第 2 站：快取判斷

```csharp
// src/TilePlan.Web/Services/ProjectStore.cs
public RoomResult Result(Room room)
{
    string key = ProjectSerializer.RoomKey(Project, room);
    if (_cache.TryGetValue(room.Id, out var cached) && cached.Key == key) return cached.Result;

    var sw = Stopwatch.StartNew();
    var result = ProjectCalculator.ComputeRoom(Project, room);
    _cache[room.Id] = new Cached(key, result, sw.Elapsed.TotalMilliseconds);
    return result;
}
```

快取鍵是「這間房 + 它用的磁磚 + 單價設定」的壓縮 JSON（`ProjectSerializer.RoomKey()`）。因為模型是可變的，比較內容是唯一可靠的失效方式；而序列化器本來就存在、本來就保證決定性，拿來當指紋幾乎不用寫新程式碼。

改縫寬時，鍵裡的 `jointMm` 變了，快取失效。報價單頁面一次要所有房間的結果，沒改到的房間會命中快取。

### 第 3 站：計算管線

```csharp
// src/TilePlan.Core/Projects/ProjectCalculator.cs（ComputeRoom 節錄）
var (polygon, survey, shapeError) = ResolveShape(room.Shape);
if (polygon is null)
    return Fail(room, shapeError!, survey: survey);
// ...
var region = Region.Tileable(polygon, exclusions.Select(e => e.Shape));
var checkedEdges = exclusions.Where(e => e.Source.CheckEdges).Select(e => (e.Source.Name, e.Shape)).ToList();
// ...
var template = Lattice.Create(tile, room.ToPattern(), room.ToBaseline(), room.OffsetU, room.OffsetV);
if (room.OffsetMode == OffsetMode.Auto)
{
    var optimized = OffsetOptimizer.Optimize(polygon, region, template, checkedEdges);
    (layout, report) = (optimized.Layout, optimized.Report);
}
else
{
    layout = LayoutEngine.Layout(region, template);
    report = OffsetOptimizer.Report(polygon, layout, checkedEdges);
}

var cuts = OffcutPlanner.Plan(layout, new CutSettings(room.ReuseOffcuts, room.AllowQuarterTurns, room.KerfMm / 10));
decimal waste = room.WasteRateOverride ?? project.Pricing.DefaultWasteRate(room.Pattern);
var materials = MaterialEstimator.Estimate(layout, cuts, tile, waste, project.Pricing.AdhesiveKgPerM2, project.Pricing.GroutDensityKgPerM3);
```

讀這段時注意三件事：

1. **每一步失敗都提早回傳，並保留已經算出的部分。** `Fail(room, ..., polygon, survey)` 把多邊形與實測結果帶出去，形狀分頁仍能畫出預覽。
2. **自動與手動偏移走同一份報告。** 手動模式不做最佳化，但仍用 `OffsetOptimizer.Report()` 以精確排版量出每面牆的切條。使用者手動調偏移時看到的數字，與自動模式的判定方式完全相同。
3. **單位在邊界轉換。** `room.KerfMm / 10` 是整條管線裡少數幾個 mm → cm 的地方，其他都在 `TileSpec` 與 `RoomShape.ToSurvey()` 內部處理。

`OffsetOptimizer.Optimize()` 內部會先用快速評估搜尋上百個候選偏移，最後只對選中的那一個呼叫 `LayoutEngine.Layout()`。這是效能預算的主要部分，見 08 章。

### 第 4 站：結果轉成 SVG

```csharp
// src/TilePlan.Web/Components/LayoutStep.razor
protected override void OnParametersSet()
{
    if (!Result.Ok || ReferenceEquals(_renderedFor, Result)) return;
    _renderedFor = Result;

    var lay = Result.Layout!;
    var reused = Result.Cuts!.Assignments.Where(a => a.FromOffcut).Select(a => a.Piece.CutNo).ToHashSet();
    _regionPath = Svg.Path(Result.Region!);
    _fullPath = Svg.Path(lay.Pieces.Where(p => p.Kind == PieceKind.Full).Select(p => p.Shape));
    _cutPath = Svg.Path(lay.CutPieces.Where(p => !reused.Contains(p.CutNo)).Select(p => p.Shape));
    _reusePath = Svg.Path(lay.CutPieces.Where(p => reused.Contains(p.CutNo)).Select(p => p.Shape));
    // ...
}
```

- `ReferenceEquals(_renderedFor, Result)`：快取命中時 `ProjectStore` 回傳同一個 `RoomResult` 物件，元件就不重建字串。這裡用參考比較是安全的，因為 `RoomResult` 是不可變的 record，同一個參考一定是同一份內容。
- **同類磚合成一條 path**：`Svg.Path(IEnumerable<Region>)` 把所有整片磚串成一個 `d` 屬性。1,200 片磚只產生 3 個 `<path>` 元素，Blazor 的 diff 只要比較 3 個字串屬性，瀏覽器也只要排版 3 個節點。
- 代價是**點選磚片不能靠 DOM 事件**（沒有個別元素可以點）。`OnCanvasClick()` 改成拿世界座標，在 .NET 端用 `piece.Shape.Contains(p)` 逐片找。1,200 次點在多邊形判斷只要幾毫秒，而且只在點擊時發生。

### 第 5 站：量到畫面真的畫出來

```csharp
// src/TilePlan.Web/Components/LayoutStep.razor
protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (_changeStartedAt is { } start)
    {
        _changeStartedAt = null;
        var painted = await (await Js.Module()).InvokeAsync<double>("afterPaint");
        _lastTotalMs = painted - start;
        if (!_benchmarking) StateHasChanged();
    }
}
```

`OnAfterRenderAsync` 只代表 Blazor 把 DOM 更新送出去了，瀏覽器還沒畫。`afterPaint()` 等兩個 `requestAnimationFrame`：第一個 frame 的 callback 在下一次繪製**之前**執行，第二個在那次繪製**之後**的下一個 frame 執行，這時畫面一定已經畫出來了。這是量測「使用者真的看到」的常見技巧。

## 各模組

### Geometry：幾何基礎

**職責**：點、多邊形、區域、面積，以及 Clipper 的轉接層。

**關鍵型別**：`Vec2`（`readonly record struct`）、`Polygon`（不可變、頂點不重複首點）、`Region`（多個多邊形，逆時針外框、順時針洞）、`Area`、`PolygonOps`、`Segment`。

**值得學的寫法**：

- `Vec2` 是 `readonly record struct`，有運算子多載與 `Cross()`、`Perp()`。幾何程式碼因此讀起來像數學式：`(b - a).Cross(c - a)`。struct 避免每個點都配置一個物件，排版時會產生上萬個點。
- `Region` 用**有號面積**表達洞：`Region.Area => Polygons.Sum(p => p.SignedArea)`。不需要額外的「這是洞」旗標，與 Clipper 的 NonZero 填色規則一致。
- `Area.RoundForDisplay()` 先轉 `decimal`、先捨到 9 位再捨到 2 位：

  ```csharp
  // src/TilePlan.Core/Geometry/Area.cs
  private static decimal RoundForDisplay(double value) =>
      // 先轉 decimal 再捨入：避免 0.125 在 double 中是 0.12499999… 而捨成 0.12
      Math.Round(Math.Round((decimal)value, 9), 2, MidpointRounding.AwayFromZero);
  ```

  `double` 算出的 0.125 可能是 0.12499999999。直接四捨五入會變 0.12，但人手算是 0.13。先捨到 9 位把雜訊吃掉，再做真正的兩位四捨五入。
- `PolygonOps.RectClip()` 的註解說明了為什麼結果要再做一次聯集：Clipper 的 `RectClip` 逐條路徑處理，含洞時結果不是正規化的區域。這種「套件行為的坑」寫在呼叫處，比寫在外部文件有用。

**可以改進的地方**：`Polygon.Contains()` 的註解寫「落在邊上的點結果不保證」。排版用格子中心點判斷，中心點剛好落在牆上的情況理論上存在（磚格與牆對齊且偏移剛好半格）。目前靠 `LayoutEngine` 先判斷「有沒有邊穿過這格」來避開，但這個前提只寫在演算法裡，沒有寫在 `Contains()` 的契約上。

### Tiles：磚格座標系

**職責**：把「磚的尺寸 + 鋪法 + 基準線 + 偏移」變成一個座標系，回答「第 (i, j) 片磚在哪裡」。

**關鍵型別**：`TileSpec`、`Pattern`、`Baseline`、`Lattice`。

**值得學的寫法**：`Lattice` 的類別註解用 ASCII 圖定義「一格 = 一片磚 + 右側縫 + 上方縫」。這個定義讓格子剛好鋪滿平面、互不重疊，於是：

- 縫面積可以逐格加總（`Lattice.JointRects()`），不會重複計算交叉點；
- 交丁只是每排的 u 方向位移 `RowShift(j)`，其他程式碼完全不用知道鋪法；
- 45° 斜貼只是 `Lattice.Create()` 把角度加 45°，排版引擎看到的仍然是正排。

把三種鋪法化約成「同一個座標系的不同參數」，是這個專案最省程式碼的設計。

### Layout：排版

**職責**：給定可鋪區域與磚格，產生每一片磚（整片或切割片）。

**關鍵型別與方法**：`LayoutEngine.Layout()`、`BoundaryCells()`、`CrossesOpenRect()`、`Piece`、`LayoutResult`、`JointNetwork.Area()`、`PieceGeometry.MinWidth()`。

**值得學的寫法**：

- **轉換一次區域，而不是轉換每一片磚。** `Layout()` 第一件事是把整個區域轉到磚格座標（`p.Transform(lattice.ToLattice)`），之後每片磚都是軸對齊長方形，可以用 Clipper 最快的 `RectClip`。只有切割片的結果才轉回世界座標。
- `Piece` 同時保存 `Shape`（鋪在房間裡的形狀）與 `LocalShape`（在原磚上的形狀）。切割計畫、檢視器、切割清單各取所需，不必反覆做座標轉換。
- `JointNetwork` 的類別註解直接寫出它存在的理由：縫面積必須獨立計算，面積守恆測試才有意義。見 09 章。

**可以改進的地方**：`LayoutResult.DisplayIndex()` 每次呼叫都要掃描全部磚片找最小的排與欄。`CutListStep.razor` 對每一片切割片呼叫一次，複雜度是 O(切割片數 × 磚片數)。1,200 片磚時仍然很快，但可以在 `LayoutResult` 建構時預先算好每排的起始欄。

### Survey：實測推算

**職責**：牆長 + 對角線 → 房間多邊形，以及「量得夠不夠、準不準」。

**關鍵型別**：`SurveySolver`、`RigidityAnalyzer`、`LeastSquares`、`Uncertainty`、`SurveyInput`、`SurveyResult`。

**值得學的寫法**：

- **錯誤訊息是給師傅看的指示，不是給工程師看的錯誤。** 例如三角不等式不成立時，訊息的格式是「A–C（100）、B–C（150）與 A–B（300）不滿足三角不等式，差了 50 cm，請確認這幾條量測。」（`Search.CircleIntersections()` 產生），直接告訴使用者要重量哪幾條。
- `SurveyResult` 的新欄位（`Alternatives`、`Redundancy`、`VertexUncertainty`、`CheckDiagonal`）用 `init` 屬性加上預設值，而不是加進主建構子。既有的 `Fail()` 與測試都不用改，這是擴充 record 的好方法。
- `LeastSquares` 用 `VarX()`／`VarY()` 把「頂點 v 的 x、y」對應到未知數向量的索引，A 點與 B 點的 y 固定，回傳 -1。`Uncertainty` 重用同一組索引函式讀共變異數矩陣，兩邊不可能對不上。

**可以改進的地方**：`SurveySolver` 的 `Search` 有 `MaxLeaves = 200_000` 的上限，超過就停止搜尋，但不會告訴使用者結果可能不完整。一般房間（n ≤ 10）的搜尋樹遠小於此，只是這個安全閥是無聲的。

### Offset：自動偏移

**職責**：找出讓切條最好看的磚格偏移，並產生每面牆、每條不鋪設區邊緣的切條報告。

**關鍵型別與方法**：`OffsetOptimizer.Optimize()`、`OffsetOptimizer.Report()`、`Wall`（內部類別）、`EdgeStripEvaluator`、`WallStrip`、`OffsetReport`、`Tenths`。

**值得學的寫法**：

- 評分是一個 `readonly record struct Score`，比較規則寫成 `BetterThan()`，每一層都有自己的容許量（不足量 1e-6、對稱 0.05 cm）。字典序比較用浮點數時一定要有容許量，否則 0.0000001 的差異就會讓後面的準則失去作用。
- **快速評估與精確報告用同一個定義。** 兩者都把「同時貼著另一面不平行牆」的切片當轉角片處理。commit `25f2661` 的說明特別寫了這一點：定義不一致時，最佳化會以為達標、報告卻顯示不足。
- `WallStrip` 的 `DisplayThreshold`／`DisplayMinWidth` 把「顯示用的數字」與「判定用的數字」分開，並保證兩者的大小關係一致（見 11 章踩坑）。

**可以改進的地方**：

- `Search` 的建構呼叫了兩次 `FacingPairs(walls)`（`_facing` 與 `_facingIndex` 各一次），`BestSeen()` 每次用 `walls.IndexOf(w)` 線性搜尋。牆數很少時無感，但可以合併。
- `Wall` 依賴 `SurveySolver.LabelPair()` 產生牆名，見 03 章。

### Cutting：餘料重用

**職責**：安排每片切割片從哪片原磚、哪個位置切下。

**關鍵型別**：`OffcutPlanner`、`CutSettings`、`CutPlan`、`StockTile`、`CutAssignment`。

**值得學的寫法**：

- `CutAssignment.Source` 保存「切下之前那塊餘料的形狀」。這個欄位只為了**驗證**而存在：測試可以檢查每次重用的切割片確實包含在來源餘料中（驗收 9），而不必重新模擬整個配置過程。
- 失敗快取用形狀指紋 `Signature()`：平移到外框原點後、頂點座標取到 0.01 mm 組成字串。45° 斜貼時同一種三角形會出現幾十次，第一次試過放不進某塊餘料，之後同形狀就跳過。

**可以改進的地方**：`CutPlan.Find()` 對所有指派做線性搜尋。檢視器每次點選呼叫一次，目前沒問題；若要在畫面上同時標示所有切割片的原磚編號，就該改成字典。

### Estimation 與 Quoting：材料與報價

**職責**：片數、箱數、包數、金額。

**值得學的寫法**：`MaterialEstimator.Bags()` 先把公斤數四捨五入到公克再無條件進位：

```csharp
// src/TilePlan.Core/Estimation/MaterialEstimator.cs
public static int Bags(double kg, double kgPerBag)
{
    if (!(kgPerBag > 0)) throw new ArgumentOutOfRangeException(nameof(kgPerBag), "每包重量必須大於 0。");
    decimal grams = Math.Round((decimal)kg, 3);
    return (int)Math.Ceiling(grams / Math.Round((decimal)kgPerBag, 3));
}
```

無條件進位對浮點雜訊極度敏感：40.0000000001 kg 會變成 3 包而不是 2 包。「先決定有意義的精度、捨掉雜訊、再進位」是所有 ceiling 運算都該有的模式。`!(kgPerBag > 0)` 的寫法同時擋掉 0、負數與 `NaN`。

`Quote.GrandTotal` 是「各行已四捨五入的金額」加總，而不是先加總再四捨五入。報價單上的每個數字自己加得起來，業主拿計算機驗算不會差 1 元。

### Projects：存檔模型與序列化

見 06 章「專案檔格式」。值得一提的是 `ProjectSerializer.Clone()` 用「序列化再反序列化」做深複製：一行程式碼，而且保證複製出來的物件與存檔後讀回的物件完全一樣。

### Web Services：狀態與儲存

**`AutoSaveService`** 是這一層最值得細讀的類別：

```csharp
// src/TilePlan.Web/Services/AutoSaveService.cs
private void OnChanged()
{
    _dirty = true;
    if (_pump.IsCompleted) _pump = PumpAsync();
}

private async Task PumpAsync()
{
    while (_dirty)
    {
        _dirty = false;
        await SaveNowAsync();
    }
}
```

這是一個不用計時器的「合併寫入」：

- 第一次修改：`_pump` 已完成，啟動 `PumpAsync()`，立刻寫入。
- 寫入進行中又修改了 9 次：每次只把 `_dirty` 設成 true，`_pump` 還沒完成所以不另外啟動。
- 第一次寫入結束：迴圈看到 `_dirty`，再寫一次**最新狀態**，然後結束。

結果是 10 次修改只寫 2 次，而且最後一次一定是最新內容；沒有 debounce 的空窗期，修改後立刻重新整理也不會遺失。WebAssembly 是單執行緒，所以 `_dirty` 不需要鎖。`WhenIdle()` 把 `_pump` 公開給測試等待，測試不必用 `Task.Delay` 猜時間。

**可以改進的地方**：每次修改都同步序列化整個專案。測試報告 §10 記錄交丁 1/3 的效能量測在這個改動後慢了約 11%，但無法確定是不是這個原因。專案很大時，可以改成只在 `requestIdleCallback` 或 `pagehide` 時序列化。

### Web Components：畫布與分頁

- **`Canvas.razor`** 是所有畫布的共用外殼：`<g data-world transform="scale(1,-1)">` 把 SVG 的 y 軸翻成向上，內容一律用世界座標（cm）。各分頁只提供 `ChildContent` 與事件處理，不必各自處理平移縮放。
- **`RoomPage.razor`** 在 markup 裡呼叫 `Store.Result(_room)`，把結果當參數傳給當前分頁。每個分頁都是「拿 `Room` 與 `RoomResult`，改了就呼叫 `OnChanged`」的同一個形狀，新增分頁很容易。
- **`PieceInspector.razor`** 把切割片的 `LocalShape` 畫在原磚的虛線框上，並標出各邊長；同樣長度的邊只標一次，避免細長磚的標籤重疊（見 11 章）。

**值得學的地方**：Blazor 迴圈裡的 lambda 要先把迴圈變數複製成區域變數，例如 `ExclusionStep.razor` 的 `int index = k;` 再寫 `@onclick="() => Select(index)"`。直接用 `k` 的話，所有按鈕都會拿到迴圈結束時的值（見 11 章踩坑）。

### Rendering：SVG 輔助

`Svg.LabelPlacement()` 決定切割清單上編號標籤放在哪裡、字多大：在切割片的外框內取 16 × 8 個樣本點，找離邊界最遠的點；如果很多點一樣遠（長條形），取最靠中心的那個。字級受「到邊界的距離」限制，所以窄條上的字會自動縮小，不會超出磚面。這是「最難到達點」（pole of inaccessibility）的粗略近似，用取樣代替精確演算法，對 UI 標籤來說夠用。
