using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Exceptions;

namespace UrlInsight.Core.Content;

/// <summary>PdfPig(Apache-2.0, マネージド実装)で PDF からテキストを抽出する。OCR は行わない。</summary>
public static class PdfExtractor
{
    public const int MaxPages = 50;
    public const int MinTextChars = 100;

    public static bool LooksLikePdf(byte[] body)
        => body.Length >= 5 && body[0] == '%' && body[1] == 'P' && body[2] == 'D' && body[3] == 'F' && body[4] == '-';

    public static ExtractedContent Extract(byte[] body, Uri url)
    {
        PdfDocument document;
        try
        {
            document = PdfDocument.Open(body, new ParsingOptions { UseLenientParsing = true });
        }
        catch (PdfDocumentEncryptedException ex)
        {
            throw new InsightException(ErrorCode.PdfEncrypted, "encrypted pdf", ex);
        }
        catch (Exception ex) when (ex is not InsightException)
        {
            throw new InsightException(ErrorCode.UnsupportedContent, "pdf parse failed", ex);
        }

        using (document)
        {
            if (document.IsEncrypted)
            {
                // 空パスワードで開けた暗号化PDF(閲覧制限のみ)も、方針として対象外にする
                throw new InsightException(ErrorCode.PdfEncrypted, "encrypted pdf");
            }

            int total = document.NumberOfPages;
            int pages = Math.Min(total, MaxPages);
            var sb = new StringBuilder();
            for (int i = 1; i <= pages; i++)
            {
                try
                {
                    var page = document.GetPage(i);
                    sb.AppendLine(page.Text);
                }
                catch (Exception)
                {
                    // 壊れたページは飛ばす
                }
                if (sb.Length > TextUtil.MaxBodyChars * 2) break;
            }

            var text = TextUtil.Normalize(sb.ToString());
            if (text.Length < MinTextChars)
                throw new InsightException(ErrorCode.PdfImageOnly, "no extractable text");

            string? title = null;
            try { title = document.Information?.Title; } catch (Exception) { }
            if (string.IsNullOrWhiteSpace(title))
                title = FileNameFromUrl(url);

            var (limited, truncated) = TextUtil.LimitBody(text);
            var content = new ExtractedContent
            {
                Kind = PageKind.Pdf,
                Url = url,
                Title = TextUtil.SingleLine(title, 200),
                Text = limited,
                Truncated = truncated,
                Quality = ExtractedContent.QualityFor(limited.Length),
            };
            if (total > MaxPages) content.Notes.Add($"全{total}ページのうち先頭{MaxPages}ページのみを対象にしました");
            if (truncated) content.Notes.Add($"本文が長いため先頭 {TextUtil.MaxBodyChars:N0} 文字までを対象にしました");
            content.Notes.Add("PDF本文には機密情報が含まれる場合があります。送信前に送信先を確認してください");
            return content;
        }
    }

    public static string FileNameFromUrl(Uri url)
    {
        var name = Uri.UnescapeDataString(url.Segments.LastOrDefault() ?? "").Trim('/');
        return string.IsNullOrWhiteSpace(name) ? url.Host : name;
    }
}
