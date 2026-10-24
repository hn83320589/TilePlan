using System.Diagnostics;
using TilePlan.Core.Projects;
using TilePlan.Core.Quoting;

namespace TilePlan.Web.Services;

/// <summary>
/// 目前開啟的專案與各房間的計算結果。結果以「房間 + 磁磚 + 單價」的序列化字串為快取鍵，
/// 修改一間房不會重算其他房間。
/// </summary>
public sealed class ProjectStore
{
    private readonly Dictionary<string, Cached> _cache = [];

    private sealed record Cached(string Key, RoomResult Result, double Milliseconds);

    public Project Project { get; private set; } = new();

    /// <summary>專案內容被修改（需要儲存、需要重繪）。</summary>
    public event Action? Changed;

    public void Replace(Project project)
    {
        Project = project;
        _cache.Clear();
        NotifyChanged();
    }

    public void NotifyChanged() => Changed?.Invoke();

    public Room? FindRoom(string id) => Project.Rooms.FirstOrDefault(r => r.Id == id);

    public RoomResult Result(Room room)
    {
        string key = ProjectSerializer.RoomKey(Project, room);
        if (_cache.TryGetValue(room.Id, out var cached) && cached.Key == key) return cached.Result;

        var sw = Stopwatch.StartNew();
        var result = ProjectCalculator.ComputeRoom(Project, room);
        _cache[room.Id] = new Cached(key, result, sw.Elapsed.TotalMilliseconds);
        return result;
    }

    /// <summary>最近一次重算這間房的耗時（ms）；用快取時不更新。</summary>
    public double LastComputeMilliseconds(Room room) => _cache.TryGetValue(room.Id, out var c) ? c.Milliseconds : 0;

    public Quote Quote() => ProjectCalculator.BuildQuote(Project, Project.Rooms.Select(Result).ToList());
}
