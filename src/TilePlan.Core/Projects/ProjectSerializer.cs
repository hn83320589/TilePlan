using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TilePlan.Core.Projects;

public sealed class ProjectFormatException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// 使用 source generator：Blazor WebAssembly 發佈時會 trim，反射式序列化可能失效。
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(Project))]
internal sealed partial class ProjectJsonContext : JsonSerializerContext;

/// <summary>
/// 專案 JSON 匯出／匯入。System.Text.Json 以最短可往返格式輸出 double，
/// 所以匯出再匯入的數值與原本完全相同，計算結果也就完全相同。
/// </summary>
public static class ProjectSerializer
{
    private static readonly JsonSerializerOptions Options = new(ProjectJsonContext.Default.Options)
    {
        // 中文直接輸出，不轉成 \uXXXX，專案檔才看得懂
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        TypeInfoResolver = ProjectJsonContext.Default,
    };

    public static string Serialize(Project project) => JsonSerializer.Serialize(project, Options.GetTypeInfo(typeof(Project)));

    public static Project Deserialize(string json)
    {
        Project? project;
        try
        {
            project = (Project?)JsonSerializer.Deserialize(json, Options.GetTypeInfo(typeof(Project)));
        }
        catch (JsonException ex)
        {
            throw new ProjectFormatException($"不是有效的 TilePlan 專案檔：{ex.Message}", ex);
        }
        if (project is null) throw new ProjectFormatException("專案檔是空的。");
        if (project.SchemaVersion > Project.CurrentSchemaVersion)
            throw new ProjectFormatException($"專案檔版本 {project.SchemaVersion} 比這個程式支援的版本 {Project.CurrentSchemaVersion} 新，請更新程式。");
        if (project.SchemaVersion < 1)
            throw new ProjectFormatException("專案檔缺少版本資訊（schemaVersion）。");
        return project;
    }

    public static Project Clone(Project project) => Deserialize(Serialize(project));
}
