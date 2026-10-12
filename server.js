// URLビューア用の中継(プロキシ)サーバー
// 使い方: node server.js  →  ブラウザで http://localhost:3000/ を開く
//
// 仕組み:
//   /proxy/https://example.com/... へのアクセスを受けると、サーバーが代わりに
//   そのサイトを取得し、埋め込みを禁止するヘッダー(X-Frame-Options / CSP)を
//   取り除いてから返す。HTML・CSS内のURLも /proxy/ 経由に書き換えるので、
//   ページ内でリンクをたどってもプロキシ経由のまま表示される。
//
// 注意: 誰でも使える中継サーバーになるため、既定では自分のPC(127.0.0.1)からの
//       接続だけを受け付ける。インターネットに公開しないこと。

const http = require('http');
const fs = require('fs');
const path = require('path');
const { Readable } = require('stream');

const PORT = Number(process.env.PORT) || 3000;
const HOST = process.env.HOST || '127.0.0.1';
const PREFIX = '/proxy/';
const VIEWER = path.join(__dirname, 'url-viewer.html');
const ORIGIN_COOKIE = '__px_origin';

// サイトから返ってきても、ブラウザへは渡さないヘッダー
const DROP_RESPONSE_HEADERS = new Set([
  'x-frame-options',
  'content-security-policy',
  'content-security-policy-report-only',
  'cross-origin-opener-policy',
  'cross-origin-embedder-policy',
  'cross-origin-resource-policy',
  'strict-transport-security',
  'referrer-policy',
  'permissions-policy',
  'report-to',
  'nel',
  'alt-svc',
  'clear-site-data',
  // 本文は Node 側で展開・書き換えるため、長さ・圧縮関連は付け直す
  'content-encoding',
  'content-length',
  'transfer-encoding',
  'connection',
  'keep-alive',
  'set-cookie',
  'location',
]);

// ブラウザから来ても、サイトへは渡さないヘッダー
const DROP_REQUEST_HEADERS = new Set([
  'host', 'connection', 'keep-alive', 'origin', 'referer', 'cookie',
  'accept-encoding', 'content-length', 'transfer-encoding', 'upgrade',
  'proxy-connection', 'te', 'trailer',
]);

// ---------- URL書き換え ----------

