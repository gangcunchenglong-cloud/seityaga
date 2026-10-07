namespace UrlInsight.Core.AI;

public enum ProviderKind { None, OpenAiCompatible, Anthropic, Fixture }

public sealed record ProviderPreset(
    string Id,
    string DisplayName,
    ProviderKind Kind,
    string? DefaultEndpoint,
    bool EndpointEditable,
    bool RequiresKey,
    string ModelHint,
    string? PricingUrl);

/// <summary>
/// 選択可能なプロバイダ一覧。モデル名・料金・無料枠は変動するためハードコードせず、
/// ユーザーが提供元の公式情報を見て入力する(ModelHint は入力例のみ)。
/// </summary>
public static class ProviderCatalog
{
    public static readonly IReadOnlyList<ProviderPreset> All = new[]
    {
        new ProviderPreset("none", "未設定（AI送信なし）", ProviderKind.None, null, false, false, "", null),
        new ProviderPreset("openai", "OpenAI", ProviderKind.OpenAiCompatible, "https://api.openai.com/v1", false, true,
            "提供元のモデル名を入力", "https://openai.com/api/pricing/"),
        new ProviderPreset("anthropic", "Anthropic (Claude)", ProviderKind.Anthropic, "https://api.anthropic.com/v1", false, true,
            "例: claude-opus-5-5 / claude-haiku-4-5", "https://www.anthropic.com/pricing"),
        new ProviderPreset("gemini", "Google Gemini（OpenAI互換API）", ProviderKind.OpenAiCompatible,
            "https://generativelanguage.googleapis.com/v1beta/openai", false, true, "提供元のモデル名を入力", "https://ai.google.dev/pricing"),
        new ProviderPreset("groq", "Groq（OpenAI互換API）", ProviderKind.OpenAiCompatible, "https://api.groq.com/openai/v1", false, true,
            "提供元のモデル名を入力", "https://groq.com/pricing/"),
        new ProviderPreset("openrouter", "OpenRouter（OpenAI互換API）", ProviderKind.OpenAiCompatible, "https://openrouter.ai/api/v1", false, true,
            "提供元のモデル名を入力", "https://openrouter.ai/models"),
        new ProviderPreset("custom", "カスタム OpenAI互換API（高度な設定）", ProviderKind.OpenAiCompatible, null, true, true,
            "提供元のモデル名を入力", null),
        new ProviderPreset("fixture", "テスト用（ローカル・AIではありません）", ProviderKind.Fixture, null, false, false, "", null),
    };

    public static ProviderPreset Get(string? id)
        => All.FirstOrDefault(p => p.Id == id) ?? All[0];

    /// <summary>カスタムエンドポイントの検証。HTTPS 必須、資格情報付き URL 禁止。</summary>
    public static bool TryValidateCustomEndpoint(string? value, out Uri? uri, out string error)
    {
        uri = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var u))
        {
            error = "URLの形式が正しくありません";
            return false;
        }
        if (u.Scheme != Uri.UriSchemeHttps)
        {
            error = "HTTPS のエンドポイントのみ利用できます（平文HTTPは拒否します）";
            return false;
        }
        if (!string.IsNullOrEmpty(u.UserInfo) || !string.IsNullOrEmpty(u.Query) || !string.IsNullOrEmpty(u.Fragment))
        {
            error = "URLに資格情報・クエリ・フラグメントを含めないでください";
            return false;
        }
        uri = u;
        return true;
    }

    public static ISummarizerProvider? Create(string? id, string? customEndpoint, string model, string? apiKey, HttpClient? client = null)
    {
        var preset = Get(id);
        switch (preset.Kind)
        {
            case ProviderKind.None:
                return null;
            case ProviderKind.Fixture:
                return new FixtureProvider();
        }
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(model)) return null;
        if (preset.Kind == ProviderKind.Anthropic)
            return new AnthropicProvider(apiKey, model.Trim(), client);

        Uri endpoint;
        if (preset.EndpointEditable)
        {
            if (!TryValidateCustomEndpoint(customEndpoint, out var u, out _)) return null;
            endpoint = u!;
        }
        else
        {
            endpoint = new Uri(preset.DefaultEndpoint!);
        }
        return new OpenAiCompatibleProvider(endpoint, apiKey, model.Trim(), preset.Id, preset.DisplayName, client);
    }
}
