// URL Insight / LinkLens — background service worker (Manifest V3)
// ・Native Messaging でWindowsアプリ(ネイティブホスト)と接続する
// ・content script からのホバー通知を検証してアプリへ中継する
// ・ユーザーが許可したサイトにだけ content script を登録する
// サービスワーカーは休止・再起動するため、状態は chrome.storage に置き、接続は必要時に張り直す。
"use strict";

const HOST_NAME = "com.urlinsight.linklens";
const SCRIPT_ID = "linklens-content";
const SCHEMA_VERSION = 1;
const MAX_URL_LENGTH = 4096;
const REQUEST_ID = /^[A-Za-z0-9-]{8,64}$/;

let port = null;
let reconnectDelayMs = 1000;
let reconnectTimer = null;

// ---------- Native Messaging ----------

function connect() {
  if (port) return port;
  try {
    port = chrome.runtime.connectNative(HOST_NAME);
  } catch (e) {
    port = null;
    setStatus({ hostConnected: false, appRunning: false, lastError: String(e && e.message || e) });
    scheduleReconnect();
    return null;
  }
  port.onMessage.addListener(onHostMessage);
  port.onDisconnect.addListener(() => {
    const err = chrome.runtime.lastError ? chrome.runtime.lastError.message : "disconnected";
    port = null;
    setStatus({ hostConnected: false, appRunning: false, lastError: err });
    scheduleReconnect();
  });
  setStatus({ hostConnected: true, lastError: "" });
  post({ type: "hello", browser: "chrome", extensionVersion: chrome.runtime.getManifest().version });
  reconnectDelayMs = 1000;
  return port;
}

function scheduleReconnect() {
  if (reconnectTimer) return;
  reconnectTimer = setTimeout(() => {
    reconnectTimer = null;
    connect();
  }, reconnectDelayMs);
  reconnectDelayMs = Math.min(reconnectDelayMs * 2, 60000);
}

function post(message) {
  const p = connect();
  if (!p) return false;
  try {
    p.postMessage(Object.assign({ schemaVersion: SCHEMA_VERSION }, message));
    return true;
  } catch (_) {
    port = null;
    scheduleReconnect();
    return false;
  }
}

function onHostMessage(msg) {
  if (!msg || typeof msg !== "object" || msg.schemaVersion !== SCHEMA_VERSION) return;
  switch (msg.type) {
    case "config":
      chrome.storage.local.set({
        hoverDelayMs: clampNumber(msg.hoverDelayMs, 300, 1500, 600),
        paused: msg.paused === true,
      });
      break;
    case "status":
      setStatus({ appRunning: msg.appRunning === true });
      break;
    case "error":
      setStatus({ lastError: String(msg.code || "error").slice(0, 100) });
      break;
  }
}

function clampNumber(v, min, max, fallback) {
  return typeof v === "number" && isFinite(v) ? Math.min(max, Math.max(min, v)) : fallback;
}

function setStatus(patch) {
  chrome.storage.session.get("status").then(({ status }) => {
    chrome.storage.session.set({ status: Object.assign({}, status || {}, patch, { updatedAt: Date.now() }) });
  });
}

// ---------- content script からのメッセージ ----------

chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  // ポップアップからの操作
  if (sender.id === chrome.runtime.id && !sender.tab) {
    handlePopupMessage(msg).then(sendResponse, (e) => sendResponse({ ok: false, error: String(e && e.message || e) }));
    return true;
  }
  // content script からの通知(不信データとして検証)
  if (!sender.tab || typeof msg !== "object" || msg === null) return false;
  switch (msg.kind) {
    case "hoverLink": {
      if (!REQUEST_ID.test(String(msg.requestId))) return false;
      if (typeof msg.url !== "string" || msg.url.length > MAX_URL_LENGTH || !/^https?:\/\//i.test(msg.url)) return false;
      post({
        type: "hoverLink",
        requestId: msg.requestId,
        url: msg.url,
        linkText: typeof msg.linkText === "string" ? msg.linkText.slice(0, 200) : "",
        pageTitle: typeof msg.pageTitle === "string" ? msg.pageTitle.slice(0, 300) : "",
        browser: "chrome",
        tabId: "t" + String(sender.tab.id),
        sentAtUtc: new Date().toISOString(),
      });
      return false;
    }
    case "hoverEnd":
      if (REQUEST_ID.test(String(msg.requestId))) post({ type: "hoverEnd", requestId: msg.requestId });
      return false;
    case "dismiss":
      post({ type: "dismiss" });
      return false;
  }
  return false;
});

