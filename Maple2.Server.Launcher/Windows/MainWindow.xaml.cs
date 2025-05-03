using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using Maple2.Server.Launcher.Config;
using Maple2.Server.Launcher.Utils;
using Microsoft.Win32;

namespace Maple2.Server.Launcher.Windows;

public partial class MainWindow
{
    private readonly string projectRoot;
    private string? clientExePath;

    private readonly Dictionary<string, Process> serverProcesses = new();

    private const string ConfigFileName = "launcher.config.json";
    private string? repoRoot;
    private string? installRoot;
    private LauncherConfig? config;
    
    private string ConfigPath => Path.Combine(projectRoot, ConfigFileName);
    
    public MainWindow()
    {
        InitializeComponent();
        projectRoot = Directory.GetCurrentDirectory();
        LoadLauncherConfig();
        LoadEnvConfig();

        BtnCloneUpdate.Content = Directory.Exists(Path.Combine(projectRoot, ".git")) ? "Update" : "Clone";
    }

    private void LoadEnvConfig()
    {
        if (installRoot != null)
        {
            string envPath = ShellUtils.GetEnvPath(installRoot);
            if (!File.Exists(envPath))
            {
                return;
            }

            ShellUtils.LoadEnvKeys(envPath, new()
            {
                ["DB_IP"] = ip => TxtDbHost.Text = ip,
                ["DB_PORT"] = port => TxtDbPort.Text = port,
                ["DB_USER"] = user => TxtDbUser.Text = user,
                ["DB_PASSWORD"] = password => TxtDbPassword.Password = password,
                ["MS2_DATA_FOLDER"] = path =>
                {
                    TxtClientPath.Text = path;
                    clientExePath = Path.Combine(Path.GetDirectoryName(path)!, "MapleStory2.exe");
                }
            });
        }
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void BtnMinimize_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void BtnClose_Click(object s, RoutedEventArgs e)
    {
        foreach (Process process in serverProcesses.Select(servers => servers.Value).Where(process => !process.HasExited))
        {
            process.Kill(true);
        }

        Close();
    }

    private void BtnSelectClient_Click(object sender, RoutedEventArgs e)
    {
        TxtSetupLog.Clear();

        OpenFileDialog openFileDialog = new()
        {
            Filter = "MapleStory2.exe|MapleStory2.exe",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        };
        if (openFileDialog.ShowDialog() != true)
        {
            return;
        }

        clientExePath = openFileDialog.FileName;
        TxtClientPath.Text = clientExePath;

        string dataDir = Path.Combine(Path.GetDirectoryName(clientExePath)!, "Data");

        if (!string.IsNullOrEmpty(dataDir) && Path.IsPathRooted(dataDir) && Directory.Exists(dataDir))
        {
            TxtClientPath.Background = Brushes.Transparent;
        }

        if (installRoot != null)
        {
            string envFile = ShellUtils.GetEnvPath(installRoot);
            ShellUtils.UpdateEnvKey(envFile, "MS2_DATA_FOLDER", dataDir);
        }
    }

    private async void BtnSetup_Click(object sender, RoutedEventArgs e)
    {
        TxtSetupLog.Clear();
        ProgressSetup.Visibility = Visibility.Visible;

        try
        {
            AppendLog("→ Checking Git submodules...", TxtSetupLog);
            ProcessStartInfo processStartInfo = new("git", "submodule status --recursive")
            {
                WorkingDirectory = projectRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process process = Process.Start(processStartInfo)!;
            string[] statusOut = (await process.StandardOutput.ReadToEndAsync()).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            await process.WaitForExitAsync();

            foreach (string line in statusOut)
            {
                AppendLog("   " + line, TxtSetupLog);
            }

            if (statusOut.Any(line => line.StartsWith("-")))
            {
                AppendLog("→ Initializing missing submodules...", TxtSetupLog);
                await ShellUtils.RunProcessAsync("git", "submodule update --init --recursive", projectRoot, TxtSetupLog);
            }
            else
            {
                AppendLog("→ Submodules already up-to-date, skipping init.", TxtSetupLog);
            }

            AppendLog("→ Installing EF tool...", TxtSetupLog);
            await ShellUtils.RunProcessAsync("dotnet", "tool install --global dotnet-ef", projectRoot, TxtSetupLog);

            AppendLog("→ Ensuring .env...", TxtSetupLog);
            string envPath = ShellUtils.GetEnvPath(installRoot);
            if (!File.Exists(envPath))
            {
                return;
            }

            string dataDir = null!;
            ShellUtils.LoadEnvKeys(envPath, new()
            {
                ["MS2_DATA_FOLDER"] = data => dataDir = data
            });

            if (string.IsNullOrEmpty(dataDir) || !Path.IsPathRooted(dataDir) || !Directory.Exists(dataDir))
            {
                AppendLog("[ERROR] MS2_DATA_FOLDER is not a valid absolute path.", TxtSetupLog);
                MainTabControl.SelectedItem = SetupTab;
                TxtClientPath.Background = new SolidColorBrush(Color.FromRgb(255, 200, 200));
                TxtClientPath.Focusable = true;
                TxtClientPath.Focus();
                return;
            }

            TxtClientPath.Background = Brushes.Transparent;

            AppendLog($"→ Using Data folder: {dataDir}", TxtSetupLog);

            AppendLog($"→ Downloading server files into {dataDir}...", TxtSetupLog);
            await ShellUtils.DownloadFilesAsync(dir: dataDir, files: ["Server.m2d", "Server.m2h", "Xml.m2d", "Xml.m2h"], outputBox: TxtSetupLog);

            if (!await PerformMySqlTest(TxtSetupLog))
            {
                return;
            }

            AppendLog("→ Running Maple2.File.Ingest...", TxtSetupLog);
            await ShellUtils.RunProcessAsync("dotnet", "run", Path.Combine(installRoot, "Maple2.File.Ingest"), TxtSetupLog);

            AppendLog("✔ Setup complete!", TxtSetupLog);
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] {ex.Message}", TxtSetupLog);
        }
        finally
        {
            ProgressSetup.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Returns true if the current TxtDb* values produce a working MySQL connection.
    /// If not, logs the error, shows a message and switches to the Database tab.
    /// </summary>
    private async Task<bool> PerformMySqlTest(TextBox output)
    {
        AppendLog("→ Validating database credentials with library...", output);
        bool ok = await ShellUtils.TestMySqlConnectionAsync(TxtDbHost.Text, TxtDbPort.Text, TxtDbUser.Text, TxtDbPassword.Password, TxtSetupLog);

        if (!ok)
        {
            MessageBox.Show("Database validation failed. Please check your credentials.",
                "DB Error", MessageBoxButton.OK, MessageBoxImage.Error);
            MainTabControl.SelectedItem = DatabaseTab;
        }

        return ok;
    }

    private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MainTabControl.SelectedItem is TabItem selected &&
            (selected.Header?.ToString() == "Settings" || selected.Header?.ToString() == "Project"))
        {
            LoadEnvConfig();
        }
    }
    
    private async void BtnSaveDbConfig_Click(object s, RoutedEventArgs e)
    {
        TxtSetupLog.Clear();

        if (!await PerformMySqlTest(TxtSetupLog))
        {
            return;
        }

        if (installRoot != null)
        {
            string envPath = ShellUtils.GetEnvPath(installRoot);

            ShellUtils.UpdateEnvKey(envPath, "DB_IP", TxtDbHost.Text);
            ShellUtils.UpdateEnvKey(envPath, "DB_PORT", TxtDbPort.Text);
            ShellUtils.UpdateEnvKey(envPath, "DB_USER", TxtDbUser.Text);
            ShellUtils.UpdateEnvKey(envPath, "DB_PASSWORD", TxtDbPassword.Password);
        }

        MessageBox.Show("Database configuration saved.", "Success", MessageBoxButton.OK);
    }

    private async void BtnBuildRun_Click(object sender, RoutedEventArgs e)
    {
        serverProcesses.Clear();
        ServerOutputTabs.Items.Clear();

        var servers = new[]
        {
            new { Name = "World", Project = "Maple2.Server.World" },
            new { Name = "Login", Project = "Maple2.Server.Login" },
            new { Name = "Web", Project = "Maple2.Server.Web" },
            new { Name = "Game", Project = "Maple2.Server.Game" },
        };

        foreach (var server in servers)
        {
            await LaunchServerAsync(server.Name, server.Project);
        }
    }

    private void BtnCloneUpdate_Click(object sender, RoutedEventArgs e) => PerformCloneOrUpdateAsync();

    private async void PerformCloneOrUpdateAsync()
    {
        TxtProjectLog.Clear();
        AppendLog("→ Validating repository URL...", TxtProjectLog);
        ProcessStartInfo validateProcessStartInfo = new("git", $"ls-remote {TxtRepoUrl.Text}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using (Process validateProcess = Process.Start(validateProcessStartInfo)!)
        {
            Task<string> stdoutTask = validateProcess.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask  = validateProcess.StandardError.ReadToEndAsync();
            await Task.WhenAll(stdoutTask, stderrTask);
            await validateProcess.WaitForExitAsync();
            
            string stdout = stdoutTask.Result;
            string stderr = stderrTask.Result;
            if (validateProcess.ExitCode != 0)
            {
                string msg = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                AppendLog($"[ERROR] Unable to access repository:\n{msg.Trim()}", TxtProjectLog);
                return;
            }
        }
        
        if (!Directory.Exists(installRoot) && installRoot != null)
        {
            Directory.CreateDirectory(installRoot);
        }

        if (installRoot != null)
        {
            string gitDir = Path.Combine(installRoot, ".git");
            IEnumerable<string> entries = Directory.EnumerateFileSystemEntries(installRoot);
            bool isRepo = Directory.Exists(gitDir);
            bool isEmpty = !entries.Any();

            try
            {
                if (!isRepo && isEmpty)
                {
                    AppendLog("→ Cloning repository (with submodules)...", TxtProjectLog);
                    await ShellUtils.RunProcessAsync("git", $"clone --recursive {TxtRepoUrl.Text} .", installRoot, TxtProjectLog);
                    AppendLog("✔ Cloned successfully.", TxtProjectLog);
                    SaveLauncherConfig();
                    BtnCloneUpdate.Content = "Update";
                }
                else if (isRepo)
                {
                    AppendLog("→ Pulling latest changes...", TxtProjectLog);
                    await ShellUtils.RunProcessAsync("git", "pull", installRoot, TxtProjectLog); 
                    AppendLog("✔ Up to date.", TxtProjectLog);
                }
                else
                {
                    AppendLog("→ Directory isn’t empty and not a Git repo—skipping clone.", TxtProjectLog);
                    BtnCloneUpdate.Content = "Update";
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[ERROR] {ex.Message}", TxtProjectLog);
            }
        }
    }

    private async Task LaunchServerAsync(string name, string projectPath)
    {
        TextBox outputBox = CreateServerOutputControls(name);
        await ShellUtils.RunProcessAsync("dotnet", $"build {projectPath}", installRoot, outputBox);
        if (installRoot != null)
        {
            string exePath = Path.Combine(installRoot, projectPath, "bin", "Debug", "net8.0", $"{projectPath}.exe");
            if (!File.Exists(exePath))
            {
                outputBox.AppendText($"[ERR] Could not find {exePath}\n");
                return;
            }

            Process proc = StartServerProcess(exePath, outputBox);
            serverProcesses[name] = proc;
            outputBox.AppendText($"✔ {name} started (PID {proc.Id})\n");
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

    private TextBox CreateServerOutputControls(string serverName)
    {
        TextBox outputBox = new()
        {
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        outputBox.TextChanged += (_, _) => outputBox.ScrollToEnd();

        Grid cmdGrid = new()
        {
            Margin = new(0, 4, 0, 0)
        };
        cmdGrid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        cmdGrid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        cmdGrid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });

        TextBox cmdInput = new() { Margin = new(0, 0, 4, 0) };
        Grid.SetColumn(cmdInput, 0);

        Button sendButton = new()
        {
            Content = "Send",
            Style = (Style)FindResource("ModernButton"),
            Width = 60,
            Height = 24
        };
        Grid.SetColumn(sendButton, 1);

        sendButton.Click += (_, _) =>
        {
            if (serverProcesses.TryGetValue(serverName, out Process? p) && !p.HasExited)
            {
                p.StandardInput.WriteLine(cmdInput.Text);
                cmdInput.Clear();
            }
        };

        Button killButton = new()
        {
            Content = "Kill",
            Style = (Style)FindResource("ModernButton"),
            Width = 60,
            Height = 24
        };
        Grid.SetColumn(killButton, 2);

        killButton.Click += (_, _) =>
        {
            if (serverProcesses.TryGetValue(serverName, out Process? process) && !process.HasExited)
            {
                ShellUtils.LogFormatted(outputBox, serverName, "WRN", $"Killing process {process.Id}");
                ShellUtils.LogFormatted(outputBox, serverName, "INF", $"Started at {process.StartTime:HH:mm:ss.fff}");
                ShellUtils.LogFormatted(outputBox, serverName, "INF", $"Uptime   {(DateTime.Now - process.StartTime):hh\\:mm\\:ss\\.fff}");
                ShellUtils.LogFormatted(outputBox, serverName, "INF", $"Memory   {process.WorkingSet64 / 1024.0 / 1024.0:F1} MB");
                ShellUtils.LogFormatted(outputBox, serverName, "INF", $"CPU Time {process.TotalProcessorTime:hh\\:mm\\:ss\\.fff}");

                process.EnableRaisingEvents = true;
                process.Exited += (_, _) =>
                {
                    Dispatcher.Invoke(() => { ShellUtils.LogFormatted(outputBox, serverName, "INF", $"Exited code {process.ExitCode} at {process.ExitTime:HH:mm:ss.fff}"); });
                };

                process.Kill(true);
            }
        };
        Grid.SetColumn(killButton, 2);

        DockPanel dock = new();
        DockPanel.SetDock(cmdGrid, Dock.Bottom);
        dock.Children.Add(cmdGrid);
        dock.Children.Add(outputBox);

        cmdGrid.Children.Add(cmdInput);
        cmdGrid.Children.Add(sendButton);
        cmdGrid.Children.Add(killButton);

        TabItem tab = new()
        {
            Header = serverName,
            Content = dock
        };
        ServerOutputTabs.Items.Add(tab);

        return outputBox;
    }

    private Process StartServerProcess(string exePath, TextBox outputBox)
    {
        ProcessStartInfo processStartInfo = new()
        {
            FileName = exePath,
            WorkingDirectory = Path.GetDirectoryName(exePath),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        Process process = new()
        {
            StartInfo = processStartInfo, 
            EnableRaisingEvents = true
        };
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data != null)
            {
                Dispatcher.Invoke(() =>
                {
                    outputBox.AppendText(args.Data + Environment.NewLine);
                    outputBox.ScrollToEnd();
                });
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data != null)
            {
                Dispatcher.Invoke(() =>
                {
                    outputBox.AppendText("[ERR] " + args.Data + Environment.NewLine);
                    outputBox.ScrollToEnd();
                });
            }
        };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private void AppendLog(string text, TextBox output)
    {
        output.AppendText(text + Environment.NewLine);
        output.ScrollToEnd();
    }
    
    private void LoadLauncherConfig()
    {
        bool configExisted = File.Exists(ConfigPath);
        
        if (configExisted)
        {
            string json = File.ReadAllText(ConfigPath);
            config = JsonSerializer.Deserialize<LauncherConfig>(json)!;
        }
        else
        {
            config = new()
            {
                RepoRoot = "https://github.com/AngeloTadeucci/Maple2",
                InstallRoot = Path.Combine(projectRoot, "Maple2")
            };
        }

        repoRoot    = config.RepoRoot!;
        installRoot = config.InstallRoot!;

        TxtRepoUrl.Text = repoRoot;
    }
    
    private void SaveLauncherConfig()
    {
        if (config != null)
        {
            config.RepoRoot = TxtRepoUrl.Text;
            config.InstallRoot = installRoot;
            JsonSerializerOptions options = new()
            {
                WriteIndented = true
            };
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, options));
        }
    }
}