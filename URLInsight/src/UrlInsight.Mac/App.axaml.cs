using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using UrlInsight.Core.AI;
using UrlInsight.Core.Diagnostics;
using UrlInsight.Core.Storage;
using UrlInsight.Mac.Platform;
using UrlInsight.Mac.Services;
using UrlInsight.Mac.UI;

namespace UrlInsight.Mac;

/// <summary>
/// 常駐アプリ本体(Mac 版)。Dock には出さず、メニューバーのアイコンから操作する。
/// ウィンドウを閉じてもメニューバーに残り、終了はメニューバーの「終了」から行う。
/// </summary>
public partial class App : Application
{
    private MacServices? _services;
    private HoverCoordinator? _coordinator;
    private MainWindow? _main;
    private SettingsWindow? _settingsWindow;
    private AxHoverWatcher? _hoverWatcher;
    private NativeMenuItem? _pauseItem;
    private TrayIcon? _tray;

    internal static AppPaths Paths { get; set; } = new();
    internal static bool StartMinimized { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Start();
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void Start()
    {
        _services = new MacServices(Paths);
        AppLog.Info($"app started v{MacServices.Version} (macOS)");

        _coordinator = new HoverCoordinator(_services);
        // 前回固定していたカードを、同じ位置に表示し直す
        _coordinator.RestorePinned();
        _coordinator.SettingsRequested += () => OpenSettings("ai");
        _coordinator.ResultProduced += () => _main?.RefreshRecent();
        _services.SettingsChanged += () => Dispatcher.UIThread.Post(() =>
        {
            _main?.RefreshStatus();
            SetPaused(_services.Settings.Paused);
        });

        // アプリを起動しておくだけで、カーソルを重ねたリンクを検出する
        _hoverWatcher = new AxHoverWatcher(() => _services.Settings);
        _hoverWatcher.LinkHovered += (url, text) => _coordinator.OnLocalHover(url, text);
        _hoverWatcher.HoverEnded += () => _coordinator.OnLocalHoverEnd();
        _hoverWatcher.StatusChanged += text => Dispatcher.UIThread.Post(() =>
        {
            _main?.SetHoverStatus(text);
            _main?.RefreshStatus();
        });

        _main = new MainWindow(_services, _coordinator) { AccessibilityTrusted = () => MacNative.AXIsProcessTrusted() };
        _main.SettingsRequested += () => OpenSettings(null);
        _hoverWatcher.Start();

        CreateTrayIcon();
        SetPaused(_services.Settings.Paused);

        if (!_services.Settings.FirstRunCompleted)
        {
            _ = RunFirstRun();
        }
        else
        {
            if (!StartMinimized) ShowMain();
            // 許可が無いときは、macOS の案内を出す(システム設定を開くボタン付き)
            if (!MacNative.AXIsProcessTrusted()) MacNative.IsAccessibilityTrusted(prompt: true);
        }
    }

    /// <summary>メニューバーのアイコンとメニュー(Windows 版のトレイメニューと同じ項目)。</summary>
    private void CreateTrayIcon()
    {
        var menu = new NativeMenu();
        void Add(string header, Action action)
        {
            var item = new NativeMenuItem(header);
            item.Click += (_, _) => action();
            menu.Add(item);
        }
        Add("URL Insight を開く", ShowMain);
        Add("URLを貼り付けて要約…", () => { ShowMain(); _main?.FocusUrlInput(); });
        _pauseItem = new NativeMenuItem("一時停止");
        _pauseItem.Click += (_, _) => _services?.UpdateSettings(s => s.Paused = !s.Paused);
        menu.Add(_pauseItem);
        Add("固定したカードをすべて閉じる", () => _coordinator?.ClosePinned());
        Add("設定…", () => OpenSettings(null));
        menu.Add(new NativeMenuItemSeparator());
        Add("URL Insight を終了", ExitApp);

        _tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://URLInsight/Assets/menubar.png"))),
            ToolTipText = "URL Insight",
            Menu = menu,
            IsVisible = true,
        };
        TrayIcon.SetIcons(this, new TrayIcons { _tray });
    }

    private void SetPaused(bool paused)
    {
        if (_pauseItem != null) _pauseItem.Header = paused ? "再開" : "一時停止";
        if (_tray != null) _tray.ToolTipText = paused ? "URL Insight（一時停止中）" : "URL Insight";
    }

    /// <summary>初回起動: アクセシビリティの許可と自動起動を案内し、AI の設定画面を開く。</summary>
    private async Task RunFirstRun()
    {
        ShowMain();
        bool login = await Dialog.Confirm(_main,
            "URL Insight へようこそ。\n\nこのアプリを起動している間、リンクやURLにカーソルを重ねて少し止めると、リンク先の要約カードを画面右側に表示します。" +
            "\nそのために、macOS の「アクセシビリティ」の許可が必要です。このあと表示される案内で「システム設定を開く」を押し、URL Insight をオンにしてください。" +
            "\n初期状態ではAIへの送信は行いません（設定でプロバイダとキーを登録したときだけ、確認のうえで送信します）。" +
            "\n\nMac にログインしたときに URL Insight を自動で開始しますか？（あとから設定で変更できます）",
            "自動で開始する", "今はしない");
        try { LoginItem.SetEnabled(login, Environment.ProcessPath ?? string.Empty); }
        catch (Exception ex) { AppLog.Error("login item registration failed", ex); }
        _services!.UpdateSettings(s =>
        {
            s.FirstRunCompleted = true;
            s.StartWithWindows = login;
        });
        MacNative.IsAccessibilityTrusted(prompt: true);
        OpenSettings("ai");
    }

    public void ShowMain()
    {
        if (_main == null) return;
        if (!_main.IsVisible) _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
        _main.RefreshAll();
    }

    private void OpenSettings(string? tab)
    {
        if (_services == null) return;
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow(_services);
            _settingsWindow.PurgeRequested += PurgeAndExit;
            _settingsWindow.Closed += (_, _) => { _settingsWindow = null; _main?.RefreshAll(); };
            _settingsWindow.Show();
        }
        if (tab != null) _settingsWindow.SelectTab(tab);
        _settingsWindow.Activate();
    }

    private void ExitApp()
    {
        AppLog.Info("app exiting");
        _hoverWatcher?.Dispose();
        _coordinator?.CloseAll();
        _services?.Dispose();
        if (_main != null)
        {
            _main.AllowClose = true;
            _main.Close();
        }
        if (_tray != null) _tray.IsVisible = false;
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    private void PurgeAndExit()
    {
        try
        {
            LoginItem.SetEnabled(false, string.Empty);
            // キーチェーンに保存した API キーも消す
            foreach (var p in ProviderCatalog.All) _services?.Secrets.Delete(LayeredSecretStore.ProviderKeyName(p.Id));
            _services?.Secrets.Delete(LayeredSecretStore.YouTubeKeyName);
        }
        catch (Exception ex)
        {
            AppLog.Error("purge: cleanup failed", ex);
        }
        ExitApp();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Program.DeleteUserData(Paths);
    }
}
