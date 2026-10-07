"use strict";

const $ = (id) => document.getElementById(id);
let currentTab = null;
let pattern = null;

function originPattern(url) {
  try {
    const u = new URL(url);
    if (u.protocol !== "https:" && u.protocol !== "http:") return null;
    return `${u.protocol}//${u.hostname}/*`;
  } catch (_) {
    return null;
  }
}

async function send(message) {
  return chrome.runtime.sendMessage(message);
}

function setDot(el, ok) {
  el.classList.toggle("ok", ok === true);
  el.classList.toggle("ng", ok === false);
}

async function refresh() {
  const res = await send({ action: "getStatus" });
  const status = (res && res.status) || {};
  setDot($("dot-host"), status.hostConnected === true ? true : status.hostConnected === false ? false : undefined);
  $("host-text").textContent = status.hostConnected
    ? "ネイティブホストと接続しています"
    : "ネイティブホストに接続できません";
  setDot($("dot-app"), status.appRunning === true ? true : false);
  $("app-text").textContent = status.appRunning ? "URL Insight アプリ: 稼働中" : "URL Insight アプリ: 起動していません";

  const err = status.lastError || "";
  if (!status.hostConnected && err) {
    $("error-text").hidden = false;
    $("error-text").textContent = /not found|not allowed|Specified native messaging host/i.test(err)
      ? "ホストが登録されていません。アプリの「設定 → ブラウザ拡張」で「ホストを登録」を押してください。"
      : `詳細: ${err}`;
  } else {
    $("error-text").hidden = true;
  }
  $("paused").hidden = !(res && res.paused);

  const enabled = !!(res && pattern && res.enabledOrigins.includes(pattern));
  $("site-state").textContent = pattern ? (enabled ? "有効: リンクにホバーすると要約します" : "無効") : "このページでは利用できません";
  $("enable-site").hidden = enabled || !pattern;
  $("disable-site").hidden = !enabled;
  $("enable-once").disabled = !pattern;
}

async function init() {
  [currentTab] = await chrome.tabs.query({ active: true, currentWindow: true });
  pattern = currentTab && currentTab.url ? originPattern(currentTab.url) : null;
  $("site-name").textContent = pattern ? new URL(currentTab.url).hostname : "対象外のページ";

  $("enable-site").addEventListener("click", async () => {
    // permissions.request はユーザー操作(クリック)の中で呼ぶ必要がある
    const granted = await chrome.permissions.request({ origins: [pattern] });
    if (!granted) return;
    await send({ action: "enableOrigin", pattern, tabId: currentTab.id });
    await refresh();
  });
  $("disable-site").addEventListener("click", async () => {
    await send({ action: "disableOrigin", pattern });
    await refresh();
  });
  $("enable-once").addEventListener("click", async () => {
    await send({ action: "enableTabOnce", tabId: currentTab.id });
    $("site-state").textContent = "このタブで一時的に有効です（再読み込みで解除）";
  });
  $("reconnect").addEventListener("click", async () => {
    await send({ action: "reconnect" });
    setTimeout(refresh, 800);
  });
  await refresh();
}

init();
