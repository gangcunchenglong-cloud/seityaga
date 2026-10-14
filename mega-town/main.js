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

// タスクバーにピン留めしたアイコンと、起動中のウィンドウを同じアプリとしてまとめる(package.json の appId と同じ値)
app.setAppUserModelId('com.gangcunchenglong.megatown');

app.whenReady().then(createWindow);

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});