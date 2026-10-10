using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using UrlInsight.Core;
using UrlInsight.Core.AI;
using UrlInsight.Core.Content;
using UrlInsight.Core.Diagnostics;
using UrlInsight.Core.Storage;
using UrlInsight.Mac.Platform;
using UrlInsight.Mac.Services;

namespace UrlInsight.Mac.UI;

/// <summary>設定画面(Windows 版と同じ項目。ブラウザ拡張の項目は Mac 版には無い)。</summary>
public partial class SettingsWindow : Window
{
    private readonly MacServices _services = null!;
    private bool _loading;

    /// <summary>XAML のプレビュー用(使わない)。</summary>
    public SettingsWindow() => InitializeComponent();

    internal SettingsWindow(MacServices services)
    {
        _services = services;
        InitializeComponent();
        ProviderCombo.ItemsSource = ProviderCatalog.All;
        ProviderCombo.SelectionChanged += (_, _) => { if (!_loading) UpdateProviderFields(); };
        DelaySlider.PropertyChanged += (_, e) => { if (e.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty) DelayText.Text = $"{(int)DelaySlider.Value} ms"; };
        ShowKeyCheck.IsCheckedChanged += (_, _) => KeyBox.PasswordChar = ShowKeyCheck.IsChecked == true ? '\0' : '●';

        SaveCloseButton.Click += (_, _) => { if (Save()) Close(); };
        CloseButton.Click += (_, _) => Close();
        AccessibilityButton.Click += (_, _) => MainWindow.OpenAccessibilitySettings();
        SaveKeyButton.Click += async (_, _) => await SaveKey();
        DeleteKeyButton.Click += (_, _) => DeleteKey();
        TestButton.Click += async (_, _) => await Test();
        PricingButton.Click += (_, _) => { if (SelectedPreset.PricingUrl is { } url) HoverCoordinator.OpenInBrowser(url); };
        SaveYouTubeKeyButton.Click += async (_, _) => await SaveYouTubeKey();
        DeleteYouTubeKeyButton.Click += (_, _) => { _services.Secrets.Delete(LayeredSecretStore.YouTubeKeyName); RefreshYouTubeKeyState(); };
        DisableAiButton.Click += (_, _) => DisableAi();
        ResetConsentButton.Click += (_, _) =>
        {
            _services.UpdateSettings(s => s.ConsentedProviders = new());
            SaveStatus.Text = "次回のAI送信時に再度確認します";
        };
        RemoveIgnoredButton.Click += (_, _) => RemoveIgnored();
        DeleteHistoryButton.Click += (_, _) =>
        {
            if (HistoryList.SelectedItem is HistoryRow row) _services.Cache?.Delete(row.Id);
            RefreshCache();
        };
        ClearCacheButton.Click += async (_, _) =>
        {
            if (!await Dialog.Confirm(this, "保存済みの要約・履歴をすべて消去しますか？")) return;
            _services.Cache?.Clear();
            RefreshCache();
        };
        OpenLogsButton.Click += (_, _) => OpenInFinder(_services.Paths.LogsDir);
        ExportDiagnosticsButton.Click += async (_, _) => await ExportDiagnostics();
        PurgeButton.Click += async (_, _) => await Purge();
        LoadFromSettings();
    }

    /// <summary>「すべてのユーザーデータを削除して終了」が選ばれた。</summary>
    public event Action? PurgeRequested;

