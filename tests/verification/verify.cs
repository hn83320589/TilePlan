#:project ../../src/TilePlan.Core/TilePlan.Core.csproj
#:property Configuration=Release
#:property PublishAot=false
using TilePlan.Core.Geometry; using TilePlan.Core.Layout; using TilePlan.Core.Tiles; using TilePlan.Core.Cutting;
using TilePlan.Core.Survey; using TilePlan.Core.Offset; using TilePlan.Core.Estimation;

// 獨立驗證腳本（測試報告 docs/test-report.md 第 4 節）
// 執行：dotnet run tests/verification/verify.cs
// 使用與單元測試不同的亂數種子與房間產生器，確認性質不依賴特定的隨機樣本。
var rng = new Random(777);
Polygon R(double x,double y,double w,double h)=>new([new(x,y),new(x+w,y),new(x+w,y+h),new(x,y+h)]);
double Rnd(double a,double b)=>Math.Round(a+rng.NextDouble()*(b-a),1);
Pattern[] pats = [Pattern.Straight, Pattern.RunningBond(BondRatio.Half), Pattern.RunningBond(BondRatio.Third), Pattern.Diagonal];

// --- AC8 / AC9：500 個隨機凸多邊形＋旋轉不鋪設區 ---
var errs = new List<double>(); int reuseWorse = 0, reuseSaved = 0, fitViolations = 0; int n8 = 500;
for (int k = 0; k < n8; k++) {
  int m = rng.Next(3, 9);
  var ang = Enumerable.Range(0,m).Select(_=>rng.NextDouble()*2*Math.PI).Order().ToArray();
  var room = new Polygon(ang.Select(a => Vec2.FromAngle(a) * Rnd(120, 320)).ToArray());
  if (!room.IsSimple() || room.Area < 1000) { k--; continue; }
  var ex = Enumerable.Range(0, rng.Next(0,3)).Select(_ => R(Rnd(-200,150), Rnd(-200,150), Rnd(10,80), Rnd(10,80))).ToList();
  var region = Region.Tileable(room, ex); if (region.IsEmpty) { k--; continue; }
  var tile = new TileSpec(Rnd(8,90), Rnd(8,60), 9, Math.Round(rng.NextDouble()*6,1), 10, 100);
  var lat = Lattice.Create(tile, pats[k%4], new Baseline(new Vec2(Rnd(-100,100),Rnd(-100,100)), Rnd(0,360)), Rnd(0,90), Rnd(0,90));
  var lay = LayoutEngine.Layout(region, lat);
  errs.Add(Math.Abs(lay.PieceArea + JointNetwork.Area(lay) - region.Area) / region.Area);
  if (k % 3 == 0) {
    var off = OffcutPlanner.Plan(lay, new CutSettings(ReuseOffcuts:false)); var on = OffcutPlanner.Plan(lay, CutSettings.Default);
    if (on.StockCount > off.StockCount) reuseWorse++; reuseSaved += off.StockCount - on.StockCount;
    foreach (var a in on.Assignments.Where(a => a.FromOffcut))
      if (PolygonOps.Difference(a.Placed, a.Source).Area > OffcutPlanner.FitTolerance(a.Placed)) fitViolations++;
  }
}
errs.Sort();
Console.WriteLine($"AC8 rooms={errs.Count} maxRelErr={errs[^1]:E2} p50={errs[errs.Count/2]:E2} over0.1%={errs.Count(e=>e>=1e-3)}");
Console.WriteLine($"AC9 rooms={(n8+2)/3} reuseWorse={reuseWorse} tilesSaved={reuseSaved} fitViolations={fitViolations}");

// --- AC3：L 形加上捲尺誤差；起點換成「扇形對角線都在室內」的兩個角，順／逆時針都測 ---
Vec2[] L = [new(0,0), new(400,0), new(400,150), new(250,150), new(250,300), new(0,300)];
foreach (double noiseCm in new[]{0.0, 0.05, 0.1, 0.2}) {
  double worst = 0; int solved = 0, trials = 0;
  foreach (int shift in new[]{0, 3}) foreach (bool cw in new[]{false, true}) for (int t = 0; t < 50; t++) {
    trials++;
    var pts = Enumerable.Range(0,6).Select(i => L[(i+shift)%6]).ToArray();
    if (cw) { var first = pts[0]; pts = new[]{first}.Concat(pts.Skip(1).Reverse()).ToArray(); }
    double nz() => (rng.NextDouble()*2-1)*noiseCm;
    var walls = pts.Select((p,i)=>Math.Round(p.DistanceTo(pts[(i+1)%6]) + nz(),1)).ToArray();
    var diags = new[]{2,3,4}.Select(j => new Diagonal(0, j, Math.Round(pts[0].DistanceTo(pts[j]) + nz(),1))).ToList();
    var res = SurveySolver.Solve(new SurveyInput(walls, diags, cw));
    if (res.Status != SurveyStatus.Solved) continue;
    solved++;
    var a0 = pts[0]; var dir = (pts[1]-a0).Normalized();
    var exp = pts.Select(p => new Vec2((p-a0).Dot(dir), (p-a0).Dot(dir.Perp()))).ToArray();
    worst = Math.Max(worst, exp.Zip(res.Polygon!.Vertices, (e,g)=>e.DistanceTo(g)).Max());
  }
  Console.WriteLine($"AC3 量測誤差 ±{noiseCm} cm（再四捨五入到 0.1 cm）：{solved}/{trials} 解出，最大頂點誤差 {worst:0.000} cm");
}

