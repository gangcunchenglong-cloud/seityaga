using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using UrlInsight.App.Platform;
using UrlInsight.App.Services;
using UrlInsight.Core;
using UrlInsight.Core.AI;
using UrlInsight.Core.Content;
using UrlInsight.Core.Diagnostics;
using UrlInsight.Core.Storage;

namespace UrlInsight.App.UI;

public partial class SettingsWindow : Window
{
    private readonly AppServices _services;
    private bool _loading;

    internal SettingsWindow(AppServices services)
    {
        _services = services;
        InitializeComponent();
        ProviderCombo.ItemsSource = ProviderCatalog.All;
        DelaySlider.ValueChanged += (_, _) => DelayText.Text = $"{(int)DelaySlider.Value} ms";
        LoadFromSettings();
    }

    /// <summary>「すべてのユーザーデータを削除して終了」が選ばれた。</summary>
    public event Action? PurgeRequested;

    public void SelectTab(string name)
    {
        Tabs.SelectedItem = name switch
        {
            "browser" => BrowserTab,
            "ai" => AiTab,
            "privacy" => PrivacyTab,
            "cache" => CacheTab,
            "advanced" => AdvancedTab,
            _ => GeneralTab,
        };
    }

    private void LoadFromSettings()
    {
        _loading = true;
        var s = _services.Settings;
        StartWithWindowsCheck.IsChecked = SafeStartupState(s.StartWithWindows);
        UiaHoverCheck.IsChecked = s.UseUiAutomationHover;
        AutoPinCheck.IsChecked = s.AutoPinSummaries;
        ChromeGeminiCheck.IsChecked = s.UseChromeGemini;
        EdgeCopilotCheck.IsChecked = s.UseEdgeCopilot;
        DelaySlider.Value = s.HoverDelayMs;
        DelayText.Text = $"{s.HoverDelayMs} ms";
        DisplayModeCombo.SelectedIndex = s.DisplayMode == DisplayMode.Pinned ? 1 : 0;
        SideCombo.SelectedIndex = s.CardSide == CardSide.Left ? 1 : 0;

        ExtensionIdBox.Text = NativeHostRegistrar.EffectiveExtensionId(s);
        EdgeCheck.IsChecked = s.RegisterForEdge;

        ProviderCombo.SelectedItem = ProviderCatalog.Get(s.ProviderId);
        ModelBox.Text = s.Model;
        EndpointBox.Text = s.CustomEndpoint;
        ConfirmEachCheck.IsChecked = s.ConfirmBeforeSend;
        MaxCharsBox.Text = s.MaxCharsToSend.ToString();

        CacheEnabledCheck.IsChecked = s.CacheEnabled;
        TtlBox.Text = s.CacheTtlDays.ToString();
        MaxEntriesBox.Text = s.CacheMaxEntries.ToString();
        MaxMbBox.Text = s.CacheMaxMegabytes.ToString();
        LogLevelCombo.SelectedIndex = s.LogLevel switch { "Error" => 0, "Warning" => 1, "Debug" => 3, _ => 2 };

        DataPathText.Text = $"データの保存場所: {_services.Paths.Root}";
        VersionText.Text = $"URL Insight バージョン {AppServices.Version}";
        _loading = false;
        UpdateProviderFields();
        RefreshBridgeStatus();
        RefreshIgnored();
        RefreshCache();
        RefreshYouTubeKeyState();
    }

    private static bool SafeStartupState(bool fallback)
    {
        try { return StartupManager.IsEnabled(); }
        catch (Exception) { return fallback; }
    }

    // ---------- 保存 ----------

