using System.Security;

namespace UrlInsight.Mac.Platform;

/// <summary>
/// ログイン時の自動起動(Windows 版のスタートアップ登録の代わり)。
/// ~/Library/LaunchAgents に設定ファイル(plist)を置くと、次回ログイン時から macOS が URL Insight を起動する。
/// </summary>
internal static class LoginItem
{
    public const string Label = "com.urlinsight.app";

    public static string PlistPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", Label + ".plist");

    public static bool IsEnabled() => File.Exists(PlistPath);

    public static void SetEnabled(bool enabled, string executablePath)
    {
        if (!enabled)
        {
            if (File.Exists(PlistPath)) File.Delete(PlistPath);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(PlistPath)!);
        File.WriteAllText(PlistPath, BuildPlist(executablePath));
    }

    internal static string BuildPlist(string executablePath) =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
            <key>Label</key>
            <string>{Label}</string>
            <key>ProgramArguments</key>
            <array>
                <string>{SecurityElement.Escape(executablePath)}</string>
                <string>--minimized</string>
            </array>
            <key>RunAtLoad</key>
            <true/>
            <key>ProcessType</key>
            <string>Interactive</string>
        </dict>
        </plist>
        """;
}
