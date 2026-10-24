using TilePlan.Core.Projects;

namespace TilePlan.Web.Services;

/// <summary>內嵌在組件中的範例專案（來源：repo 的 samples/*.json）。</summary>
public static class SampleProject
{
    /// <summary>浴室、廚房、L 形客廳。</summary>
    public const string Renovation = "sample-project";

    /// <summary>驗收 11 的效能量測：400 × 300 房間、10 × 10 小磚。</summary>
    public const string Performance = "perf-400x300";

    public static Project Load(string name = Renovation)
    {
        using var stream = typeof(SampleProject).Assembly.GetManifestResourceStream($"TilePlan.Web.{name}.json")
            ?? throw new InvalidOperationException($"找不到內嵌的範例專案 {name}.json。");
        using var reader = new StreamReader(stream);
        return ProjectSerializer.Deserialize(reader.ReadToEnd());
    }
}
