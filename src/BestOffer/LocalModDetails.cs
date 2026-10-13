using System.Text.Json;
using MelonLoader;
using MelonLoader.Utils;

namespace DeepSpaceModSupport;

internal static class LocalModDetails
{
    internal static void Register(string builtInName, string nameZh, string nameEn, string summaryZh, string summaryEn)
    {
        try
        {
            string path = Path.Combine(MelonEnvironment.UserDataDirectory, "ModManager", "mod-details.json");
            var cache = File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(path)) ?? new()
                : new Dictionary<string, JsonElement>();
            cache[builtInName.Trim().ToUpperInvariant()] = JsonSerializer.SerializeToElement(new
            {
                BuiltInName = builtInName, NameZh = nameZh, NameEn = nameEn,
                SummaryZh = summaryZh, SummaryEn = summaryEn, Link = "", LocalizedSummary = summaryZh
            });
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, true);
        }
        catch (Exception e) { MelonLogger.Warning("本地 Mod 说明缓存未写入：" + e.Message); }
    }
}
