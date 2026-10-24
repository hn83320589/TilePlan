using TilePlan.Core.Projects;

namespace TilePlan.Web.Services;

/// <summary>專案一有變更就延遲寫入瀏覽器儲存（連續修改只寫最後一次）；啟動時還原。</summary>
public sealed class AutoSaveService(ProjectStore store, IBrowserStorage storage) : IDisposable
{
    public const string StorageKey = "tileplan.project.v1";

    private CancellationTokenSource? _pending;

    public TimeSpan Delay { get; init; } = TimeSpan.FromMilliseconds(400);

    /// <summary>最近一次儲存或還原失敗的原因；成功時為 null。</summary>
    public string? LastError { get; private set; }

    public DateTimeOffset? LastSaved { get; private set; }

    public event Action? Saved;

    /// <summary>從瀏覽器儲存還原專案；沒有存檔或存檔損壞時回傳 false（損壞原因放在 LastError）。</summary>
    public async Task<bool> RestoreAsync()
    {
        var json = await storage.GetAsync(StorageKey);
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            store.Replace(ProjectSerializer.Deserialize(json));
            LastError = null;
            return true;
        }
        catch (ProjectFormatException ex)
        {
            LastError = $"瀏覽器中的自動存檔無法讀取，已改為開啟範例專案：{ex.Message}";
            return false;
        }
    }

    public void Start() => store.Changed += OnChanged;

    private void OnChanged()
    {
        _pending?.Cancel();
        var cts = _pending = new CancellationTokenSource();
        _ = SaveLaterAsync(cts.Token);
    }

    private async Task SaveLaterAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(Delay, token);
            await SaveNowAsync();
        }
        catch (TaskCanceledException)
        {
            // 被下一次修改取代，不需要儲存
        }
    }

    public async Task SaveNowAsync()
    {
        try
        {
            await storage.SetAsync(StorageKey, ProjectSerializer.Serialize(store.Project));
            LastSaved = DateTimeOffset.Now;
            LastError = null;
        }
        catch (Exception ex) when (ex is Microsoft.JSInterop.JSException or InvalidOperationException)
        {
            LastError = $"自動儲存失敗（瀏覽器儲存空間可能已滿或被停用）：{ex.Message}";
        }
        Saved?.Invoke();
    }

    public void Dispose()
    {
        store.Changed -= OnChanged;
        _pending?.Cancel();
    }
}