    private bool TryCollect(out Action<AppSettings>? apply, out string error)
    {
        apply = null;
        error = string.Empty;
        var preset = (ProviderPreset?)ProviderCombo.SelectedItem ?? ProviderCatalog.All[0];
        var endpoint = EndpointBox.Text.Trim();
        if (preset.EndpointEditable && !ProviderCatalog.TryValidateCustomEndpoint(endpoint, out _, out var epError))
        {
            error = "エンドポイント: " + epError;
            return false;
        }
        if (!int.TryParse(MaxCharsBox.Text, out var maxChars)) { error = "送信本文の上限は数値で入力してください"; return false; }
        if (!int.TryParse(TtlBox.Text, out var ttl)) { error = "有効期限は数値で入力してください"; return false; }
        if (!int.TryParse(MaxEntriesBox.Text, out var maxEntries)) { error = "最大件数は数値で入力してください"; return false; }
        if (!int.TryParse(MaxMbBox.Text, out var maxMb)) { error = "最大容量は数値で入力してください"; return false; }
        var extId = ExtensionIdBox.Text.Trim();
        if (!NativeHostRegistrar.IsValidExtensionId(extId)) { error = "拡張機能IDは英小文字 a〜p の32文字です"; return false; }

        var delay = (int)DelaySlider.Value;
        var mode = DisplayModeCombo.SelectedIndex == 1 ? DisplayMode.Pinned : DisplayMode.HoverOnly;
        var side = SideCombo.SelectedIndex == 1 ? CardSide.Left : CardSide.Right;
        var model = ModelBox.Text.Trim();
        var confirm = ConfirmEachCheck.IsChecked == true;
        var cacheOn = CacheEnabledCheck.IsChecked == true;
        var level = ((ComboBoxItem)LogLevelCombo.SelectedItem).Content.ToString() ?? "Info";
        var edge = EdgeCheck.IsChecked == true;
        var startup = StartWithWindowsCheck.IsChecked == true;
        var uiaHover = UiaHoverCheck.IsChecked == true;
        var autoPin = AutoPinCheck.IsChecked == true;
        var chromeGemini = ChromeGeminiCheck.IsChecked == true;
        var edgeCopilot = EdgeCopilotCheck.IsChecked == true;

        apply = s =>
        {
            s.HoverDelayMs = delay;
            s.DisplayMode = mode;
            s.CardSide = side;
            s.ProviderId = preset.Id;
            s.Model = model;
            s.CustomEndpoint = preset.EndpointEditable ? endpoint : s.CustomEndpoint;
            s.ConfirmBeforeSend = confirm;
            s.MaxCharsToSend = maxChars;
            s.CacheEnabled = cacheOn;
            s.CacheTtlDays = ttl;
            s.CacheMaxEntries = maxEntries;
            s.CacheMaxMegabytes = maxMb;
            s.LogLevel = level;
            s.ExtensionId = extId;
            s.RegisterForEdge = edge;
            s.StartWithWindows = startup;
            s.UseUiAutomationHover = uiaHover;
            s.AutoPinSummaries = autoPin;
            s.UseChromeGemini = chromeGemini;
            s.UseEdgeCopilot = edgeCopilot;
        };
        return true;
    }

    private bool Save()
    {
        if (!TryCollect(out var apply, out var error))
        {
            SaveStatus.Text = error;
            SaveStatus.SetResourceReference(TextBlock.ForegroundProperty, "NgBrush");
            return false;
        }
        var before = _services.Settings;
        _services.UpdateSettings(apply!);
        try
        {
            StartupManager.SetEnabled(_services.Settings.StartWithWindows);
        }
        catch (Exception ex)
        {
            AppLog.Error("startup setting failed", ex);
        }
        if (before.RegisterForEdge != _services.Settings.RegisterForEdge || before.ExtensionId != _services.Settings.ExtensionId)
            NativeHostRegistrar.RepairIfMoved(_services.Paths, _services.Settings);
        SaveStatus.Text = "保存しました";
        SaveStatus.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        return true;
    }

