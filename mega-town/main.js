const { app, BrowserWindow } = require('electron');

function createWindow() {
  const win = new BrowserWindow({
    width: 1200,
    height: 800,
    autoHideMenuBar: true, // メニューバーを隠す
    webPreferences: {
      nodeIntegration: false
    }
  });

  win.loadFile('index2.html'); // あなたのHTMLを読み込む
}

app.whenReady().then(createWindow);

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});