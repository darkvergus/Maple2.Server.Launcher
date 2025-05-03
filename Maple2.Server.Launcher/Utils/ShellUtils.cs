using System.Diagnostics;
using System.IO;
using System.Net.Http;
using MySqlConnector;

namespace Maple2.Server.Launcher.Utils;

public static class ShellUtils
{
    /// <summary>
    /// Runs a shell process (git/dotnet/etc.) with redirected IO and pumps its output into the provided TextBox.
    /// </summary>
    public static async Task RunProcessAsync(string filename, string args, string? workDir, TextBox outputBox)
    {
        ProcessStartInfo processStartInfo = new(filename, args)
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        Process process = new()
        {
            StartInfo = processStartInfo, 
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, dataReceivedEventArgs) =>
        {
            if (!string.IsNullOrEmpty(dataReceivedEventArgs.Data))
            {
                AppendLine(outputBox, dataReceivedEventArgs.Data);
            }
        };
        
        process.ErrorDataReceived += (_, dataReceivedEventArgs) =>
        {
            if (!string.IsNullOrEmpty(dataReceivedEventArgs.Data))
            {
                AppendLine(outputBox, dataReceivedEventArgs.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();
    }
    
    /// <summary>
    /// Logs a line in the form:
    ///  HH:mm:ss.fff  Component   [LVL] <TID> Message
    /// </summary>
    public static void LogFormatted(TextBox box, string component, string level, string message, int threadId = 1)
    {
        string ts = DateTime.Now.ToString("HH:mm:ss.fff");

        string comp = component.PadRight(12);

        string lvl = level.ToUpper().PadRight(3)[..3];

        box.AppendText($"{ts}  {comp} [{lvl}] <{threadId}> {message}{Environment.NewLine}");
        box.ScrollToEnd();
    }
    
    private static void AppendLine(TextBox box, string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        box.Dispatcher.Invoke(() =>
        {
            box.AppendText(text + Environment.NewLine);
            box.ScrollToEnd();
        });
    }
    
    /// <summary>
    /// Ensures there is a .env file (creates from .env.example if needed), returns its path.
    /// </summary>
    public static string GetEnvPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string envPath = Path.Combine(path, ".env");
        string examplePath = Path.Combine(path, ".env.example");

        if (!File.Exists(envPath) && File.Exists(examplePath))
        {
            File.Copy(examplePath, envPath);
        }
        
        return !File.Exists(envPath) ? throw new FileNotFoundException("Neither .env nor .env.example found.", envPath) : envPath;
    }


    /// <summary>
    /// Updates (or appends) a key=value pair in an .env file.
    /// </summary>
    public static void UpdateEnvKey(string envPath, string key, string value)
    {
        if (!File.Exists(envPath))
        {
            return;
        }

        List<string> lines = File.ReadAllLines(envPath).ToList();
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

        File.WriteAllLines(envPath, lines);
    }

    /// <summary>
    /// Parses a simple .env file for given keys.
    /// </summary>
    public static void LoadEnvKeys(string envPath, Dictionary<string, Action<string>> setters)
    {
        if (!File.Exists(envPath))
        {
            return;
        }

        foreach (string line in File.ReadAllLines(envPath))
        {
            foreach (KeyValuePair<string, Action<string>> kv in setters.Where(kv => line.StartsWith(kv.Key + "=")))
            {
                kv.Value(line.Substring(kv.Key.Length + 1));
                break;
            }
        }
    }
    
    /// <summary>
    /// Download multiple files in parallel and log each save.
    /// </summary>
    public static async Task DownloadFilesAsync(string dir, string[] files, TextBox outputBox)
    {
        Directory.CreateDirectory(dir);
        using HttpClient http = new();
        IEnumerable<Task> tasks = files.Select(async file =>
        {
            string url  = $"https://github.com/Zintixx/MapleStory2-XML/releases/latest/download/{file}";
            byte[] data = await http.GetByteArrayAsync(url);
            string dest = Path.Combine(dir, file);
            await File.WriteAllBytesAsync(dest, data);
            AppendLine(outputBox, $"→ {file} saved to {dest}");
        });
        await Task.WhenAll(tasks);
    }
    
    /// <summary>
    /// Tests a MySQL connection using a client library.
    /// </summary>
    public static async Task<bool> TestMySqlConnectionAsync(string host, string port, string user, string password, TextBox outputBox)
    {
        string connStr = new MySqlConnectionStringBuilder
        {
            Server   = host,
            Port     = uint.Parse(port),
            UserID   = user,
            Password = password,
            Database = ""
        }.ConnectionString;

        try
        {
            await using MySqlConnection conn = new(connStr);
            await conn.OpenAsync();
            AppendLine(outputBox, "✔ MySQL library connection succeeded.");
            return true;
        }
        catch (Exception ex)
        {
            AppendLine(outputBox, $"[ERROR] MySQL library connection failed: {ex.Message}");
            return false;
        }
    }

}