using TilePlan.Core.Projects;
using TilePlan.Web.Services;

namespace TilePlan.Web.Tests;

public class AutoSaveTests
{
    /// <summary>模擬開啟頁面：建立新的 store 與自動儲存服務，接上同一個瀏覽器儲存。</summary>
    private static (ProjectStore Store, AutoSaveService AutoSave) Boot(IBrowserStorage storage)
    {
        var store = new ProjectStore();
        return (store, new AutoSaveService(store, storage));
    }

    [Fact]
    public async Task Project_survives_a_page_reload()
    {
        var storage = new MemoryStorage();
        var (store, autoSave) = Boot(storage);
        autoSave.Start();
        store.Replace(SampleProject.Load());
        store.Project.Rooms[0].Name = "主臥浴室";
        store.NotifyChanged();
        await autoSave.SaveNowAsync();
        var before = ProjectSerializer.Serialize(store.Project);

        // 重新整理：新的 store 與服務，同一個瀏覽器儲存
        var (reloaded, restore) = Boot(storage);
        Assert.True(await restore.RestoreAsync());

        Assert.Equal("主臥浴室", reloaded.Project.Rooms[0].Name);
        Assert.Equal(before, ProjectSerializer.Serialize(reloaded.Project));
    }

    [Fact]
    public void Change_is_saved_immediately_without_a_debounce_window()
    {
        // 測試報告 F3：原本延遲 0.4 秒才寫入，這段時間內重新整理會遺失修改
        var storage = new MemoryStorage();
        var (store, autoSave) = Boot(storage);
        autoSave.Start();

        store.Replace(new Project { Name = "林小姐套房" });

        // MemoryStorage 是同步完成的：事件處理結束時就已經寫入，不需要等待任何計時器
        Assert.Contains("林小姐套房", storage.Items[AutoSaveService.StorageKey]);
    }

    [Fact]
    public async Task Changes_during_a_slow_save_are_coalesced_into_one_more_save()
    {
        var storage = new GatedStorage();
        var (store, autoSave) = Boot(storage);
        autoSave.Start();

        store.Project.Name = "第 1 版";
        store.NotifyChanged();                 // 開始儲存，卡在 storage 裡
        for (int i = 2; i <= 10; i++)
        {
            store.Project.Name = $"第 {i} 版";
            store.NotifyChanged();             // 儲存進行中：只做標記
        }
        storage.Release();
        await autoSave.WhenIdle().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, storage.Writes.Count);
        Assert.Contains("第 1 版", storage.Writes[0]);
        Assert.Contains("第 10 版", storage.Writes[1]);
    }

    /// <summary>第一次寫入會卡住，直到 Release()；用來模擬很慢的瀏覽器儲存。</summary>
    private sealed class GatedStorage : IBrowserStorage
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Writes { get; } = [];

        public void Release() => _gate.TrySetResult();

        public ValueTask<string?> GetAsync(string key) => ValueTask.FromResult<string?>(null);

        public async ValueTask SetAsync(string key, string value)
        {
            Writes.Add(value);
            if (Writes.Count == 1) await _gate.Task;
        }

        public ValueTask RemoveAsync(string key) => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Reloaded_project_computes_identical_quote()
    {
        var storage = new MemoryStorage();
        var (store, autoSave) = Boot(storage);
        store.Replace(SampleProject.Load());
        await autoSave.SaveNowAsync();
        var quoteBefore = store.Quote();

        var (reloaded, restore) = Boot(storage);
        await restore.RestoreAsync();
        var quoteAfter = reloaded.Quote();

        Assert.Equal(quoteBefore.GrandTotal, quoteAfter.GrandTotal);
        Assert.Equal(quoteBefore.Tiles.Select(t => (t.TileName, t.Boxes)), quoteAfter.Tiles.Select(t => (t.TileName, t.Boxes)));
    }

    [Fact]
    public async Task Corrupted_storage_is_reported_instead_of_crashing()
    {
        var storage = new MemoryStorage();
        storage.Items[AutoSaveService.StorageKey] = "{ 壞掉的 json";
        var (_, autoSave) = Boot(storage);

        Assert.False(await autoSave.RestoreAsync());
        Assert.Contains("無法讀取", autoSave.LastError);
    }

    [Fact]
    public async Task Empty_storage_means_first_visit()
    {
        var (_, autoSave) = Boot(new MemoryStorage());

        Assert.False(await autoSave.RestoreAsync());
        Assert.Null(autoSave.LastError);
    }
}
