using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace PgPlanCheck.Config;

/// <summary>設定ファイル（JSON）。当面は使用するルールの有効/無効を指定する。</summary>
public sealed class PgPlanCheckConfig
{
    [JsonPropertyName("rules")] public RulesConfig Rules { get; set; } = new();
}

/// <summary>ルールの選択。<see cref="Enabled"/> 指定時はそれらのみ実行（ホワイトリスト）。
/// 未指定なら全ルールから <see cref="Disabled"/> を除外（ブラックリスト）。</summary>
public sealed class RulesConfig
{
    [JsonPropertyName("enabled")] public List<string> Enabled { get; set; } = new();
    [JsonPropertyName("disabled")] public List<string> Disabled { get; set; } = new();
}

/// <summary>設定ファイルの読み込み。</summary>
public static class ConfigLoader
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static async Task<PgPlanCheckConfig> LoadAsync(string path)
    {
        await using var fs = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<PgPlanCheckConfig>(fs, Opts) ?? new PgPlanCheckConfig();
    }
}
