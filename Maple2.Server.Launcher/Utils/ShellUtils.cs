using System.Diagnostics;
using System.IO;
using System.Net.Http;
using MySqlConnector;

namespace Maple2.Server.Launcher.Utils;

/// <summary>
/// Generic shell / network helpers. All progress is reported through <see cref="IProgress{String}"/> so UI‑free code can consume it.
/// .env helpers were extracted to <c>EnvUtils</c>.
/// </summary>
public static class ShellUtils
{
    /// <summary>
    /// Runs a process and streams its stdout / stderr to the provided progress sink.
    /// </summary>
    public static async Task RunProcessAsync(string file, string args, string? workDir, IProgress<string> log)
    {
        ProcessStartInfo processStartInfo = new(file, args)
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using Process proc = new() { StartInfo = processStartInfo };

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                log.Report(e.Data);
            }
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                log.Report(e.Data);
            }
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        await proc.WaitForExitAsync();
    }

    /// <summary>
    /// Download several files in parallel and report each successful save.
    /// </summary>
    public static async Task DownloadFilesAsync(string dir, IEnumerable<string> files, IProgress<string> log)
    {
        Directory.CreateDirectory(dir);
        using HttpClient http = new();
        IEnumerable<Task> tasks = files.Select(async file =>
        {
            string url = $"https://github.com/Zintixx/MapleStory2-XML/releases/latest/download/{file}";
            byte[] data = await http.GetByteArrayAsync(url);
            string dest = Path.Combine(dir, file);
            await File.WriteAllBytesAsync(dest, data);
            log.Report($"→ {file} saved to {dest}");
        });
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Tests a MySQL connection using the MySqlConnector library.
    /// </summary>
    public static async Task<bool> TestMySqlConnectionAsync(string host, string port, string user, string password, IProgress<string> log)
    {
        string connectionString = new MySqlConnectionStringBuilder
        {
            Server = host,
            Port = uint.Parse(port),
            UserID = user,
            Password = password
        }.ConnectionString;

        try
        {
            await using MySqlConnection conn = new(connectionString);
            await conn.OpenAsync();
            log.Report("✔ MySQL connection successful.");
            return true;
        }
        catch (Exception ex)
        {
            log.Report("[ERROR] MySQL connection failed: " + ex.Message);
            return false;
        }
    }
}