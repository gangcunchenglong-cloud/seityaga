namespace UrlInsight.Core.AI;

/// <summary>
/// 要約プロバイダの共通インターフェース。実装は AI 事業者ごとのアダプター。
/// 失敗時は <see cref="InsightException"/>(InvalidApiKey / RateLimited / ProviderError / Timeout / NetworkError)を投げる。
/// </summary>
public interface ISummarizerProvider
{
    string Id { get; }
    string DisplayName { get; }
    string Model { get; }
    /// <summary>送信先ホスト名(同意画面に表示)。</summary>
    string EndpointHost { get; }
    /// <summary>外部へ送信しないテスト用プロバイダか。</summary>
    bool IsTestProvider { get; }
    /// <summary>外部へ送信せず、この PC 内だけで要約するか(送信前の確認が不要)。</summary>
    bool IsLocal => false;

    Task<string> CompleteAsync(SummaryPrompt prompt, CancellationToken ct);
}
