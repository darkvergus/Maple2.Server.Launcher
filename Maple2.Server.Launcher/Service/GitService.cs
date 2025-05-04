using System.Diagnostics;
using System.IO;
using Maple2.Server.Launcher.Utils;

namespace Maple2.Server.Launcher.Service;

public sealed class GitService : IGitService
{
    public async Task<bool> CanReachRemote(string url, int timeoutSeconds = 15)
    {
        try
        {
            using Process proc = new()
            {
                StartInfo = new()
                {
                    FileName = "git",
                    Arguments = $"ls-remote --heads {url}",
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            proc.Start();

            Task finished = proc.WaitForExitAsync();
            Task raced    = await Task.WhenAny(finished, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)));

            if (raced != finished)
            {
                try
                {
                    proc.Kill(true);
                }
                catch
                {
                     /* ignore */
                }
                return false;
            }

            return proc.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task CloneAsync(string url, string workDir, IProgress<string> log)
    {
        log.Report("→ Cloning repository (with submodules)...");
        await ShellUtils.RunProcessAsync("git", $"clone --recursive {url} .", workDir, log);
        log.Report("✔ Cloned successfully.");
    }

    public async Task PullAsync(string workDir, IProgress<string> log)
    {
        log.Report("→ Pulling latest changes...");
        await ShellUtils.RunProcessAsync("git", "pull", workDir, log);
        log.Report("✔ Up to date.");
    }

    public async Task CloneOrPullAsync(string url, string workDir, IProgress<string> log)
    {
        string gitDir = Path.Combine(workDir, ".git");
        bool isRepo = Directory.Exists(gitDir);
        bool isEmpty = !Directory.EnumerateFileSystemEntries(workDir).Any();

        switch (isRepo)
        {
            case false when isEmpty:
                await CloneAsync(url, workDir, log);
                break;
            case true:
                await PullAsync(workDir, log);
                break;
            default:
                log.Report("Directory not empty & not a git repo—skipping clone.");
                break;
        }
    }
}