using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using TilePlan.Web;
using TilePlan.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

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
