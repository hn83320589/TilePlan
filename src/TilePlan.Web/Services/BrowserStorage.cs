using Microsoft.JSInterop;

namespace TilePlan.Web.Services;

/// <summary>瀏覽器端的鍵值儲存（localStorage）；測試時替換成記憶體實作。</summary>
public interface IBrowserStorage
{
    ValueTask<string?> GetAsync(string key);
    ValueTask SetAsync(string key, string value);
    ValueTask RemoveAsync(string key);
}

public sealed class LocalStorage(TilePlanJs js) : IBrowserStorage
{
    public async ValueTask<string?> GetAsync(string key) => await (await js.Module()).InvokeAsync<string?>("storageGet", key);
    public async ValueTask SetAsync(string key, string value) => await (await js.Module()).InvokeVoidAsync("storageSet", key, value);
    public async ValueTask RemoveAsync(string key) => await (await js.Module()).InvokeVoidAsync("storageRemove", key);
}

/// <summary>延遲載入 wwwroot/js/tileplan.js 模組，全程共用一份。</summary>
public sealed class TilePlanJs(IJSRuntime js) : IAsyncDisposable
{
    private Task<IJSObjectReference>? _module;

    public Task<IJSObjectReference> Module() => _module ??= js.InvokeAsync<IJSObjectReference>("import", "./js/tileplan.js").AsTask();

    public async ValueTask DisposeAsync()
    {
        if (_module is not null) await (await _module).DisposeAsync();
    }
}
