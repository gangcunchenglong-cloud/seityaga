// URL Insight / LinkLens — リンクURLの事前フィルタ(content script とテストで共用)
// 最終的な検証はWindowsアプリ側で必ず再度行う。ここでは送る必要のないURLを減らすだけ。
(function (root) {
  "use strict";

  const MAX_URL_LENGTH = 4096;

  /**
   * ホバーしたリンクの href を検証し、送信してよいURL文字列を返す。対象外なら null。
   * @param {string} href
   * @param {string} [pageHref] 現在のページURL(同一ページ内リンクの除外に使う)
   */
  function normalize(href, pageHref) {
    if (typeof href !== "string" || href.length === 0 || href.length > MAX_URL_LENGTH) return null;
    let url;
    try {
      url = new URL(href);
    } catch (_) {
      return null;
    }
    if (url.protocol !== "https:" && url.protocol !== "http:") return null; // javascript:, data:, file:, chrome: 等を拒否
    if (url.username || url.password) return null; // 資格情報付きURLは送らない
    if (pageHref) {
      try {
        const page = new URL(pageHref);
        // 同じページ内のアンカー(#...)は対象外
        if (page.origin === url.origin && page.pathname === url.pathname && page.search === url.search) return null;
      } catch (_) {
        /* ignore */
      }
    }
    url.hash = "";
    return url.toString();
  }

  /** 要約に使える程度の短いリンクテキスト */
  function linkText(anchor) {
    if (!anchor) return "";
    const t = (anchor.innerText || anchor.textContent || anchor.getAttribute("title") || anchor.getAttribute("aria-label") || "")
      .replace(/\s+/g, " ")
      .trim();
    return t.slice(0, 200);
  }

  const api = { normalize, linkText, MAX_URL_LENGTH };
  if (typeof module !== "undefined" && module.exports) module.exports = api;
  root.LinkLensFilter = api;
})(typeof globalThis !== "undefined" ? globalThis : this);