    private void SaveClose_Click(object sender, RoutedEventArgs e)
    {
        if (Save()) Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ---------- ブラウザ拡張 ----------

    private void RefreshBridgeStatus()
    {
        BridgeStatusText.Text = _services.Bridge.BrowserConnected
            ? "● Chrome拡張と接続しています"
            : "● " + ErrorMessages.Title(ErrorCode.ExtensionNotConnected);
        HostStatusText.Text = _services.NativeHostRegistered
            ? $"ネイティブホスト: 登録済み（{NativeHostRegistrar.HostName}）"
            : "ネイティブホスト: 未登録 — 「ホストを登録」を押してください";
    }

    private void RegisterHost_Click(object sender, RoutedEventArgs e)
    {
        if (!Save()) return;
        try
        {
            NativeHostRegistrar.Register(_services.Paths, NativeHostRegistrar.EffectiveExtensionId(_services.Settings), _services.Settings.RegisterForEdge);
            MessageBox.Show(this, "ネイティブホストを登録しました。Chromeで拡張を読み込み済みの場合は、拡張のポップアップで「再接続」を押すか、Chromeを再起動してください。",
                "URL Insight", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "登録できませんでした: " + ex.Message, "URL Insight", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        RefreshBridgeStatus();
    }

    private void UnregisterHost_Click(object sender, RoutedEventArgs e)
    {
        try { NativeHostRegistrar.Unregister(_services.Paths); }
        catch (Exception ex) { MessageBox.Show(this, "解除できませんでした: " + ex.Message, "URL Insight"); }
        RefreshBridgeStatus();
    }

    private void OpenExtensionFolder_Click(object sender, RoutedEventArgs e)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "browser-extension");
        if (!Directory.Exists(dir))
        {
            MessageBox.Show(this, "拡張フォルダが見つかりません。インストーラーで導入したか、READMEの手順を確認してください。\n" + dir, "URL Insight");
            return;
        }
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }

    // ---------- AI ----------

    private ProviderPreset SelectedPreset => (ProviderPreset?)ProviderCombo.SelectedItem ?? ProviderCatalog.All[0];

    private void Provider_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        UpdateProviderFields();
    }

    private void UpdateProviderFields()
    {
        var p = SelectedPreset;
        bool usesApi = p.Kind is ProviderKind.OpenAiCompatible or ProviderKind.Anthropic;
        ModelBox.IsEnabled = usesApi;
        ModelHint.Text = p.ModelHint;
        EndpointBox.IsReadOnly = !p.EndpointEditable;
        EndpointBox.IsEnabled = usesApi;
        if (!p.EndpointEditable) EndpointBox.Text = p.DefaultEndpoint ?? string.Empty;
        else if (EndpointBox.Text.Length == 0 || ProviderCatalog.All.Any(x => x.DefaultEndpoint == EndpointBox.Text))
            EndpointBox.Text = _services.Settings.CustomEndpoint;
        EndpointWarning.Visibility = p.EndpointEditable ? Visibility.Visible : Visibility.Collapsed;
        KeyBox.IsEnabled = KeyPlainBox.IsEnabled = p.RequiresKey;
        PricingButton.Visibility = p.PricingUrl != null ? Visibility.Visible : Visibility.Collapsed;
        TestButton.IsEnabled = p.Kind != ProviderKind.None;
        KeyStateText.Text = !p.RequiresKey
            ? (p.Kind == ProviderKind.Fixture ? "テスト用プロバイダはキー不要で、外部送信しません。" : "AIへは送信しません。")
            : _services.Secrets.Persistent.Exists(LayeredSecretStore.ProviderKeyName(p.Id)) ? "キー: 暗号化して保存済み"
            : _services.Secrets.Session.Exists(LayeredSecretStore.ProviderKeyName(p.Id)) ? "キー: このセッションのみ（終了時に破棄）"
            : "キー: 未設定";
        TestResultText.Text = string.Empty;
    }

    private string EnteredKey => ShowKeyCheck.IsChecked == true ? KeyPlainBox.Text.Trim() : KeyBox.Password.Trim();

    private void ShowKey_Changed(object sender, RoutedEventArgs e)
    {
        if (ShowKeyCheck.IsChecked == true)
        {
            KeyPlainBox.Text = KeyBox.Password;
            KeyPlainBox.Visibility = Visibility.Visible;
            KeyBox.Visibility = Visibility.Collapsed;
        }
        else
        {
            KeyBox.Password = KeyPlainBox.Text;
            KeyPlainBox.Text = string.Empty;
            KeyPlainBox.Visibility = Visibility.Collapsed;
            KeyBox.Visibility = Visibility.Visible;
        }
    }

