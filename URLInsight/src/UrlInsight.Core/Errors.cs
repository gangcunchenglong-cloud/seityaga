namespace UrlInsight.Core;

/// <summary>処理全体で使うエラー分類。UI表示文言は <see cref="ErrorMessages"/> で日本語に変換する。</summary>
public enum ErrorCode
{
    None,
    ExtensionNotConnected,
    AiNotConfigured,
    NetworkError,
    Timeout,
    AccessRestricted,
    NotFound,
    SiteError,
    RateLimited,
    InvalidApiKey,
    ProviderError,
    UnsupportedUrl,
    UnsupportedContent,
    TooLarge,
    PdfImageOnly,
    PdfEncrypted,
    YouTubeNoCaptions,
    YouTubeUnavailable,
    ContentTooShort,
    InvalidOutput,
    BlockedAddress,
    TooManyRedirects,
    Cancelled,
    ConsentDeclined,
    KeyStorageFailed,
    Internal,
}

/// <summary>処理中に発生した、ユーザーへ説明可能なエラー。</summary>
public sealed class InsightException : Exception
{
    public ErrorCode Code { get; }
    public TimeSpan? RetryAfter { get; init; }

    public InsightException(ErrorCode code, string? detail = null, Exception? inner = null)
        : base(detail ?? code.ToString(), inner)
    {
        Code = code;
    }
}

public static class ErrorMessages
{
    public static string Title(ErrorCode code) => code switch
    {
        ErrorCode.ExtensionNotConnected => "ブラウザ拡張が接続されていません",
        ErrorCode.AiNotConfigured => "AI設定が必要です。ページ情報のみ表示しています",
        ErrorCode.NetworkError => "ページへ接続できませんでした",
        ErrorCode.Timeout => "取得に時間がかかっています",
        ErrorCode.AccessRestricted => "このページは取得が制限されています",
        ErrorCode.NotFound => "ページが見つかりませんでした",
        ErrorCode.SiteError => "サイト側でエラーが発生しています",
        ErrorCode.RateLimited => "AIの利用上限に達しました",
        ErrorCode.InvalidApiKey => "APIキーを確認してください",
        ErrorCode.ProviderError => "要約サービスが応答していません",
        ErrorCode.UnsupportedUrl => "この種類のリンクは要約できません",
        ErrorCode.UnsupportedContent => "この形式のファイルは要約できません",
        ErrorCode.TooLarge => "ファイルが大きすぎるため取得を中止しました",
        ErrorCode.PdfImageOnly => "文字を取り出せないPDFです",
        ErrorCode.PdfEncrypted => "暗号化されたPDFのため対象外です",
        ErrorCode.YouTubeNoCaptions => "利用できる字幕が見つかりません",
        ErrorCode.YouTubeUnavailable => "この動画の情報を取得できませんでした",
        ErrorCode.ContentTooShort => "要約に十分な本文がありません",
        ErrorCode.InvalidOutput => "要約結果を読み取れませんでした",
        ErrorCode.BlockedAddress => "ローカル/社内ネットワーク宛てのURLは取得しません",
        ErrorCode.TooManyRedirects => "リダイレクトが多すぎます",
        ErrorCode.Cancelled => "キャンセルしました",
        ErrorCode.ConsentDeclined => "AIへの送信をキャンセルしました",
        ErrorCode.KeyStorageFailed => "APIキーを安全に保存できませんでした",
        _ => "予期しないエラーが発生しました",
    };

    public static string Hint(ErrorCode code) => code switch
    {
        ErrorCode.ExtensionNotConnected => "設定の「ブラウザ拡張」から導入手順を確認するか、URLを手動で貼り付けてください。",
        ErrorCode.AiNotConfigured => "設定の「AI」でプロバイダとAPIキーを設定すると要約できます。",
        ErrorCode.NetworkError => "ネット接続を確認して再試行するか、ページを手動で開いてください。",
        ErrorCode.Timeout => "時間をおいて再試行してください。",
        ErrorCode.AccessRestricted => "ログインやCAPTCHAが必要なページは要約できません。手動で開いてください。",
        ErrorCode.NotFound => "リンク切れの可能性があります。",
        ErrorCode.SiteError => "時間をおいて再試行してください。",
        ErrorCode.RateLimited => "しばらく待って再試行するか、モデル/プロバイダを変更してください。",
        ErrorCode.InvalidApiKey => "設定の「AI」でAPIキーを確認・再入力してください。",
        ErrorCode.ProviderError => "時間をおいて再試行してください。保存済みの要約があれば表示します。",
        ErrorCode.UnsupportedUrl => "http/https の一般的なWebページ、PDF、YouTubeに対応しています。",
        ErrorCode.UnsupportedContent => "Webページ(HTML)、PDF、YouTubeに対応しています。",
        ErrorCode.TooLarge => "上限(初期値20MB)を超えています。手動で開いてください。",
        ErrorCode.PdfImageOnly => "スキャン画像のPDFはOCR未対応です。手動で開いてください。",
        ErrorCode.PdfEncrypted => "パスワード付きPDFは処理しません。",
        ErrorCode.YouTubeNoCaptions => "説明欄のみで続けるか、キャンセルしてください。",
        ErrorCode.YouTubeUnavailable => "非公開・削除・地域/年齢制限の可能性があります。",
        ErrorCode.ContentTooShort => "タイトル等のメタ情報のみ表示しています。",
        ErrorCode.InvalidOutput => "再要約を試してください。続く場合はモデルを変更してください。",
        ErrorCode.BlockedAddress => "安全のため localhost・プライベートIP は取得対象外です。",
        ErrorCode.TooManyRedirects => "手動で開いて確認してください。",
        ErrorCode.KeyStorageFailed => "キーは平文では保存しません。このセッションのみ使うこともできます。",
        _ => "再試行してください。続く場合は診断情報を確認してください。",
    };

    /// <summary>自動で連続再試行してはいけないエラーか。</summary>
    public static bool IsNonRetryable(ErrorCode code) => code is
        ErrorCode.InvalidApiKey or ErrorCode.AccessRestricted or ErrorCode.UnsupportedUrl or
        ErrorCode.UnsupportedContent or ErrorCode.BlockedAddress or ErrorCode.PdfEncrypted or
        ErrorCode.PdfImageOnly or ErrorCode.NotFound;
}