// /proxy/ 以降を対象URLとして取り出す(スキーム省略時は https を補う)
function targetFromPath(p) {
  let rest = p.slice(PREFIX.length);
  if (!/^https?:\/\//i.test(rest)) {
    // ブラウザやサーバーによって "https:/example.com" に縮まる場合がある
    rest = rest.replace(/^(https?):\/(?!\/)/i, '$1://');
    if (!/^https?:\/\//i.test(rest)) rest = 'https://' + rest;
  }
  return new URL(rest);
}

// URLを /proxy/ 経由の形に変換する(相対URLは base を基準に解決)
function proxify(raw, base) {
  if (raw == null) return raw;
  const s = String(raw).trim();
  if (!s || s[0] === '#' || /^(data|blob|javascript|mailto|tel|about|sms):/i.test(s)) return raw;
  if (s.startsWith(PREFIX)) return raw;
  try {
    const abs = new URL(s, base);
    if (abs.protocol !== 'http:' && abs.protocol !== 'https:') return raw;
    return PREFIX + abs.href;
  } catch {
    return raw;
  }
}

function rewriteSrcset(value, base) {
  return value.split(',').map((part) => {
    const m = part.trim().match(/^(\S+)(\s+.*)?$/);
    if (!m) return part;
    return proxify(m[1], base) + (m[2] || '');
  }).join(', ');
}

function rewriteCss(css, base) {
  return css
    .replace(/url\(\s*(['"]?)(.*?)\1\s*\)/gi, (all, q, u) => `url(${q}${proxify(u, base)}${q})`)
    .replace(/@import\s+(['"])(.*?)\1/gi, (all, q, u) => `@import ${q}${proxify(u, base)}${q}`);
}

// iframe内のページに差し込むスクリプト。JavaScriptが後から作るURLや
// fetch / XMLHttpRequest もプロキシ経由になるようにする。
function injectedScript(pageUrl) {
  return `<script>(function(){
var TARGET=${JSON.stringify(pageUrl)},P=location.origin+${JSON.stringify(PREFIX)};
function px(u){try{if(u==null)return u;var s=String(u).trim();if(!s||s[0]==='#'||/^(data|blob|javascript|mailto|tel|about|sms):/i.test(s))return u;
if(s.indexOf(${JSON.stringify(PREFIX)})===0)return u;
var a=new URL(s,TARGET);
if(a.origin===location.origin){if(a.pathname.indexOf(${JSON.stringify(PREFIX)})===0)return a.href;a=new URL(a.pathname+a.search+a.hash,TARGET);}
if(a.protocol!=='http:'&&a.protocol!=='https:')return u;return P+a.href;}catch(e){return u;}}
window.__pxUrl=px;
var of=window.fetch;if(of)window.fetch=function(i,o){try{if(typeof i==='string'||i instanceof URL)i=px(String(i));else if(i&&i.url)i=new Request(px(i.url),i);}catch(e){}return of.call(this,i,o);};
var xo=XMLHttpRequest.prototype.open;XMLHttpRequest.prototype.open=function(m,u){arguments[1]=px(u);return xo.apply(this,arguments);};
var wo=window.open;window.open=function(u){if(u)arguments[0]=px(u);return wo.apply(this,arguments);};
['pushState','replaceState'].forEach(function(k){var f=history[k];history[k]=function(s,t,u){if(u!=null)arguments[2]=px(u);var r=f.apply(this,arguments);report();return r;};});
if(navigator.serviceWorker){try{navigator.serviceWorker.register=function(){return Promise.reject(new Error('disabled by proxy'));};}catch(e){}}
function fixLink(a){var h=a.getAttribute('href');if(h!=null){var n=px(h);if(n!==h)a.setAttribute('href',n);}var t=(a.getAttribute('target')||'').toLowerCase();if(t==='_top'||t==='_parent')a.setAttribute('target','_self');}
document.addEventListener('click',function(e){var a=e.target&&e.target.closest&&e.target.closest('a[href],area[href]');if(a)fixLink(a);},true);
document.addEventListener('submit',function(e){var f=e.target;if(f&&f.getAttribute){var ac=f.getAttribute('action');if(ac){var n=px(ac);if(n!==ac)f.setAttribute('action',n);}var t=(f.getAttribute('target')||'').toLowerCase();if(t==='_top'||t==='_parent')f.setAttribute('target','_self');}},true);
function real(){try{var p=location.pathname+location.search+location.hash;var i=p.indexOf(${JSON.stringify(PREFIX)});return i===0?p.slice(${PREFIX.length}):TARGET;}catch(e){return TARGET;}}
function report(){try{if(window.parent!==window)window.parent.postMessage({type:'proxy-location',url:real(),title:document.title},'*');}catch(e){}}
window.addEventListener('DOMContentLoaded',report);window.addEventListener('hashchange',report);window.addEventListener('popstate',report);
})();</script>`;
}

function rewriteHtml(html, base) {
  const attr = /(\s(?:href|src|action|formaction|poster|background|data|longdesc|cite|manifest)\s*=\s*)(?:(["'])(.*?)\2|([^\s"'>]+))/gis;
  html = html
    // 埋め込み禁止のmetaタグ・改ざん検知(integrity)・文字コード指定を除去
    .replace(/<meta[^>]+http-equiv\s*=\s*["']?content-security-policy[^>]*>/gi, '')
    .replace(/<meta[^>]+charset[^>]*>/gi, '')
    .replace(/\sintegrity\s*=\s*(["']).*?\1/gi, '')
    .replace(/(<base\s[^>]*href\s*=\s*)(["'])(.*?)\2/gi, (all, pre, q, u) => {
      // <base> があれば、以降の相対URLの基準をそれに合わせる
      try { base = new URL(u, base).href; } catch {}
      return `${pre}${q}${proxify(u, base)}${q}`;
    })
    .replace(attr, (all, pre, q, v1, v2) => {
      const v = q ? v1 : v2;
      const quote = q || '"';
      return `${pre}${quote}${proxify(v, base)}${quote}`;
    })
    .replace(/(\s(?:srcset|imagesrcset)\s*=\s*)(["'])(.*?)\2/gis, (all, pre, q, v) => `${pre}${q}${rewriteSrcset(v, base)}${q}`)
    .replace(/(<meta[^>]+http-equiv\s*=\s*["']?refresh["']?[^>]*content\s*=\s*["'][^"']*?url\s*=\s*)([^"'>]+)/gi,
      (all, pre, u) => pre + proxify(u.trim(), base))
    .replace(/(<style[^>]*>)([\s\S]*?)(<\/style>)/gi, (all, a, css, b) => a + rewriteCss(css, base) + b)
    .replace(/(\sstyle\s*=\s*)(["'])(.*?)\2/gis, (all, pre, q, css) => pre + q + rewriteCss(css, base) + q)
    // target="_top" のリンクはビューア自体を置き換えてしまうので、iframe内で開く
    .replace(/(\starget\s*=\s*)(["']?)_(top|parent)\2/gi, '$1$2_self$2');

  const head = '<meta charset="utf-8">' + injectedScript(base);
  if (/<head[^>]*>/i.test(html)) return html.replace(/<head[^>]*>/i, (m) => m + head);
  if (/<html[^>]*>/i.test(html)) return html.replace(/<html[^>]*>/i, (m) => m + head);
  return head + html;
}

// ---------- 文字コード ----------

function detectCharset(contentType, buf) {
  let m = /charset\s*=\s*["']?([\w-]+)/i.exec(contentType || '');
  if (m) return m[1];
  const head = buf.subarray(0, 4096).toString('latin1');
  m = /<meta[^>]+charset\s*=\s*["']?([\w-]+)/i.exec(head);
  if (m) return m[1];
  m = /^@charset\s+["']([\w-]+)/i.exec(head);
  if (m) return m[1];
  return 'utf-8';
}

function decode(buf, charset) {
  try { return new TextDecoder(charset).decode(buf); } catch { return new TextDecoder('utf-8').decode(buf); }
}

// ---------- Cookie ----------

// サイトのCookieを localhost 用に変換(Domainを外し、Pathはプロキシ全体にする)
function rewriteSetCookie(c) {
  return c.split(';').map((p) => p.trim()).filter((p) => {
    const k = p.split('=')[0].trim().toLowerCase();
    return k !== 'domain' && k !== 'path' && k !== 'secure' && k !== 'partitioned';
  }).map((p) => (/^samesite\s*=\s*none$/i.test(p) ? 'SameSite=Lax' : p))
    .concat('Path=/').join('; ');
}

function parseCookies(header) {
  const out = {};
  (header || '').split(';').forEach((p) => {
    const i = p.indexOf('=');
    if (i > 0) out[p.slice(0, i).trim()] = p.slice(i + 1).trim();
  });
  return out;
}

// ---------- リクエスト処理 ----------

function readBody(req) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    req.on('data', (c) => chunks.push(c));
    req.on('end', () => resolve(Buffer.concat(chunks)));
    req.on('error', reject);
  });
}

async function handleProxy(req, res, target) {
  const headers = {};
  for (const [k, v] of Object.entries(req.headers)) {
    if (!DROP_REQUEST_HEADERS.has(k) && !k.startsWith('sec-fetch-') && !k.startsWith('sec-ch-')) headers[k] = v;
  }
  // 中継サーバー自身のCookieはサイトに送らない
  const cookie = (req.headers.cookie || '').split(';').filter((p) => !p.trim().startsWith(ORIGIN_COOKIE + '=')).join(';').trim();
  if (cookie) headers.cookie = cookie;
  if (req.headers.origin) headers.origin = target.origin;
  if (req.headers.referer) {
    try {
      const r = new URL(req.headers.referer);
      if (r.pathname.startsWith(PREFIX)) headers.referer = targetFromPath(r.pathname + r.search).href;
    } catch {}
  }

  const hasBody = !['GET', 'HEAD'].includes(req.method);
  const upstream = await fetch(target.href, {
    method: req.method,
    headers,
    body: hasBody ? await readBody(req) : undefined,
    redirect: 'manual',
  });

  const outHeaders = {};
  upstream.headers.forEach((v, k) => { if (!DROP_RESPONSE_HEADERS.has(k)) outHeaders[k] = v; });
  const cookies = (upstream.headers.getSetCookie ? upstream.headers.getSetCookie() : []).map(rewriteSetCookie);
  cookies.push(`${ORIGIN_COOKIE}=${encodeURIComponent(target.origin)}; Path=/; SameSite=Lax`);
  outHeaders['set-cookie'] = cookies;

  const loc = upstream.headers.get('location');
  if (loc) outHeaders.location = proxify(loc, target.href);

  const type = (upstream.headers.get('content-type') || '').toLowerCase();
  const isHtml = type.includes('text/html') || type.includes('application/xhtml');
  const isCss = type.includes('text/css');

  if (req.method === 'HEAD' || !upstream.body || upstream.status === 204 || upstream.status === 304) {
    res.writeHead(upstream.status, outHeaders);
    return res.end();
  }

  if (isHtml || isCss) {
    const buf = Buffer.from(await upstream.arrayBuffer());
    const text = decode(buf, detectCharset(type, buf));
    // リダイレクト後のURLを基準にする
    const base = upstream.url || target.href;
    const body = isHtml ? rewriteHtml(text, base) : rewriteCss(text.replace(/^@charset[^;]*;/i, ''), base);
    outHeaders['content-type'] = (isHtml ? 'text/html' : 'text/css') + '; charset=utf-8';
    outHeaders['cache-control'] = 'no-store';
    res.writeHead(upstream.status, outHeaders);
    return res.end(body);
  }

  res.writeHead(upstream.status, outHeaders);
  Readable.fromWeb(upstream.body).on('error', () => res.destroy()).pipe(res);
}

// "/style.css" のようにサイト内のルートから始まるURLは /proxy/ を通らずに来るので、
// 直前に見ていたページ(Referer または Cookie)からサイトを推測して振り直す
function guessOrigin(req) {
  try {
    const r = new URL(req.headers.referer);
    if (r.pathname.startsWith(PREFIX)) return targetFromPath(r.pathname).origin;
  } catch {}
  const c = parseCookies(req.headers.cookie)[ORIGIN_COOKIE];
  return c ? decodeURIComponent(c) : null;
}

const server = http.createServer(async (req, res) => {
  try {
    const url = new URL(req.url, 'http://localhost');

    if (url.pathname === '/' || url.pathname === '/url-viewer.html') {
      res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' });
      return fs.createReadStream(VIEWER).pipe(res);
    }

    if (url.pathname.startsWith(PREFIX)) {
      // req.url をそのまま使う(URLオブジェクト経由だと // が正規化されることがある)
      const target = targetFromPath(req.url);
      return await handleProxy(req, res, target);
    }

    const origin = guessOrigin(req);
    if (origin) {
      res.writeHead(307, { location: PREFIX + new URL(req.url, origin).href });
      return res.end();
    }
    res.writeHead(404, { 'content-type': 'text/plain; charset=utf-8' });
    res.end('Not found');
  } catch (err) {
    console.error(req.method, req.url, '-', err.message);
    if (!res.headersSent) {
      res.writeHead(502, { 'content-type': 'text/html; charset=utf-8' });
      res.end(`<!doctype html><meta charset="utf-8"><body style="font-family:sans-serif;padding:24px">
<h2>ページを取得できませんでした</h2><p>${String(err.cause?.message || err.message).replace(/[<>&]/g, '')}</p></body>`);
    } else {
      res.destroy();
    }
  }
});

server.listen(PORT, HOST, () => {
  console.log(`URLビューアを起動しました: http://${HOST === '0.0.0.0' ? 'localhost' : HOST}:${PORT}/`);
});
