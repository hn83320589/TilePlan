using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Playwright;

namespace TilePlan.E2E.Tests;

/// <summary>
/// 以 ASP.NET Core 靜態伺服器載入 <c>dotnet publish</c> 的發佈版（與 GitHub Pages 相同：找不到的路徑回 index.html），
/// 再用 Playwright 的 Chromium 開啟。發佈目錄可用環境變數 TILEPLAN_SITE 指定（CI 先發佈好），否則自動發佈一次。
/// </summary>
public sealed class SiteFixture : IAsyncLifetime
{
    private WebApplication? _app;
    private IPlaywright? _playwright;

    public string BaseUrl { get; private set; } = "";
    public IBrowser Browser { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        string site = Environment.GetEnvironmentVariable("TILEPLAN_SITE") ?? await PublishAsync();
        if (!File.Exists(Path.Combine(site, "index.html")))
            throw new InvalidOperationException($"找不到發佈版的 index.html：{site}");

        _app = BuildHost(site);
        await _app.StartAsync();
        BaseUrl = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First().TrimEnd('/') + "/";

        // 已安裝時很快就會結束；CI 另以 playwright.ps1 install --with-deps 安裝系統相依套件
        int exit = Microsoft.Playwright.Program.Main(["install", "chromium"]);
        if (exit != 0) throw new InvalidOperationException($"安裝 Chromium 失敗（exit code {exit}）。");
        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
    }

    public async Task<IPage> NewPageAsync()
    {
        // 每個測試用全新的瀏覽器環境：localStorage 是空的，第一次開啟會載入範例專案
        var context = await Browser.NewContextAsync(new() { ViewportSize = new() { Width = 1440, Height = 900 } });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(60_000);
        return page;
    }

    private static WebApplication BuildHost(string site)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        var files = new PhysicalFileProvider(site);
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = files,
            ContentTypeProvider = new FileExtensionContentTypeProvider(),
            ServeUnknownFileTypes = true,
        });
        app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = files });
        return app;
    }

    private static async Task<string> PublishAsync()
    {
        string root = RepoRoot();
        string output = Path.Combine(Path.GetTempPath(), "tileplan-e2e-site");
        var psi = new ProcessStartInfo("dotnet", ["publish", Path.Combine(root, "src", "TilePlan.Web"), "-c", "Release", "-o", output])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(psi)!;
        string log = await process.StandardOutput.ReadToEndAsync() + await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException($"dotnet publish 失敗：\n{log}");
        return Path.Combine(output, "wwwroot");
    }

    public static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "TilePlan.slnx"))) return dir.FullName;
        throw new InvalidOperationException("找不到 TilePlan.slnx，無法定位專案根目錄。");
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null) await Browser.CloseAsync();
        _playwright?.Dispose();
        if (_app is not null) await _app.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class SiteCollection : ICollectionFixture<SiteFixture>
{
    public const string Name = "TilePlan 發佈版";
}
