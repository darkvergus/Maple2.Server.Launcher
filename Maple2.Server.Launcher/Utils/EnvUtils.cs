using System.IO;

namespace Maple2.Server.Launcher.Utils;

public class EnvUtils
{
    /// Ensures <c>.env</c> exists (copies from <c>.env.example</c> if needed) and returns the full path.
    public static string EnsureEnv(string repoRoot)
    {
        ArgumentNullException.ThrowIfNull(repoRoot);
        string env = Path.Combine(repoRoot, ".env");
        string example = Path.Combine(repoRoot, ".env.example");

        if (!File.Exists(env) && File.Exists(example))
        {
            File.Copy(example, env);
        }

        if (!File.Exists(env))
        {
            throw new FileNotFoundException("Neither .env nor .env.example were found.", env);
        }

        return env;
    }

    public static IDictionary<string,string> Read(string envPath)
        => File.Exists(envPath) ? File.ReadAllLines(envPath).Where(line => line.Contains('='))
                .Select(line => line.Split('=',2)).ToDictionary(strings => strings[0], a => a[1]) : new();

    public static void Insert(string envPath, string key, string value)
    {
        List<string> lines = File.Exists(envPath) ? File.ReadAllLines(envPath).ToList() : [];

        string prefix = key + "=";
        int idx = lines.FindIndex(l => l.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
        {
            lines[idx] = prefix + value;
        }
        else
        {
            lines.Add(prefix + value);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(envPath)!);
        File.WriteAllLines(envPath, lines);
    }
}