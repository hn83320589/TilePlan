using TilePlan.Web.Services;

namespace TilePlan.Web.Tests;

/// <summary>模擬瀏覽器的 localStorage：同一個實例在「重新整理」前後共用，就像真的瀏覽器。</summary>
internal sealed class MemoryStorage : IBrowserStorage
{
    public Dictionary<string, string> Items { get; } = [];

    public ValueTask<string?> GetAsync(string key) => ValueTask.FromResult(Items.GetValueOrDefault(key));

    public ValueTask SetAsync(string key, string value)
    {
        Items[key] = value;
        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveAsync(string key)
    {
        Items.Remove(key);
        return ValueTask.CompletedTask;
    }
}