    private void ClearKeyInputs()
    {
        KeyBox.Password = string.Empty;
        KeyPlainBox.Text = string.Empty;
    }

    private void SaveKey_Click(object sender, RoutedEventArgs e)
    {
        var p = SelectedPreset;
        var key = EnteredKey;
        if (!p.RequiresKey || key.Length == 0)
        {
            KeyStateText.Text = "保存するキーを入力してください";
            return;
        }
        var name = LayeredSecretStore.ProviderKeyName(p.Id);
        try
        {
            _services.Secrets.Save(name, key);
            KeyStateText.Text = "キーを暗号化して保存しました";
        }
        catch (InsightException ex) when (ex.Code == ErrorCode.KeyStorageFailed)
        {
            AppLog.Error("key storage failed", ex);
            var answer = MessageBox.Show(this,
                "APIキーを暗号化して保存できませんでした。平文では保存しません。\n\nこのキーを、アプリを終了するまでの間だけメモリ上で使いますか？",
                "URL Insight", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer == MessageBoxResult.Yes)
            {
                _services.Secrets.Session.Save(name, key);
                KeyStateText.Text = "キー: このセッションのみ（終了時に破棄）";
            }
            else
            {
                KeyStateText.Text = ErrorMessages.Title(ErrorCode.KeyStorageFailed);
            }
        }
        ClearKeyInputs();
        Save();
        UpdateProviderFields();
    }