    public void SelectTab(string name)
    {
        Tabs.SelectedItem = name switch
        {
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
        StartAtLoginCheck.IsChecked = SafeLoginState(s.StartWithWindows);
        HoverCheck.IsChecked = s.UseUiAutomationHover;
        AutoPinCheck.IsChecked = s.AutoPinSummaries;
        DelaySlider.Value = s.HoverDelayMs;
        DelayText.Text = $"{s.HoverDelayMs} ms";
        DisplayModeCombo.SelectedIndex = s.DisplayMode == DisplayMode.Pinned ? 1 : 0;
        SideCombo.SelectedIndex = s.CardSide == CardSide.Left ? 1 : 0;

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
        VersionText.Text = $"URL Insight（Mac 版）バージョン {MacServices.Version}";
        _loading = false;
        UpdateProviderFields();
        RefreshIgnored();
        RefreshCache();
        RefreshYouTubeKeyState();
    }

    private static bool SafeLoginState(bool fallback)
    {
        try { return LoginItem.IsEnabled(); }
        catch (Exception) { return fallback; }
    }

    // ---------- 保存 ----------

    private bool TryCollect(out Action<AppSettings>? apply, out string error)
    {
        apply = null;
        error = string.Empty;
        var preset = SelectedPreset;
        var endpoint = (EndpointBox.Text ?? string.Empty).Trim();
        if (preset.EndpointEditable && !ProviderCatalog.TryValidateCustomEndpoint(endpoint, out _, out var epError))
        {
            error = "エンドポイント: " + epError;
            return false;
        }
        if (!int.TryParse(MaxCharsBox.Text, out var maxChars)) { error = "送信本文の上限は数値で入力してください"; return false; }
        if (!int.TryParse(TtlBox.Text, out var ttl)) { error = "有効期限は数値で入力してください"; return false; }
        if (!int.TryParse(MaxEntriesBox.Text, out var maxEntries)) { error = "最大件数は数値で入力してください"; return false; }
        if (!int.TryParse(MaxMbBox.Text, out var maxMb)) { error = "最大容量は数値で入力してください"; return false; }

        var delay = (int)DelaySlider.Value;
        var mode = DisplayModeCombo.SelectedIndex == 1 ? DisplayMode.Pinned : DisplayMode.HoverOnly;
        var side = SideCombo.SelectedIndex == 1 ? CardSide.Left : CardSide.Right;
        var model = (ModelBox.Text ?? string.Empty).Trim();
        var confirm = ConfirmEachCheck.IsChecked == true;
        var cacheOn = CacheEnabledCheck.IsChecked == true;
        var level = (LogLevelCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Info";
        var login = StartAtLoginCheck.IsChecked == true;
        var hover = HoverCheck.IsChecked == true;
        var autoPin = AutoPinCheck.IsChecked == true;

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
            // 「Windows 起動時に開始」の設定を、Mac では「ログイン時に開始」として使う
            s.StartWithWindows = login;
            s.UseUiAutomationHover = hover;
            s.AutoPinSummaries = autoPin;
        };
        return true;
    }

    private bool Save()
    {
        if (!TryCollect(out var apply, out var error))
        {
            SaveStatus.Text = error;
            SaveStatus.Foreground = CardWindow.Brush("NgBrush");
            return false;
        }
        _services.UpdateSettings(apply!);
        try
        {
            LoginItem.SetEnabled(_services.Settings.StartWithWindows, Environment.ProcessPath ?? string.Empty);
        }
        catch (Exception ex)
        {
            AppLog.Error("login item setting failed", ex);
        }
        SaveStatus.Text = "保存しました";
        SaveStatus.Foreground = CardWindow.Brush("MutedBrush");
        return true;
    }

    // ---------- AI ----------

    private ProviderPreset SelectedPreset => ProviderCombo.SelectedItem as ProviderPreset ?? ProviderCatalog.All[0];

    private void UpdateProviderFields()
    {
        var p = SelectedPreset;
        bool usesApi = p.Kind is ProviderKind.OpenAiCompatible or ProviderKind.Anthropic;
        ModelBox.IsEnabled = usesApi;
        ModelHint.Text = p.ModelHint;
        EndpointBox.IsReadOnly = !p.EndpointEditable;
        EndpointBox.IsEnabled = usesApi;
        if (!p.EndpointEditable) EndpointBox.Text = p.DefaultEndpoint ?? string.Empty;
        else if (string.IsNullOrEmpty(EndpointBox.Text) || ProviderCatalog.All.Any(x => x.DefaultEndpoint == EndpointBox.Text))
            EndpointBox.Text = _services.Settings.CustomEndpoint;
        EndpointWarning.IsVisible = p.EndpointEditable;
        KeyBox.IsEnabled = p.RequiresKey;
        PricingButton.IsVisible = p.PricingUrl != null;
        TestButton.IsEnabled = p.Kind != ProviderKind.None;
        KeyStateText.Text = !p.RequiresKey
            ? (p.Kind == ProviderKind.Fixture ? "テスト用プロバイダはキー不要で、外部送信しません。" : "AIへは送信しません。")
            : _services.Secrets.Persistent.Exists(LayeredSecretStore.ProviderKeyName(p.Id)) ? "キー: キーチェーンに保存済み"
            : _services.Secrets.Session.Exists(LayeredSecretStore.ProviderKeyName(p.Id)) ? "キー: このセッションのみ（終了時に破棄）"
            : "キー: 未設定";
        TestResultText.Text = string.Empty;
    }

    private string EnteredKey => (KeyBox.Text ?? string.Empty).Trim();

    private async Task SaveKey()
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
            KeyStateText.Text = "キーをキーチェーンに保存しました";
        }
        catch (InsightException ex) when (ex.Code == ErrorCode.KeyStorageFailed)
        {
            AppLog.Error("key storage failed", ex);
            if (await Dialog.Confirm(this,
                    "APIキーをキーチェーンに保存できませんでした。平文では保存しません。\n\nこのキーを、アプリを終了するまでの間だけメモリ上で使いますか？"))
            {
                _services.Secrets.Session.Save(name, key);
            }
        }
        KeyBox.Text = string.Empty;
        Save();
        UpdateProviderFields();
    }

    private void DeleteKey()
    {
        _services.Secrets.Delete(LayeredSecretStore.ProviderKeyName(SelectedPreset.Id));
        KeyBox.Text = string.Empty;
        UpdateProviderFields();
        KeyStateText.Text = "キーを削除しました";
    }

    private async Task Test()
    {
        var p = SelectedPreset;
        var key = EnteredKey;
        if (key.Length == 0) key = _services.Secrets.TryGet(LayeredSecretStore.ProviderKeyName(p.Id)) ?? string.Empty;
        ISummarizerProvider? provider;
        try
        {
            provider = ProviderCatalog.Create(p.Id, (EndpointBox.Text ?? string.Empty).Trim(), (ModelBox.Text ?? string.Empty).Trim(), key);
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

    private void RefreshYouTubeKeyState()
        => YouTubeKeyState.Text = _services.Secrets.Exists(LayeredSecretStore.YouTubeKeyName) ? "保存済み" : "未設定（YouTubeはタイトル等のみ表示）";

    private async Task SaveYouTubeKey()
    {
        var key = (YouTubeKeyBox.Text ?? string.Empty).Trim();
        if (key.Length == 0) return;
        try
        {
            _services.Secrets.Save(LayeredSecretStore.YouTubeKeyName, key);
        }
        catch (InsightException)
        {
            await Dialog.Info(this, "キーをキーチェーンに保存できませんでした。平文では保存しません。");
        }
        YouTubeKeyBox.Text = string.Empty;
        RefreshYouTubeKeyState();
    }

    // ---------- プライバシー ----------

    private void DisableAi()
    {
        _services.UpdateSettings(s => s.ProviderId = "none");
        _loading = true;
        ProviderCombo.SelectedItem = ProviderCatalog.Get("none");
        _loading = false;
        UpdateProviderFields();
        SaveStatus.Text = "AIへの送信を無効にしました";
    }

    private void RefreshIgnored() => IgnoredList.ItemsSource = _services.Settings.IgnoredUrls.ToList();

    private void RemoveIgnored()
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

    // ---------- 詳細 ----------

    private static void OpenInFinder(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var psi = new ProcessStartInfo("open") { UseShellExecute = false };
            psi.ArgumentList.Add(dir);
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            AppLog.Error("open folder failed", ex);
        }
    }

    private async Task ExportDiagnostics()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = $"URLInsight-diagnostics-{DateTime.Now:yyyyMMdd-HHmm}.txt",
            DefaultExtension = "txt",
        });
        if (file == null) return;
        var report = DiagnosticsReport.Build(MacServices.Version, _services.Settings, _services.Cache?.Stats(),
            extensionConnected: false, nativeHostRegistered: false, AppLog.Tail(200));
        await using (var stream = await file.OpenWriteAsync())
        await using (var writer = new StreamWriter(stream))
        {
            await writer.WriteAsync(report);
        }
        SaveStatus.Text = "診断情報を書き出しました（URL・キー・本文は含みません）";
    }

    private async Task Purge()
    {
        if (!await Dialog.Confirm(this,
                "設定・キャッシュ・履歴・保存したAPIキー・ログをすべて削除し、アプリを終了します。\nログイン時の自動起動も解除します。よろしいですか？"))
            return;
        Close();
        PurgeRequested?.Invoke();
    }
}
