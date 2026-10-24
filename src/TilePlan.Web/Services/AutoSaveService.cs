using TilePlan.Core.Projects;

namespace TilePlan.Web.Services;

/// <summary>
/// 專案一有變更就立即寫入瀏覽器儲存，啟動時還原。
/// 不使用延遲（debounce）：延遲期間重新整理或關閉分頁會遺失最後的修改（測試報告 F3）。
/// 儲存進行中又有修改時只做標記，完成後再存一次最新狀態，所以連續修改不會排出一長串寫入。
/// </summary>
public sealed class AutoSaveService(ProjectStore store, IBrowserStorage storage) : IDisposable
{
    public const string StorageKey = "tileplan.project.v1";

    private bool _dirty;
    private Task _pump = Task.CompletedTask;

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

    /// <summary>等目前排定的儲存全部完成（測試用）。</summary>
    public Task WhenIdle() => _pump;

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

    public void Dispose() => store.Changed -= OnChanged;
}
