namespace UrlInsight.Core.AI;

/// <summary>
/// プロバイダ呼び出しと再試行の方針:
/// - 出力形式不正: 1回だけ再試行
/// - 429: Retry-After を尊重(最大 <see cref="MaxRetryAfter"/> まで待つ。それ以上なら即エラー)
/// - 5xx / ネットワーク / タイムアウト: 指数バックオフ+ジッターで最大2回
/// - 401/403 等: 再試行しない
/// - 本文に無い数値を含む要約: どの数値かを伝えて1回だけ作り直す
/// </summary>
public sealed class SummarizerEngine
{
    public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(20);
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public SummarizerEngine(Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _delay = delay ?? ((t, ct) => Task.Delay(t, ct));
    }

    public async Task<SummaryOutput> SummarizeAsync(ISummarizerProvider provider, SummaryInput input, int maxChars, CancellationToken ct)
    {
        var prompt = SummaryPromptBuilder.Build(input, maxChars);
        var output = await CompleteAsync(provider, prompt, ct).ConfigureAwait(false);

        // 本文に無い数値が含まれていたら、どの数値かを伝えて1回だけ作り直してもらう
        var unsupported = SummaryGrounding.FindUnsupportedNumbers(output, input);
        if (unsupported.Count == 0) return output;

        var retry = prompt with { User = prompt.User + "\n\n" + SummaryPromptBuilder.GroundingFeedback(unsupported) };
        try
        {
            var second = await CompleteAsync(provider, retry, ct).ConfigureAwait(false);
            var stillUnsupported = SummaryGrounding.FindUnsupportedNumbers(second, input);
            // 作り直しても残る場合は、少ない方を採用して確信度を下げる(カードに注意書きを出す)
            if (stillUnsupported.Count < unsupported.Count) (output, unsupported) = (second, stillUnsupported);
        }
        catch (InsightException)
        {
            // 作り直しに失敗しても、最初の要約に注意書きを付けて表示する
        }
        if (unsupported.Count > 0)
        {
            output.UnsupportedNumbers = unsupported;
            output.Confidence = "low";
        }
        return output;
    }

    private async Task<SummaryOutput> CompleteAsync(ISummarizerProvider provider, SummaryPrompt prompt, CancellationToken ct)
    {
        int invalidOutputRetries = 0, transientRetries = 0, rateLimitRetries = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var raw = await provider.CompleteAsync(prompt, ct).ConfigureAwait(false);
                if (SummaryValidator.TryParse(raw, out var output)) return output;
                if (invalidOutputRetries++ >= 1) throw new InsightException(ErrorCode.InvalidOutput);
            }
            catch (InsightException ex) when (ex.Code == ErrorCode.InvalidOutput && invalidOutputRetries++ < 1)
            {
            }
            catch (InsightException ex) when (ex.Code == ErrorCode.RateLimited)
            {
                var wait = ex.RetryAfter ?? TimeSpan.FromSeconds(5);
                if (rateLimitRetries++ >= 1 || wait > MaxRetryAfter) throw;
                await _delay(wait, ct).ConfigureAwait(false);
            }
            catch (InsightException ex) when (ex.Code is ErrorCode.ProviderError or ErrorCode.NetworkError or ErrorCode.Timeout
                                              && IsTransient(ex) && transientRetries < 2)
            {
                var backoff = TimeSpan.FromMilliseconds(1000 * Math.Pow(2, transientRetries) + Random.Shared.Next(0, 400));
                transientRetries++;
                await _delay(backoff, ct).ConfigureAwait(false);
            }
        }
    }

    private static bool IsTransient(InsightException ex)
    {
        if (ex.Code != ErrorCode.ProviderError) return true;
        // 4xx(モデル名誤りなど)は再試行しない
        return ex.Message.Contains("http 5", StringComparison.Ordinal);
    }
}