// ---------- サイト単位の有効化 ----------

async function getEnabledOrigins() {
  const { enabledOrigins } = await chrome.storage.local.get("enabledOrigins");
  return Array.isArray(enabledOrigins) ? enabledOrigins : [];
}

async function syncContentScripts() {
  const origins = await getEnabledOrigins();
  // 権限が取り消されたものは除外する
  const granted = [];
  for (const o of origins) {
    if (await chrome.permissions.contains({ origins: [o] })) granted.push(o);
  }
  if (granted.length !== origins.length) await chrome.storage.local.set({ enabledOrigins: granted });

  const existing = await chrome.scripting.getRegisteredContentScripts({ ids: [SCRIPT_ID] });
  if (granted.length === 0) {
    if (existing.length) await chrome.scripting.unregisterContentScripts({ ids: [SCRIPT_ID] });
    return;
  }
  const script = {
    id: SCRIPT_ID,
    js: ["src/shared/linkfilter.js", "src/content/content.js"],
    matches: granted,
    runAt: "document_idle",
    allFrames: false,
    persistAcrossSessions: true,
  };
  if (existing.length) await chrome.scripting.updateContentScripts([script]);
  else await chrome.scripting.registerContentScripts([script]);
}

async function injectInto(tabId) {
  try {
    await chrome.scripting.executeScript({
      target: { tabId },
      files: ["src/shared/linkfilter.js", "src/content/content.js"],
    });
  } catch (_) {
    // Chrome内部ページ・Web Store などは注入できない(仕様どおり対象外)
  }
}

async function handlePopupMessage(msg) {
  switch (msg && msg.action) {
    case "getStatus": {
      connect();
      const { status } = await chrome.storage.session.get("status");
      const { paused } = await chrome.storage.local.get("paused");
      return { ok: true, status: status || {}, paused: paused === true, enabledOrigins: await getEnabledOrigins() };
    }
    case "enableOrigin": {
      // 権限要求はポップアップ側(ユーザー操作)で済ませてから呼ばれる
      const pattern = String(msg.pattern || "");
      if (!(await chrome.permissions.contains({ origins: [pattern] }))) return { ok: false, error: "permission" };
      const origins = await getEnabledOrigins();
      if (!origins.includes(pattern)) origins.push(pattern);
      await chrome.storage.local.set({ enabledOrigins: origins });
      await syncContentScripts();
      if (typeof msg.tabId === "number") await injectInto(msg.tabId);
      connect();
      return { ok: true };
    }
    case "disableOrigin": {
      const pattern = String(msg.pattern || "");
      const origins = (await getEnabledOrigins()).filter((o) => o !== pattern);
      await chrome.storage.local.set({ enabledOrigins: origins });
      await syncContentScripts();
      try { await chrome.permissions.remove({ origins: [pattern] }); } catch (_) { /* ignore */ }
      return { ok: true };
    }
    case "enableTabOnce": {
      // activeTab 権限で、このタブにだけ一時的に注入する(サイト権限は付与しない)
      if (typeof msg.tabId === "number") await injectInto(msg.tabId);
      connect();
      return { ok: true };
    }
    case "reconnect":
      if (port) { try { port.disconnect(); } catch (_) { /* ignore */ } port = null; }
      reconnectDelayMs = 1000;
      connect();
      return { ok: true };
  }
  return { ok: false, error: "unknown action" };
}

chrome.permissions.onRemoved.addListener(() => { syncContentScripts(); });
chrome.runtime.onStartup.addListener(() => { syncContentScripts(); connect(); });
chrome.runtime.onInstalled.addListener(() => { syncContentScripts(); connect(); });
