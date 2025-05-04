using System.IO;
using Maple2.Server.Launcher.Utils;

namespace Maple2.Server.Launcher.Service;

public sealed class SetupRunner(IGitService git)
{
    private readonly IGitService git = git;

    public async Task RunAsync(string installRoot, IProgress<string> log)
    {
        await ShellUtils.RunProcessAsync("git", "submodule update --init --recursive", installRoot, log);
        
        await ShellUtils.RunProcessAsync("dotnet", "tool install --global dotnet-ef", installRoot, log);
        
        string envPath = EnvUtils.EnsureEnv(installRoot);
        IDictionary<string, string> env = EnvUtils.Read(envPath);
        
        if (!env.TryGetValue("MS2_DATA_FOLDER", out string dataDir) || string.IsNullOrWhiteSpace(dataDir) || !Directory.Exists(dataDir))
        {
            throw new InvalidOperationException("MS2_DATA_FOLDER missing or invalid in .env");
        }

        await ShellUtils.DownloadFilesAsync(dataDir, ["Server.m2d","Server.m2h","Xml.m2d","Xml.m2h"], log);
        
        await ShellUtils.RunProcessAsync("dotnet", "run", Path.Combine(installRoot, "Maple2.File.Ingest"), log);
    }
}