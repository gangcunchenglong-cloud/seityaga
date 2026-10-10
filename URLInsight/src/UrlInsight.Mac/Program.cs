using Avalonia;
using UrlInsight.Core.Diagnostics;
using UrlInsight.Core.Storage;

namespace UrlInsight.Mac;

/// <summary>
/// エントリポイント(Mac 版)。
///   (引数なし)    常駐アプリを起動
///   --minimized   ウィンドウを出さずにメニューバーだけで起動(ログイン時の自動起動用)
/// データの保存先は ~/Library/Application Support/URLInsight。
/// </summary>
public static class Program
{
    private static FileStream? _instanceLock;

    [STAThread]
    public static int Main(string[] args)
    {
        var paths = new AppPaths();
        try
        {
            paths.EnsureCreated();
            AppLog.Initialize(paths.LogsDir, LogLevel.Info);
        }
        catch (Exception)
        {
        }

        // 多重起動の防止(2つ目は何もせず終わる。Finder から開き直した場合は macOS が既存のアプリを前に出す)
        try
        {
            _instanceLock = new FileStream(Path.Combine(paths.Root, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return 0;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) => AppLog.Error("unhandled exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Error("unobserved task exception", e.Exception);
            e.SetObserved();
        };

        App.Paths = paths;
        App.StartMinimized = args.Contains("--minimized");
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // Dock にアイコンを出さない(メニューバーに常駐する)
            .With(new MacOSPlatformOptions { ShowInDock = false })
            .LogToTrace();

    internal static void DeleteUserData(AppPaths paths)
    {
        try
        {
            _instanceLock?.Dispose();
            if (Directory.Exists(paths.Root)) Directory.Delete(paths.Root, recursive: true);
        }
        catch (Exception)
        {
        }
    }
}
