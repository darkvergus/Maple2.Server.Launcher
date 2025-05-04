namespace Maple2.Server.Launcher.Utils;

public static class DbUtils
{
    public static Task<bool> TestAsync(string host,string port,string user,string pass,IProgress<string> log) => ShellUtils.TestMySqlConnectionAsync(host,port,user,pass,log);
}