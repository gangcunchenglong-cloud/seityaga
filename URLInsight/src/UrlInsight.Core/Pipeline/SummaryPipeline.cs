using System.Diagnostics;
using UrlInsight.Core.AI;
using UrlInsight.Core.Content;
using UrlInsight.Core.Diagnostics;
using UrlInsight.Core.Net;
using UrlInsight.Core.Storage;

namespace UrlInsight.Core.Pipeline;

/// <summary>UI 側が実装する。呼び出しはバックグラウンドスレッドから行われる。</summary>
public interface IPipelineUi
{
    void Show(CardState state);
    Task<ConsentDecision> RequestConsentAsync(ConsentInfo info, CancellationToken ct);
}

public sealed record SummaryRequest(string Url, string RequestId, bool ForceRefresh = false, string? LinkText = null);

/// <summary>
/// 1件のリンクを要約する処理の流れ(仕様書 9章):
/// URL検証 → 確認中表示 → キャッシュ検索 → 取得・本文抽出 → AI未設定ならメタ情報のみ →
/// 送信前確認 → AI要約 → スキーマ検証 → 表示・キャッシュ保存。
/// </summary>
public sealed class SummaryPipeline
{
    private readonly ContentService _content;
    private readonly SummaryCache? _cache;
    private readonly Func<AppSettings> _settings;
    private readonly Func<ISummarizerProvider?> _provider;
    private readonly Action<string> _rememberConsent;
    private readonly SummarizerEngine _engine;

    public SummaryPipeline(ContentService content, SummaryCache? cache, Func<AppSettings> settings,
        Func<ISummarizerProvider?> provider, Action<string> rememberConsent, SummarizerEngine? engine = null)
    {
        _content = content;
        _cache = cache;
        _settings = settings;
        _provider = provider;
        _rememberConsent = rememberConsent;
        _engine = engine ?? new SummarizerEngine();
    }

    /// <summary>テスト用: ローカルのテストサーバー URL を許可する(本番では常に false)。</summary>
    internal bool AllowLocalUrlsForTesting { get; init; }

    public static string ConsentKey(ISummarizerProvider p) => $"{p.Id}|{p.EndpointHost}";

    private bool TryNormalize(string input, out Uri? url, out ErrorCode error)
    {
        if (AllowLocalUrlsForTesting && Uri.TryCreate(input, UriKind.Absolute, out var local) && local.IsLoopback)
        {
            url = local;
            error = ErrorCode.None;
            return true;
        }
        return UrlPolicy.TryNormalize(input, out url, out error);
    }

