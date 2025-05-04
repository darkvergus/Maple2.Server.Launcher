namespace Maple2.Server.Launcher.Service;

public interface IGitService
{
    Task<bool> CanReachRemote(string url, int timeoutSeconds = 15);
    Task CloneAsync(string url, string workDir, IProgress<string> log);
    Task PullAsync(string workDir, IProgress<string> log);
    Task CloneOrPullAsync(string url, string workDir, IProgress<string> log);
}