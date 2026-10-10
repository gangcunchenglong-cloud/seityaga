using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(UrlInsight.Mac.Tests.TestAppBuilder))]

namespace UrlInsight.Mac.Tests;

/// <summary>画面なしモードで Mac 版のアプリ(配色・スタイル)を読み込み、実際の描画(Skia)で画面を作る。</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
