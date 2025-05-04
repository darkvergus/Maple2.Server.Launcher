using System.IO;
using Maple2.Server.Launcher.Config;
using Maple2.Server.Launcher.Service;
using Maple2.Server.Launcher.UI;
using Maple2.Server.Launcher.Utils;
using Microsoft.Win32;

namespace Maple2.Server.Launcher.Windows;

public partial class MainWindow
{
    private readonly LauncherConfigStore cfgStore;
    private readonly IGitService git;
    private readonly SetupRunner setup;
    private readonly ServerManager servers;

    private readonly LauncherConfig cfg;

    public MainWindow()
    {
        InitializeComponent();

        string projectRoot = Directory.GetCurrentDirectory();

        cfgStore = new(projectRoot);
        git = new GitService();
        setup = new(git);
        servers = new(ServerOutputTabs, LogViewFactory.Create);

        cfg = cfgStore.LoadOrDefault();
        if (cfg.RepoRoot != null)
        {
            TxtRepoUrl.Text = cfg.RepoRoot;
        }

        BtnCloneUpdate.Content = Directory.Exists(Path.Combine(projectRoot, ".git")) ? "Update" : "Clone";

        LoadEnvIntoUi();
    }
    
    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void BtnMinimize_Click(object _, RoutedEventArgs __) => WindowState = WindowState.Minimized;

    private async void BtnCloneUpdate_Click(object s, RoutedEventArgs e)
    {
        TextBlockProgress log = new(TxtProjectLog);
        TxtProjectLog.Clear();

        if (!await git.CanReachRemote(TxtRepoUrl.Text))
        {
            log.Report("[ERROR] Cannot reach repository");
            return;
        }
        
        Directory.CreateDirectory(cfg.InstallRoot);
        await git.CloneOrPullAsync(TxtRepoUrl.Text, cfg.InstallRoot, log);

        cfg.RepoRoot = TxtRepoUrl.Text;
        cfgStore.Save(cfg);
        LoadEnvIntoUi();
    }
    
    private async void BtnSetup_Click(object s, RoutedEventArgs e)
    {
        TextBlockProgress log = new(TxtSetupLog);
        TxtSetupLog.Clear();
        ProgressSetup.Visibility = Visibility.Visible;
        try
        {
            if (cfg.InstallRoot != null)
            {
                await setup.RunAsync(cfg.InstallRoot, log);
            }

            log.Report("✔ Setup complete");
        }
        catch (Exception ex)
        {
            log.Report("[ERROR] " + ex.Message);
        }
        finally
        {
            ProgressSetup.Visibility = Visibility.Collapsed;
        }
    }
    
    private async void BtnSaveDbConfig_Click(object _, RoutedEventArgs __)
    {
        TextBlockProgress log = new(TxtDatabaseLog);
        TxtDatabaseLog.Clear();
        if (!await DbUtils.TestAsync(TxtDbHost.Text, TxtDbPort.Text, TxtDbUser.Text, TxtDbPassword.Password, log))
        {
            return;
        }

        if (cfg.InstallRoot != null)
        {
            string env = EnvUtils.EnsureEnv(cfg.InstallRoot);
            EnvUtils.Insert(env, "DB_IP", TxtDbHost.Text);
            EnvUtils.Insert(env, "DB_PORT", TxtDbPort.Text);
            EnvUtils.Insert(env, "DB_USER", TxtDbUser.Text);
            EnvUtils.Insert(env, "DB_PASSWORD", TxtDbPassword.Password);
        }

        log.Report("✔ Saved");
    }
    
    private void LoadEnvIntoUi()
    {
        try
        {
            if (cfg.InstallRoot != null)
            {
                string env = EnvUtils.EnsureEnv(cfg.InstallRoot);
                IDictionary<string, string> kv = EnvUtils.Read(env);
            
                TxtDbHost.Text = kv.GetValueOrDefault("DB_IP", "localhost");
                TxtDbPort.Text = kv.GetValueOrDefault("DB_PORT", "3306");
                TxtDbUser.Text = kv.GetValueOrDefault("DB_USER", "root");
                TxtDbPassword.Password = kv.GetValueOrDefault("DB_PASSWORD", "");
                TxtClientPath.Text = kv.GetValueOrDefault("MS2_DATA_FOLDER", "<not set>");
            }
        }
        catch
        {
            /* first‑run: fine to ignore */
        }
    }
    
    private void ServerOutputTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ServerOutputTabs.SelectedItem is TabItem tabItem && tabItem.Content is DockPanel dock)
        {
            TextBox? textbox = dock.Children.OfType<TextBox>().FirstOrDefault();
            textbox?.ScrollToEnd();
        }
    }
    
    private async void BtnBuildRun_Click(object s, RoutedEventArgs e)
    {
        var servers = new[]
        {
            new { Name = "World", Project = "Maple2.Server.World" },
            new { Name = "Login", Project = "Maple2.Server.Login" },
            new { Name = "Web", Project = "Maple2.Server.Web" },
            new { Name = "Game", Project = "Maple2.Server.Game" }
        };
        foreach (var sv in servers)
        {
            if (cfg.InstallRoot != null)
            {
                await this.servers.LaunchAsync(sv.Name, sv.Project, cfg.InstallRoot);
            }
        }
    }

    private void BtnClose_Click(object _, RoutedEventArgs __)
    {
        servers.KillAll();
        Close();
    }

    private void DatabaseTab_Loaded(object sender, RoutedEventArgs e) => LoadEnvIntoUi();

    private void BtnSelectClient_Click(object sender, RoutedEventArgs e)
    {
        TextBlockProgress log = new(TxtSetupLog);
        OpenFileDialog openFileDialog = new()
        {
            Filter = "MapleStory2.exe|MapleStory2.exe",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        };
        if (openFileDialog.ShowDialog() != true)
        {
            return;
        }

        TxtClientPath.Text = openFileDialog.FileName;
        string dataDir = Path.Combine(Path.GetDirectoryName(openFileDialog.FileName)!, "Data");
        if (!Directory.Exists(dataDir))
        {
            log.Report("[WARN] Data dir not found; MS2_DATA_FOLDER not updated");
            return;
        }

        if (cfg.InstallRoot != null)
        {
            string env = EnvUtils.EnsureEnv(cfg.InstallRoot);
            EnvUtils.Insert(env, "MS2_DATA_FOLDER", dataDir);
        }

        log.Report($"✔ MS2_DATA_FOLDER set to {dataDir}");
    }
}