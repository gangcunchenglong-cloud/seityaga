using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using UrlInsight.Core.Content;

namespace UrlInsight.Core.AI;

/// <summary>HTTP ベースのプロバイダ共通処理(エラー分類・タイムアウト・秘密情報を含まないエラー化)。</summary>
public abstract class HttpProviderBase : ISummarizerProvider
{
    private static readonly HttpClient SharedClient = CreateClient();

    protected HttpProviderBase(Uri endpoint, string apiKey, string model, string id, string displayName, HttpClient? client = null)
    {
        if (endpoint.Scheme != Uri.UriSchemeHttps)
            throw new InsightException(ErrorCode.ProviderError, "https endpoint required");
        Endpoint = endpoint;
        ApiKey = apiKey;
        Model = model;
        Id = id;
        DisplayName = displayName;
        Client = client ?? SharedClient;
    }

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            UseCookies = false,
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(15),
        };
        return new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    }

    protected HttpClient Client { get; }
    protected Uri Endpoint { get; }
    protected string ApiKey { get; }
    public string Id { get; }
    public string DisplayName { get; }
    public string Model { get; }
    public string EndpointHost => Endpoint.Host;
    public bool IsTestProvider => false;
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(90);

    public abstract Task<string> CompleteAsync(SummaryPrompt prompt, CancellationToken ct);

    protected async Task<JsonNode> SendJsonAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(RequestTimeout);
        HttpResponseMessage response;
        try
        {
            response = await Client.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new InsightException(ErrorCode.Timeout, "provider timeout");
        }
        catch (HttpRequestException ex)
        {
            var tls = ex.InnerException is AuthenticationException;
            var dns = ex.InnerException is SocketException;
            throw new InsightException(ErrorCode.NetworkError, tls ? "provider tls failure" : dns ? "provider dns/connect failure" : "provider network failure", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            int status = (int)response.StatusCode;
            if (status is >= 200 and < 300)
            {
                try
                {
                    return JsonNode.Parse(body) ?? throw new InsightException(ErrorCode.InvalidOutput, "empty response");
                }
                catch (JsonException)
                {
                    throw new InsightException(ErrorCode.InvalidOutput, "response is not json");
                }
            }

            var message = ExtractErrorMessage(body);
            switch (status)
            {
                case 401:
                case 403:
                    throw new InsightException(ErrorCode.InvalidApiKey, $"provider http {status}: {message}");
                case 429:
                    throw new InsightException(ErrorCode.RateLimited, $"provider http 429: {message}") { RetryAfter = ParseRetryAfter(response) };
                case >= 500:
                    throw new InsightException(ErrorCode.ProviderError, $"provider http {status}: {message}");
                default:
                    throw new InsightException(ErrorCode.ProviderError, $"provider http {status}: {message}");
            }
        }
    }

    /// <summary>エラー本文から短いメッセージだけを取り出す(キーが含まれていれば伏せる)。</summary>
    private string ExtractErrorMessage(string body)
    {
        string msg = string.Empty;
        try
        {
            var node = JsonNode.Parse(body);
            msg = node?["error"]?["message"]?.GetValue<string>()
                  ?? node?["error"]?.GetValue<string>()
                  ?? node?["message"]?.GetValue<string>()
                  ?? string.Empty;
        }
        catch (Exception)
        {
        }
        if (ApiKey.Length > 0) msg = msg.Replace(ApiKey, "***");
        return TextUtil.SingleLine(Diagnostics.Redactor.Redact(msg), 200);
    }

    private static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        var ra = response.Headers.RetryAfter;
        if (ra?.Delta is TimeSpan d) return d;
        if (ra?.Date is DateTimeOffset date)
        {
            var delta = date - DateTimeOffset.UtcNow;
            return delta > TimeSpan.Zero ? delta : TimeSpan.Zero;
        }
        return null;
    }
}

/// <summary>OpenAI 互換 Chat Completions API(OpenAI / Gemini 互換エンドポイント / Groq / OpenRouter / カスタム)。</summary>
public sealed class OpenAiCompatibleProvider : HttpProviderBase
{
    public OpenAiCompatibleProvider(Uri baseUrl, string apiKey, string model, string id, string displayName, HttpClient? client = null)
        : base(baseUrl, apiKey, model, id, displayName, client)
    {
    }

