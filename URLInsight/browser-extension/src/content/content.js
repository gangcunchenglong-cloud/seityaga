// URL Insight / LinkLens — content script
// ユーザーが有効化したサイトでのみ動作する。ページは改変せず、リンクへのホバーを検出して
// 最小限の情報(リンクURL・リンク文字列・ページタイトル)をサービスワーカー経由でアプリへ送る。
(function () {
  "use strict";
  if (globalThis.__linkLensLoaded) return;
  globalThis.__linkLensLoaded = true;

  const Filter = globalThis.LinkLensFilter;
  let hoverDelayMs = 600;
  let paused = false;
  let currentAnchor = null;
  let currentRequestId = null;
  let timer = null;

  chrome.storage.local.get(["hoverDelayMs", "paused"]).then((v) => applyConfig(v)).catch(() => {});
  chrome.storage.onChanged.addListener((changes, area) => {
    if (area !== "local") return;
    const v = {};
    if (changes.hoverDelayMs) v.hoverDelayMs = changes.hoverDelayMs.newValue;
    if (changes.paused) v.paused = changes.paused.newValue;
    applyConfig(v);
  });

  function applyConfig(v) {
    if (typeof v.hoverDelayMs === "number") hoverDelayMs = Math.min(1500, Math.max(300, v.hoverDelayMs));
    if (typeof v.paused === "boolean") {
      paused = v.paused;
      if (paused) leave();
    }
  }

  function send(message) {
    try {
      chrome.runtime.sendMessage(message).catch(() => {});
    } catch (_) {
      // 拡張が更新・無効化された後の古いスクリプト。何もしない。
    }
  }

  /** composedPath を使い、Shadow DOM 内のリンクも見つける */
  function findAnchor(event) {
    const path = typeof event.composedPath === "function" ? event.composedPath() : [event.target];
    for (const node of path) {
      if (node && node.nodeType === 1 && node.localName === "a" && node.hasAttribute("href")) return node;
      if (node === document) break;
    }
    return null;
  }

  function leave() {
    if (timer) {
      clearTimeout(timer);
      timer = null;
    }
    if (currentRequestId) send({ kind: "hoverEnd", requestId: currentRequestId });
    currentRequestId = null;
    currentAnchor = null;
  }

  function fire(anchor) {
    timer = null;
    if (paused || anchor !== currentAnchor || !anchor.isConnected) return;
    const url = Filter.normalize(anchor.href, location.href);
    if (!url) return;
    currentRequestId = crypto.randomUUID();
    send({
      kind: "hoverLink",
      requestId: currentRequestId,
      url,
      linkText: Filter.linkText(anchor),
      pageTitle: (document.title || "").slice(0, 300),
    });
  }

  document.addEventListener(
    "pointerover",
    (event) => {
      if (paused || event.pointerType === "touch") return;
      const anchor = findAnchor(event);
      if (anchor === currentAnchor) return;
      leave();
      if (!anchor) return;
      currentAnchor = anchor;
      // 短い通過では何もしない(遅延後にだけ送信)
      timer = setTimeout(() => fire(anchor), hoverDelayMs);
    },
    { capture: true, passive: true }
  );

  document.addEventListener(
    "pointerout",
    (event) => {
      if (!currentAnchor) return;
      const to = event.relatedTarget;
      if (to && currentAnchor.contains(to)) return;
      leave();
    },
    { capture: true, passive: true }
  );

  document.addEventListener(
    "keydown",
    (event) => {
      if (event.key === "Escape") send({ kind: "dismiss" });
    },
    { capture: true, passive: true }
  );

  window.addEventListener("pagehide", leave);
  document.addEventListener("visibilitychange", () => {
    if (document.hidden) leave();
  });
})();
