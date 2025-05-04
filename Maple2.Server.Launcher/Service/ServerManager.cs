using System.Diagnostics;
using System.IO;
using Maple2.Server.Launcher.Utils;

namespace Maple2.Server.Launcher.Service;

public sealed class ServerManager(TabControl host, Func<string, (TabItem, IProgress<string>)> logFactory)
{
    private readonly Func<string, (TabItem view, IProgress<string> sink)> factory = logFactory;
    private readonly Dictionary<string, Process> processes = new();

    public async Task LaunchAsync(string name, string relProjectPath, string installRoot)
    {
        (TabItem tab, IProgress<string> log) = factory(name);
        host.Items.Add(tab);

        await ShellUtils.RunProcessAsync("dotnet", $"build {relProjectPath}", installRoot, log);

        string exe = Path.Combine(installRoot, relProjectPath, "bin", "Debug", "net8.0", $"{relProjectPath}.exe");
        if (!File.Exists(exe))
        {
            log.Report($"[ERR] {exe} not found");
            return;
        }

        ProcessStartInfo processStartInfo = new()
        {
            FileName = exe,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        Process process = new()
        {
            StartInfo = processStartInfo, EnableRaisingEvents = true
        };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                log.Report(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                log.Report("[ERR] " + e.Data);
            }
        };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        
        processes[name] = process;
        
        log.Report($"✔ {name} started (PID {process.Id})");
    }

    public void KillAll()
    {
        foreach (Process process in processes.Values.Where(process => !process.HasExited))
        {
            process.Kill(true);
        }
    }
}