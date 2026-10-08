using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using UrlInsight.App.Platform;
using UrlInsight.App.Services;
using UrlInsight.App.UI;
using UrlInsight.Core.Diagnostics;
using UrlInsight.Core.Storage;

namespace UrlInsight.App;

/// <summary>常駐アプリ本体。ウィンドウを閉じてもトレイに残り、終了はトレイメニューから行う。</summary>
internal sealed class App : Application
{
    private readonly AppPaths _paths;
    private readonly bool _startMinimized;
    private AppServices? _services;
    private HoverCoordinator? _coordinator;
    private MainWindow? _main;
    private SettingsWindow? _settingsWindow;
    private TrayIcon? _tray;
    private UiaHoverWatcher? _hoverWatcher;

    public App(AppPaths paths, bool startMinimized)
    {
        _paths = paths;
        _startMinimized = startMinimized;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/URLInsight;component/UI/Styles.xaml"),
        });
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => AppLog.Error("unhandled exception", e.ExceptionObject as Exception);
        TaskSchedulerUnobserved();
    }

    private static void TaskSchedulerUnobserved()
        => System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Error("unobserved task exception", e.Exception);
            e.SetObserved();
        };

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (SystemParameters.HighContrast) ApplyHighContrast();

        _services = new AppServices(_paths);
        AppLog.Info($"app started v{AppServices.Version}");
        NativeHostRegistrar.RepairIfMoved(_paths, _services.Settings);

        _coordinator = new HoverCoordinator(_services, Dispatcher);
        _coordinator.SettingsRequested += () => OpenSettings("ai");
        _coordinator.ResultProduced += () => _main?.RefreshRecent();

        _services.Bridge.MessageReceived += OnBridgeMessage;
        _services.Bridge.ConnectionsChanged += () => Dispatcher.BeginInvoke(() => _main?.RefreshStatus());
        _services.SettingsChanged += () => Dispatcher.BeginInvoke(() =>
        {
            _main?.RefreshStatus();
            _tray?.SetPaused(_services.Settings.Paused);
        });
        _services.Bridge.Start();

        // 拡張機能なしでも、アプリを起動しておくだけでホバー検出できるようにする
        _hoverWatcher = new UiaHoverWatcher(() => _services.Settings);
        _hoverWatcher.LinkHovered += (url, text) => _coordinator.OnLocalHover(url, text);
        _hoverWatcher.HoverEnded += () => _coordinator.OnLocalHoverEnd();
        _hoverWatcher.StatusChanged += text => Dispatcher.BeginInvoke(() => _main?.SetHoverStatus(text));

        _main = new MainWindow(_services, _coordinator);
        _hoverWatcher.Start();
        _main.SettingsRequested += () => OpenSettings(null);

        _tray = new TrayIcon();
        _tray.OpenRequested += ShowMain;
        _tray.ManualRequested += () => { ShowMain(); _main.FocusUrlInput(); };
        _tray.PauseToggleRequested += () => _services.UpdateSettings(s => s.Paused = !s.Paused);
        _tray.SettingsRequested += () => OpenSettings(null);
        _tray.ExitRequested += ExitApp;
        _tray.SetPaused(_services.Settings.Paused);

        if (!_services.Settings.FirstRunCompleted)
        {
            RunFirstRun();
        }
        else if (!_startMinimized)
        {
            ShowMain();
        }
    }

    private void OnBridgeMessage(Core.Bridge.BrowserMessage message)
    {
        switch (message.Type)
        {
            case "app.activate":
                Dispatcher.BeginInvoke(ShowMain);
                break;
            case "app.showManual":
                Dispatcher.BeginInvoke(() => { ShowMain(); _main?.FocusUrlInput(); });
                break;
            default:
                _coordinator?.OnBridgeMessage(message);
                break;
        }
    }

    /// <summary>初回起動: 自動起動はユーザーに選んでもらう(既定で有効化しない)。</summary>
    private void RunFirstRun()
    {
        ShowMain();
        var answer = MessageBox.Show(_main!,
            "URL Insight へようこそ。\n\nこのアプリを起動している間、リンクやURLにカーソルを重ねて少し止めると、リンク先の要約カードを画面右側に表示します（Chrome拡張は不要です）。" +
            "\n初期状態では API を使わず、この PC 内で本文から重要そうな文を抜き出して要約します（外部のAIへは送信しません）。" +
            "\nより自然な文章の要約にしたい場合は、設定の「AIプロバイダ」でプロバイダとキーを登録できます（送信前に確認します）。" +
            "\n\nWindows の起動時に URL Insight を自動で開始しますか？\n（あとから設定で変更できます）",
            "URL Insight のセットアップ", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        bool startup = answer == MessageBoxResult.Yes;
        try { StartupManager.SetEnabled(startup); }
        catch (Exception ex) { AppLog.Error("startup registration failed", ex); }
        _services!.UpdateSettings(s =>
        {
            s.FirstRunCompleted = true;
            s.StartWithWindows = startup;
        });
        // APIなしで要約できるので、AI の設定画面は「未設定」のときだけ開く
        if (_services.Settings.ProviderId == "none") OpenSettings("ai");
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
        if (_settingsWindow == null || !_settingsWindow.IsLoaded)
        {
            _settingsWindow = new SettingsWindow(_services);
            _settingsWindow.PurgeRequested += PurgeAndExit;
            _settingsWindow.Closed += (_, _) => { _settingsWindow = null; _main?.RefreshAll(); };
            if (_main != null && _main.IsVisible) _settingsWindow.Owner = _main;
            _settingsWindow.Show();
        }
        if (tab != null) _settingsWindow.SelectTab(tab);
        _settingsWindow.Activate();
    }

    private void ExitApp()
    {
        AppLog.Info("app exiting");
        _hoverWatcher?.Dispose();
        _coordinator?.Hide();
        _tray?.Dispose();
        _services?.Dispose();
        if (_main != null)
        {
            _main.AllowClose = true;
            _main.Close();
        }
        Shutdown();
    }

    private void PurgeAndExit()
    {
        try
        {
            NativeHostRegistrar.Unregister(_paths);
            StartupManager.SetEnabled(false);
        }
        catch (Exception ex)
        {
            AppLog.Error("purge: unregister failed", ex);
        }
        ExitApp();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Program.DeleteUserData(_paths);
    }

    /// <summary>Windows のハイコントラスト設定時はシステム配色を使う。</summary>
    private void ApplyHighContrast()
    {
        void Set(string key, Brush brush) => Resources[key] = brush;
        Set("WindowBgBrush", SystemColors.WindowBrush);
        Set("CardBrush", SystemColors.WindowBrush);
        Set("SubtleBrush", SystemColors.ControlBrush);
        Set("FgBrush", SystemColors.WindowTextBrush);
        Set("MutedBrush", SystemColors.GrayTextBrush);
        Set("LineBrush", SystemColors.WindowTextBrush);
        Set("AccentBrush", SystemColors.HighlightBrush);
        Set("AccentSolidBrush", SystemColors.HighlightBrush);
        Set("AccentFgBrush", SystemColors.HighlightTextBrush);
        foreach (var key in new[] { "OkBgBrush", "NgBgBrush", "WarnBgBrush", "BadgeBgBrush", "PdfBgBrush", "YtBgBrush", "NavSelectedBrush" })
            Set(key, SystemColors.ControlBrush);
        foreach (var key in new[] { "OkBrush", "OkFgBrush", "NgBrush", "BadgeFgBrush", "PdfFgBrush", "YtFgBrush" })
            Set(key, SystemColors.WindowTextBrush);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // 1件の処理の失敗で常駐プロセスを終了させない
        AppLog.Error("ui exception", e.Exception);
        e.Handled = true;
    }
}
