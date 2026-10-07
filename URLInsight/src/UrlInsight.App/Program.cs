using System;
using System.IO;
using System.Linq;
using System.Threading;
using UrlInsight.App.Platform;
using UrlInsight.App.Services;
using UrlInsight.Core.Diagnostics;
using UrlInsight.Core.Storage;

namespace UrlInsight.App;

/// <summary>
/// エントリポイント。
///   (引数なし)                 常駐アプリを起動(既に起動していれば前面に出す)
///   --minimized                トレイに格納した状態で起動(Windows起動時用)
///   --register-native-host     Chrome へネイティブホストを登録(インストーラーから呼ぶ)
///   --unregister-native-host   登録を解除
///   --purge-user-data          設定・キャッシュ・キー・ログを削除(アンインストーラーから呼ぶ)
/// </summary>
public static class Program
{
    private const string MutexName = @"Local\URLInsight.SingleInstance";

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

        if (args.Contains("--register-native-host"))
        {
            try
            {
                var settings = new SettingsStore(paths.SettingsFile).Load();
                NativeHostRegistrar.Register(paths, NativeHostRegistrar.EffectiveExtensionId(settings), settings.RegisterForEdge);
                return 0;
            }
            catch (Exception ex)
            {
                AppLog.Error("register native host failed", ex);
                return 1;
            }
        }
        if (args.Contains("--unregister-native-host"))
        {
            try
            {
                NativeHostRegistrar.Unregister(paths);
                StartupManager.SetEnabled(false);
                return 0;
            }
            catch (Exception ex)
            {
                AppLog.Error("unregister native host failed", ex);
                return 1;
            }
        }
        if (args.Contains("--purge-user-data"))
        {
            DeleteUserData(paths);
            return 0;
        }

        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // 多重起動: 既存のインスタンスを前面に出して終了
            BridgeServer.TrySendToRunningInstance("app.activate");
            return 0;
        }

        var app = new App(paths, startMinimized: args.Contains("--minimized"));
        return app.Run();
    }

    internal static void DeleteUserData(AppPaths paths)
    {
        try
        {
            if (Directory.Exists(paths.Root)) Directory.Delete(paths.Root, recursive: true);
        }
        catch (Exception)
        {
            // 使用中のファイルがあれば残る(次回削除)
        }
    }
}
