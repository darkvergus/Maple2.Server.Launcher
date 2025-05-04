using System.IO;
using System.Text.Json;

namespace Maple2.Server.Launcher.Config;

public sealed class LauncherConfigStore(string projectRoot)
{
    private readonly string path = Path.Combine(projectRoot, "launcher.config.json");

    public LauncherConfig LoadOrDefault()
    {
        if (!File.Exists(path))
        {
            return new()
            {
                RepoRoot   = "https://github.com/AngeloTadeucci/Maple2",
                InstallRoot = Path.Combine(Directory.GetCurrentDirectory(), "Maple2")
            };
        }

        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<LauncherConfig>(json)!;
    }

    public void Save(LauncherConfig cfg)
    {
        JsonSerializerOptions opt = new() { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(cfg, opt));
    }
}