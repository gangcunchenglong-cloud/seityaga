// 学び広場：同梱した HTML をそのまま表示するだけの Electron ラッパー
const { app, BrowserWindow, Menu, shell } = require('electron');
const path = require('path');
const { installOfflineFonts } = require('./offline-fonts');

const APP_TITLE = '学び広場';

// 学習の記録（localStorage）の保存先は旧アプリ名「クイズひろば」のフォルダのまま使う。
// アプリ名を変えると保存先も変わり、これまでの記録が読めなくなるため。
app.setPath('userData', path.join(app.getPath('appData'), 'クイズひろば'));

function createWindow() {
  const win = new BrowserWindow({
    width: 1200,
    height: 860,
    minWidth: 360,
    minHeight: 480,
    title: APP_TITLE,
    autoHideMenuBar: true,
    webPreferences: {
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
    },
  });

  // 外部リンク（http/https）は既定のブラウザで開き、アプリ内では開かない
  // HTML の <title> ではなく、常にアプリ名をウィンドウのタイトルにする
  win.on('page-title-updated', (e) => e.preventDefault());

  win.webContents.setWindowOpenHandler(({ url }) => {
    if (/^https?:/i.test(url)) shell.openExternal(url);
    return { action: 'deny' };
  });
  win.webContents.on('will-navigate', (e, url) => {
    if (/^https?:/i.test(url)) {
      e.preventDefault();
      shell.openExternal(url);
    }
  });

  win.loadFile(path.join(__dirname, 'app', '教科選択.html'));
}

// 二重起動を防ぐ（2回目の起動は既存ウィンドウを前面に出す）
if (!app.requestSingleInstanceLock()) {
  app.quit();
} else {
  app.on('second-instance', () => {
    const [win] = BrowserWindow.getAllWindows();
    if (win) {
      if (win.isMinimized()) win.restore();
      win.focus();
    }
  });

  app.whenReady().then(() => {
    Menu.setApplicationMenu(null);
    installOfflineFonts();
    createWindow();
  });

  app.on('window-all-closed', () => app.quit());
}
