using TilePlan.Core.Projects;
using TilePlan.Web.Services;

namespace TilePlan.Web.Tests;

public class AutoSaveTests
{
    /// <summary>模擬開啟頁面：建立新的 store 與自動儲存服務，接上同一個瀏覽器儲存。</summary>
    private static (ProjectStore Store, AutoSaveService AutoSave) Boot(MemoryStorage storage)
    {
        var store = new ProjectStore();
        return (store, new AutoSaveService(store, storage) { Delay = TimeSpan.FromMilliseconds(10) });
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
    public async Task Changes_are_saved_after_the_debounce_delay()
    {
        var storage = new MemoryStorage();
        var (store, autoSave) = Boot(storage);
        autoSave.Start();
        var saved = new TaskCompletionSource();
        autoSave.Saved += () => saved.TrySetResult();

        store.Replace(new Project { Name = "林小姐套房" });

        await saved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("林小姐套房", storage.Items[AutoSaveService.StorageKey]);
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