// --- 發現 F2：對角線穿過牆外（捲尺量不到）時，系統仍回報「已解出」 ---
{
  var pts = Enumerable.Range(0,6).Select(i => L[(i+1)%6]).ToArray();   // 從 (400,0) 開始，扇形對角線會穿過 L 的缺口
  var walls = pts.Select((p,i)=>p.DistanceTo(pts[(i+1)%6])).ToArray();
  var res = SurveySolver.Solve(new SurveyInput(walls, new[]{2,3,4}.Select(j => new Diagonal(0, j, pts[0].DistanceTo(pts[j]))).ToList()));
  var a0 = pts[0]; var dir = (pts[1]-a0).Normalized();
  var exp = pts.Select(p => new Vec2((p-a0).Dot(dir), (p-a0).Dot(dir.Perp()))).ToArray();
  double err = res.Polygon is null ? double.NaN : exp.Zip(res.Polygon.Vertices, (e,g)=>e.DistanceTo(g)).Max();
  Console.WriteLine($"F2 對角線穿牆的輸入：狀態 {res.Status}，與真實形狀的最大頂點差 {err:0.0} cm");
}

// --- AC4：各種多邊形只量牆長都必須拒絕 ---
int refused = 0;
foreach (var walls in new double[][]{ [300,200,300,200], [400,150,150,150,250,300], [320,213,281,210] })
  if (SurveySolver.Solve(new SurveyInput(walls, [])).Status == SurveyStatus.Underdetermined) refused++;
Console.WriteLine($"AC4 refused={refused}/3");

// --- AC7：隨機長方形正排一定達標；交丁 1/2 的達成率（資訊）---
int straightOk = 0, bondOk = 0, nRect = 150; double minStrip = double.MaxValue;
for (int t = 0; t < nRect; t++) {
  double w = Rnd(90, 600), h = Rnd(90, 600); var room = R(0,0,w,h);
  var tile = new TileSpec(Rnd(10,80), Rnd(10,60), 9, Math.Round(rng.NextDouble()*5,1), 10, 100);
  if (Math.Max(tile.LengthCm, tile.WidthCm) > Math.Min(w,h)) { t--; continue; }
  var r1 = OffsetOptimizer.Optimize(room, new Region(room), Lattice.Create(tile, Pattern.Straight, new Baseline(new Vec2(Rnd(0,w),0), t%2==0?0:90)));
  if (r1.Report.Satisfied) straightOk++;
  foreach (var p in r1.Layout.CutPieces) minStrip = Math.Min(minStrip, Math.Min(p.LocalBounds.Width / (r1.Lattice.TileU/3), p.LocalBounds.Height / (r1.Lattice.TileV/3)));
  var r2 = OffsetOptimizer.Optimize(room, new Region(room), Lattice.Create(tile, Pattern.RunningBond(BondRatio.Half), Baseline.Default));
  if (r2.Report.Satisfied) bondOk++;
}
Console.WriteLine($"AC7 straight satisfied {straightOk}/{nRect}, min strip/threshold ratio {minStrip:0.000}; runningBond½ satisfied {bondOk}/{nRect}");

// --- 決定性：同一輸入算兩次，結果逐片相同 ---
var lt = Lattice.Create(new TileSpec(60,30,9,2,8,1000), Pattern.Diagonal, Baseline.Default);
string Sig(OffsetResult r) => string.Join("|", r.Layout.Pieces.Select(p => $"{p.I},{p.J},{p.Kind},{p.Area:R}")) + OffcutPlanner.Plan(r.Layout, CutSettings.Default).StockCount;
var room2 = new Polygon([new(0,0), new(520,0), new(520,280), new(330,280), new(330,420), new(0,420)]);
Console.WriteLine($"Determinism identical={Sig(OffsetOptimizer.Optimize(room2, new Region(room2), lt)) == Sig(OffsetOptimizer.Optimize(room2, new Region(room2), lt))}");
