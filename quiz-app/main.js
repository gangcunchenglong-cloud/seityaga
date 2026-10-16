// クイズひろば：同梱した HTML をそのまま表示するだけの Electron ラッパー
const { app, BrowserWindow, Menu, shell } = require('electron');
const path = require('path');

function createWindow() {
  const win = new BrowserWindow({
    width: 1200,
    height: 860,
    minWidth: 360,
    minHeight: 480,
    title: 'クイズひろば',
    autoHideMenuBar: true,
    webPreferences: {
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
    },
  });

  // 外部リンク（http/https）は既定のブラウザで開き、アプリ内では開かない
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
    createWindow();
  });

  app.on('window-all-closed', () => app.quit());
}