    public async Task RunAsync(SummaryRequest request, IPipelineUi ui, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var corr = request.RequestId.Length > 8 ? request.RequestId[..8] : request.RequestId;

        if (!TryNormalize(request.Url, out var url, out var urlError))
        {
            AppLog.Info($"[{corr}] rejected url ({urlError})");
            ui.Show(new CardState
            {
                Phase = CardPhase.Error, Url = request.Url, Domain = "", Error = urlError, CorrelationId = corr,
            });
            return;
        }

        var domain = UrlPolicy.DisplayDomain(url!);
        var settings = _settings();
        ui.Show(new CardState
        {
            Phase = CardPhase.Loading, Url = url!.ToString(), Domain = domain,
            Title = request.LinkText, StatusText = "ページ情報を確認中…", CorrelationId = corr,
        });

        var provider = _provider();
        string providerId = provider?.Id ?? "none";
        string model = provider?.Model ?? "";
        var lookupKey = SummaryCache.LookupKey(url.ToString(), providerId, model);

        if (_cache != null && settings.CacheEnabled && provider != null && !request.ForceRefresh)
        {
            var cached = _cache.TryGet(lookupKey, TimeSpan.FromDays(settings.CacheTtlDays));
            if (cached != null)
            {
                AppLog.Info($"[{corr}] cache hit kind={cached.Kind} {sw.ElapsedMilliseconds}ms");
                ui.Show(new CardState
                {
                    Phase = CardPhase.Result, Url = url.ToString(), Domain = domain, Kind = cached.Kind,
                    Title = cached.Title, Card = cached, FromCache = true, CorrelationId = corr,
                });
                return;
            }
        }

        ExtractedContent content;
        try
        {
            content = await _content.ExtractAsync(url, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            AppLog.Debug($"[{corr}] cancelled during fetch");
            return;
        }
        catch (InsightException ex)
        {
            AppLog.Info($"[{corr}] fetch failed code={ex.Code} stage=fetch {sw.ElapsedMilliseconds}ms");
            ui.Show(new CardState
            {
                Phase = CardPhase.Error, Url = url.ToString(), Domain = domain, Title = request.LinkText,
                Error = ex.Code, CorrelationId = corr,
            });
            return;
        }
        catch (Exception ex)
        {
            AppLog.Error($"[{corr}] unexpected extract error", ex);
            ui.Show(new CardState
            {
                Phase = CardPhase.Error, Url = url.ToString(), Domain = domain, Title = request.LinkText,
                Error = ErrorCode.Internal, CorrelationId = corr,
            });
            return;
        }
        ct.ThrowIfCancellationRequested();
        AppLog.Info($"[{corr}] extracted kind={content.Kind} quality={content.Quality} chars={content.Text.Length} {sw.ElapsedMilliseconds}ms");

        var baseCard = new SummaryCard
        {
            Url = url.ToString(),
            Domain = domain,
            Kind = content.Kind,
            Title = content.Title ?? request.LinkText ?? domain,
            SiteName = content.SiteName,
            Description = content.Description,
            Quality = content.Quality,
            Notes = content.Notes.ToList(),
        };

        // 根拠となる本文が無い場合は AI に推測させない
        if (content.Quality == SourceQuality.MetadataOnly)
        {
            baseCard.Notice = content.Kind == PageKind.YouTube && content.Warning != ErrorCode.None ? content.Warning : ErrorCode.ContentTooShort;
            ShowResult(ui, baseCard, corr);
            return;
        }

        if (provider == null)
        {
            baseCard.Notice = ErrorCode.AiNotConfigured;
            ShowResult(ui, baseCard, corr);
            return;
        }

        int sendChars = Math.Min(content.Text.Length, settings.MaxCharsToSend);
        bool consentNeeded = !provider.IsTestProvider &&
                             (settings.ConfirmBeforeSend || !settings.ConsentedProviders.Contains(ConsentKey(provider)));
        bool warningNeedsConfirm = content.Warning != ErrorCode.None;
        if (consentNeeded || warningNeedsConfirm)
        {
            var info = new ConsentInfo(provider.DisplayName, provider.EndpointHost, url.ToString(), content.Kind, sendChars,
                content.Warning, ShowRememberOption: consentNeeded && !settings.ConfirmBeforeSend);
            ui.Show(new CardState
            {
                Phase = CardPhase.Consent, Url = url.ToString(), Domain = domain, Kind = content.Kind,
                Title = baseCard.Title, Card = baseCard, CorrelationId = corr,
            });
            ConsentDecision decision;
            try
            {
                decision = await ui.RequestConsentAsync(info, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (decision == ConsentDecision.Decline)
            {
                baseCard.Notice = ErrorCode.ConsentDeclined;
                ShowResult(ui, baseCard, corr);
                return;
            }
            if (decision == ConsentDecision.SendAndRemember) _rememberConsent(ConsentKey(provider));
        }

        ui.Show(new CardState
        {
            Phase = CardPhase.Summarizing, Url = url.ToString(), Domain = domain, Kind = content.Kind,
            Title = baseCard.Title, StatusText = $"{provider.DisplayName} で要約中…", Card = baseCard, CorrelationId = corr,
        });

        SummaryOutput output;
        try
        {
            var input = new SummaryInput(url, content.Kind, content.Title ?? request.LinkText, content.Description, content.Text, content.Quality);
            output = await _engine.SummarizeAsync(provider, input, settings.MaxCharsToSend, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            AppLog.Debug($"[{corr}] cancelled during summarize");
            return;
        }
        catch (InsightException ex)
        {
            AppLog.Info($"[{corr}] summarize failed code={ex.Code} stage=ai {sw.ElapsedMilliseconds}ms");
            ui.Show(new CardState
            {
                Phase = CardPhase.Error, Url = url.ToString(), Domain = domain, Kind = content.Kind, Title = baseCard.Title,
                Card = baseCard, Error = ex.Code, CorrelationId = corr,
            });
            return;
        }

        var card = baseCard;
        if (!string.IsNullOrWhiteSpace(output.Title)) card.Title = output.Title;
        card.SummaryLines = output.SummaryLines;
        card.KeyPoints = output.KeyPoints;
        card.Confidence = output.Confidence;
        card.ProviderName = provider.DisplayName;
        card.Model = provider.Model;
        card.IsTestProvider = provider.IsTestProvider;
        card.CreatedAt = DateTimeOffset.UtcNow;
        if (content.Quality == SourceQuality.Partial)
            card.Notes.Insert(0, "本文の一部のみから作成しています（根拠が限定的）");
        if (output.Confidence == "low")
            card.Notes.Insert(0, "AIの確信度が低い要約です。必要に応じてページを開いて確認してください");

        if (_cache != null && settings.CacheEnabled)
        {
            try
            {
                _cache.Put(lookupKey, card, content.ContentHash, providerId, model);
                _cache.Prune(TimeSpan.FromDays(settings.CacheTtlDays), settings.CacheMaxEntries, (long)settings.CacheMaxMegabytes * 1024 * 1024);
            }
            catch (Exception ex)
            {
                AppLog.Error($"[{corr}] cache write failed", ex);
            }
        }

        AppLog.Info($"[{corr}] summarized kind={content.Kind} provider={providerId} {sw.ElapsedMilliseconds}ms");
        ShowResult(ui, card, corr);
    }

    private static void ShowResult(IPipelineUi ui, SummaryCard card, string corr)
        => ui.Show(new CardState
        {
            Phase = CardPhase.Result, Url = card.Url, Domain = card.Domain, Kind = card.Kind, Title = card.Title, Card = card,
            CorrelationId = corr,
        });
}
