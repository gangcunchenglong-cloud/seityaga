// HTML が読み込もうとする Google Fonts を、アプリに同梱したフォントで応答する。
// HTML 自体は書き換えずに、インターネットにつながっていなくてもフォントが表示されるようにするため。
const { protocol, net } = require('electron');
const fs = require('fs');
const path = require('path');

const FONT_DIR = path.join(__dirname, 'fonts');
const LOCAL_PREFIX = '/quiz-local/';

function respondFile(file, type) {
  try {
    return new Response(fs.readFileSync(file), {
      headers: { 'Content-Type': type, 'Access-Control-Allow-Origin': '*' },
    });
  } catch (e) {
    return new Response('Not Found', { status: 404 });
  }
}

function installOfflineFonts() {
  protocol.handle('https', (request) => {
    const url = new URL(request.url);
    if (url.hostname === 'fonts.googleapis.com') {
      return respondFile(path.join(FONT_DIR, 'fonts.css'), 'text/css; charset=utf-8');
    }
    if (url.hostname === 'fonts.gstatic.com' && url.pathname.startsWith(LOCAL_PREFIX)) {
      const name = path.basename(url.pathname);
      return respondFile(path.join(FONT_DIR, 'files', name), 'font/woff2');
    }
    // それ以外の https は通常どおりインターネットへ
    return net.fetch(request, { bypassCustomProtocolHandlers: true });
  });
}

module.exports = { installOfflineFonts };
