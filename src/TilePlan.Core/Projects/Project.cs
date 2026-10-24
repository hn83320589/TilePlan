using TilePlan.Core.Geometry;
using TilePlan.Core.Quoting;
using TilePlan.Core.Survey;
using TilePlan.Core.Tiles;

namespace TilePlan.Core.Projects;

/// <summary>
/// 存檔模型：只保存「輸入」，所有排版與報價結果都由 <see cref="ProjectCalculator"/> 從輸入算出。
/// 可變類別，方便 UI 直接繫結編輯。
/// </summary>
public sealed class Project
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Name { get; set; } = "新專案";
    public string Client { get; set; } = "";
    public List<TileItem> Tiles { get; set; } = [];
    public Pricing Pricing { get; set; } = Pricing.Default;
    public List<Room> Rooms { get; set; } = [];

    public TileItem? FindTile(string id) => Tiles.FirstOrDefault(t => t.Id == id);
}

public sealed class TileItem
{
    public string Id { get; set; } = NewId();
    public string Name { get; set; } = "磁磚";
    public double LengthCm { get; set; } = 60;
    public double WidthCm { get; set; } = 30;
    public double ThicknessMm { get; set; } = 9;
    public double JointMm { get; set; } = 2;
    public int PiecesPerBox { get; set; } = 8;
    public decimal PricePerBox { get; set; } = 1000;

    public TileSpec ToSpec() => new(LengthCm, WidthCm, ThicknessMm, JointMm, PiecesPerBox, PricePerBox);

    public static string NewId() => Guid.NewGuid().ToString("N")[..8];
}

public enum ShapeKind
{
    Polygon,
    Survey,
}

public enum OffsetMode
{
    Auto,
    Manual,
}

/// <summary>座標點（cm）。可變類別，UI 才能直接繫結頂點座標。</summary>
public sealed class Point2
{
    public double X { get; set; }
    public double Y { get; set; }

    public Point2() { }

    public Point2(double x, double y) => (X, Y) = (x, y);

    public Vec2 ToVec() => new(X, Y);

    public static Point2 From(Vec2 v) => new(v.X, v.Y);
}

public sealed class RoomShape
{
    public ShapeKind Kind { get; set; } = ShapeKind.Polygon;

    /// <summary>座標繪製模式的頂點（cm）。</summary>
    public List<Point2> Vertices { get; set; } = [];

    /// <summary>實測模式：依序的牆長（cm）。</summary>
    public List<double> Walls { get; set; } = [];

    public List<Diagonal> Diagonals { get; set; } = [];

    /// <summary>實測時是否沿順時針方向量。</summary>
    public bool Clockwise { get; set; }

    /// <summary>捲尺精度（mm）：95% 的量測誤差在 ±此值以內。</summary>
    public double TapePrecisionMm { get; set; } = 5;

    public SurveyInput ToSurvey() => new(Walls, Diagonals, Clockwise, TapePrecisionMm / 10);
}

public sealed class Exclusion
{
    public string Name { get; set; } = "不鋪設區";
    public List<Point2> Vertices { get; set; } = [];
}

public sealed class Room
{
    public string Id { get; set; } = TileItem.NewId();
    public string Name { get; set; } = "房間";
    public RoomShape Shape { get; set; } = new();
    public List<Exclusion> Exclusions { get; set; } = [];

    public string TileId { get; set; } = "";
    public PatternKind Pattern { get; set; } = PatternKind.Straight;
    public BondRatio Bond { get; set; } = BondRatio.Half;
    public bool LongSideAlongBaseline { get; set; } = true;

    public Point2 BaselineOrigin { get; set; } = new(0, 0);
    public double BaselineAngleDeg { get; set; }

    public OffsetMode OffsetMode { get; set; } = OffsetMode.Auto;
    public double OffsetU { get; set; }
    public double OffsetV { get; set; }

    public bool ReuseOffcuts { get; set; } = true;
    public bool AllowQuarterTurns { get; set; }
    public double KerfMm { get; set; }

    /// <summary>覆寫專案預設的損耗率；null 表示依鋪法使用預設值。</summary>
    public decimal? WasteRateOverride { get; set; }

    public Pattern ToPattern() => new(Pattern, Bond, LongSideAlongBaseline);

    public Baseline ToBaseline() => new(BaselineOrigin.ToVec(), BaselineAngleDeg);
}