    public override async Task<string> CompleteAsync(SummaryPrompt prompt, CancellationToken ct)
    {
        var url = new Uri(Endpoint.ToString().TrimEnd('/') + "/chat/completions");
        var payload = new JsonObject
        {
            ["model"] = Model,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = prompt.System },
                new JsonObject { ["role"] = "user", ["content"] = prompt.User },
            },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        var node = await SendJsonAsync(request, ct).ConfigureAwait(false);
        var content = node["choices"]?[0]?["message"]?["content"];
        if (content is JsonValue v && v.TryGetValue<string>(out var text)) return text;
        throw new InsightException(ErrorCode.InvalidOutput, "no message content");
    }
}

/// <summary>Anthropic Messages API(Claude)。</summary>
public sealed class AnthropicProvider : HttpProviderBase
{
    public const string ApiVersion = "2023-06-01";

    public AnthropicProvider(string apiKey, string model, HttpClient? client = null)
        : base(new Uri("https://api.anthropic.com/v1/"), apiKey, model, "anthropic", "Anthropic (Claude)", client)
    {
    }

    public override async Task<string> CompleteAsync(SummaryPrompt prompt, CancellationToken ct)
    {
        var payload = new JsonObject
        {
            ["model"] = Model,
            ["max_tokens"] = 8000,
            ["system"] = prompt.System,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "user", ["content"] = prompt.User },
            },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Endpoint, "messages"))
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-api-key", ApiKey);
        request.Headers.Add("anthropic-version", ApiVersion);
        var node = await SendJsonAsync(request, ct).ConfigureAwait(false);

        var stop = node["stop_reason"]?.GetValue<string>();
        if (stop == "refusal") throw new InsightException(ErrorCode.ProviderError, "model declined the request");

        var sb = new StringBuilder();
        if (node["content"] is JsonArray blocks)
        {
            foreach (var block in blocks)
            {
                if (block?["type"]?.GetValue<string>() == "text")
                    sb.Append(block["text"]?.GetValue<string>());
            }
        }
        if (sb.Length == 0) throw new InsightException(ErrorCode.InvalidOutput, "no text content");
        return sb.ToString();
    }
}

/// <summary>
/// テスト用プロバイダ(AI ではない)。外部送信をせず、抽出本文の先頭文から機械的にカードを組み立てる。
/// API キー無しで取得〜表示の流れを確認するためのもので、カード上でも「テスト用」と明示する。
/// </summary>
public sealed class FixtureProvider : ISummarizerProvider
{
    public string Id => "fixture";
    public string DisplayName => "テスト用（ローカル・AIではありません）";
    public string Model => "fixture-v1";
    public string EndpointHost => "localhost (送信なし)";
    public bool IsTestProvider => true;

    public Task<string> CompleteAsync(SummaryPrompt prompt, CancellationToken ct)
    {
        var sentences = SummaryValidator.SplitSentences(prompt.Input.Text.Replace('\n', ' '))
            .Select(s => TextUtil.SingleLine(s, 100))
            .Where(s => s.Length >= 8)
            .ToList();
        var lines = sentences.Take(3).ToList();
        if (lines.Count == 0 && !string.IsNullOrWhiteSpace(prompt.Input.Description))
            lines.Add(TextUtil.SingleLine(prompt.Input.Description, 100));
        if (lines.Count == 0) lines.Add("本文が短いため抜粋できませんでした。");
        lines.Insert(0, "【テスト用】AIを使わず本文の冒頭を抜粋しています。");
        var output = new SummaryOutput
        {
            Title = TextUtil.SingleLine(prompt.Input.Title ?? prompt.Input.Url.Host, 60),
            SummaryLines = lines.Take(SummaryValidator.MaxLines).ToList(),
            KeyPoints = sentences.Skip(3).Take(2).Select(s => TextUtil.Truncate(s, 40)).ToList(),
            Confidence = "low",
        };
        return Task.FromResult(JsonSerializer.Serialize(output));
    }
}