    private void DeleteKey_Click(object sender, RoutedEventArgs e)
    {
        var p = SelectedPreset;
        _services.Secrets.Delete(LayeredSecretStore.ProviderKeyName(p.Id));
        ClearKeyInputs();
        UpdateProviderFields();
        KeyStateText.Text = "キーを削除しました";
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        var p = SelectedPreset;
        var key = EnteredKey;
        if (key.Length == 0) key = _services.Secrets.TryGet(LayeredSecretStore.ProviderKeyName(p.Id)) ?? string.Empty;
        ISummarizerProvider? provider;
        try
        {
            provider = ProviderCatalog.Create(p.Id, EndpointBox.Text.Trim(), ModelBox.Text.Trim(), key);
        }
        catch (InsightException ex)
        {
            TestResultText.Text = "× " + ErrorMessages.Title(ex.Code);
            return;
        }
        if (provider == null)
        {
            TestResultText.Text = "× モデル名・APIキー・エンドポイントを入力してください";
            return;
        }

        TestButton.IsEnabled = false;
        TestResultText.Text = "テスト中…（短いテスト文のみを送信します）";
        try
        {
            var sample = new SummaryInput(new Uri("https://example.com/connection-test"), PageKind.Web, "接続テスト", null,
                "これは URL Insight の接続テスト用の短い文章です。AIサービスに正しく接続できるかを確認しています。", SourceQuality.FullText);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var result = await Task.Run(() => new SummarizerEngine().SummarizeAsync(provider, sample, 1000, cts.Token));
            TestResultText.Text = $"〇 接続できました（{provider.DisplayName} / {provider.Model}）: {result.SummaryLines.FirstOrDefault()}";
        }
        catch (InsightException ex)
        {
            TestResultText.Text = $"× {ErrorMessages.Title(ex.Code)} — {ErrorMessages.Hint(ex.Code)}\n詳細: {Redactor.Redact(ex.Message)}";
        }
        catch (OperationCanceledException)
        {
            TestResultText.Text = "× " + ErrorMessages.Title(ErrorCode.Timeout);
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private void Pricing_Click(object sender, RoutedEventArgs e)
    {
        var url = SelectedPreset.PricingUrl;
        if (url != null) HoverCoordinator.OpenInBrowser(url);
    }

    private void RefreshYouTubeKeyState()
        => YouTubeKeyState.Text = _services.Secrets.Exists(LayeredSecretStore.YouTubeKeyName) ? "保存済み" : "未設定（YouTubeはタイトル等のみ表示）";

    private void SaveYouTubeKey_Click(object sender, RoutedEventArgs e)
    {
        var key = YouTubeKeyBox.Password.Trim();
        if (key.Length == 0) return;
        try
        {
            _services.Secrets.Save(LayeredSecretStore.YouTubeKeyName, key);
        }
        catch (InsightException)
        {
            MessageBox.Show(this, "キーを暗号化して保存できませんでした。平文では保存しません。", "URL Insight", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        YouTubeKeyBox.Password = string.Empty;
        RefreshYouTubeKeyState();
    }

    private void DeleteYouTubeKey_Click(object sender, RoutedEventArgs e)
    {
        _services.Secrets.Delete(LayeredSecretStore.YouTubeKeyName);
        RefreshYouTubeKeyState();
    }

    // ---------- プライバシー ----------

    private void DisableAi_Click(object sender, RoutedEventArgs e)
    {
        _services.UpdateSettings(s => s.ProviderId = "none");
        _loading = true;
        ProviderCombo.SelectedItem = ProviderCatalog.Get("none");
        _loading = false;
        UpdateProviderFields();
        SaveStatus.Text = "AIへの送信を無効にしました";
    }

    private void ResetConsent_Click(object sender, RoutedEventArgs e)
    {
        _services.UpdateSettings(s => s.ConsentedProviders = new());
        SaveStatus.Text = "次回のAI送信時に再度確認します";
    }

    private void RefreshIgnored() => IgnoredList.ItemsSource = _services.Settings.IgnoredUrls.ToList();

    private void RemoveIgnored_Click(object sender, RoutedEventArgs e)
    {
        if (IgnoredList.SelectedItem is not string url) return;
        _services.UpdateSettings(s => s.IgnoredUrls = s.IgnoredUrls.Where(u => u != url).ToList());
        RefreshIgnored();
    }

    // ---------- キャッシュ ----------

    private sealed record HistoryRow(long Id, string Display);

    private void RefreshCache()
    {
        var cache = _services.Cache;
        if (cache == null)
        {
            CacheStatsText.Text = "キャッシュを利用できません（ログを確認してください）";
            HistoryList.ItemsSource = null;
            return;
        }
        var stats = cache.Stats();
        CacheStatsText.Text = $"使用量: {stats.Count} 件 ・ 約 {Math.Max(1, stats.Bytes / 1024)} KB";
        HistoryList.ItemsSource = cache.Recent(500)
            .Select(x => new HistoryRow(x.Id, $"{x.CreatedAt.ToLocalTime():yyyy/MM/dd HH:mm}  {x.Title}（{x.Domain}）"))
            .ToList();
    }

    private void DeleteHistory_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is HistoryRow row) _services.Cache?.Delete(row.Id);
        RefreshCache();
    }

    private void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "保存済みの要約・履歴をすべて消去しますか？", "URL Insight", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _services.Cache?.Clear();
        RefreshCache();
    }

    // ---------- 詳細 ----------

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_services.Paths.LogsDir}\"") { UseShellExecute = true });

    private void ExportDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"URLInsight-diagnostics-{DateTime.Now:yyyyMMdd-HHmm}.txt",
            Filter = "テキスト (*.txt)|*.txt",
        };
        if (dialog.ShowDialog(this) != true) return;
        var report = DiagnosticsReport.Build(AppServices.Version, _services.Settings, _services.Cache?.Stats(),
            _services.Bridge.BrowserConnected, _services.NativeHostRegistered, AppLog.Tail(200));
        File.WriteAllText(dialog.FileName, report);
        SaveStatus.Text = "診断情報を書き出しました（URL・キー・本文は含みません）";
    }

    private void PurgeAll_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this,
            "設定・キャッシュ・履歴・保存したAPIキー・ログをすべて削除し、アプリを終了します。\nネイティブホストの登録と自動起動も解除します。よろしいですか？",
            "URL Insight", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        Close();
        PurgeRequested?.Invoke();
    }
}
