; URL Insight / LinkLens インストーラー (NSIS 3)
; ユーザー単位インストール(管理者権限不要)。%LOCALAPPDATA%\Programs\URLInsight に配置。
; makensis -DVERSION=1.0.0 -DSRCDIR=<dist\URLInsight> -DOUTFILE=<出力exe> URLInsight.nsi

Unicode true
SetCompressor /SOLID lzma
RequestExecutionLevel user
ManifestDPIAware true

!ifndef VERSION
  !define VERSION "1.0.0"
!endif
!ifndef SRCDIR
  !error "SRCDIR を指定してください"
!endif
!ifndef OUTFILE
  !define OUTFILE "URLInsight-Setup-${VERSION}.exe"
!endif

!define APPNAME "URL Insight"
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\URLInsight"

!include "MUI2.nsh"
!include "FileFunc.nsh"

Name "${APPNAME}"
OutFile "${OUTFILE}"
InstallDir "$LOCALAPPDATA\Programs\URLInsight"
InstallDirRegKey HKCU "Software\URLInsight" "InstallDir"
BrandingText "${APPNAME} ${VERSION}"
VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "${APPNAME}"
VIAddVersionKey "FileDescription" "${APPNAME} セットアップ"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "LegalCopyright" "Copyright (c) 2026"

!define MUI_ICON "${SRCDIR}\..\..\src\UrlInsight.App\Assets\app.ico"
!define MUI_UNICON "${SRCDIR}\..\..\src\UrlInsight.App\Assets\app.ico"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\URLInsight.exe"
!define MUI_FINISHPAGE_RUN_TEXT "URL Insight を起動する"
!define MUI_FINISHPAGE_SHOWREADME "$INSTDIR\browser-extension"
!define MUI_FINISHPAGE_SHOWREADME_TEXT "Chrome拡張のフォルダを開く（拡張の読み込みに使います）"
!define MUI_FINISHPAGE_SHOWREADME_NOTCHECKED

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "${SRCDIR}\LICENSES.md"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Japanese"

Section "URL Insight" SecMain
  SectionIn RO
  ; 起動中なら終了してもらう
  nsExec::Exec 'taskkill /IM URLInsight.exe /F'
  nsExec::Exec 'taskkill /IM URLInsight.NativeHost.exe /F'
  Sleep 500

  SetOutPath "$INSTDIR"
  File "${SRCDIR}\URLInsight.exe"
  File "${SRCDIR}\URLInsight.NativeHost.exe"
  File "${SRCDIR}\README.md"
  File "${SRCDIR}\LICENSES.md"
  SetOutPath "$INSTDIR\docs"
  File /r "${SRCDIR}\docs\*.*"
  SetOutPath "$INSTDIR\browser-extension"
  File /r "${SRCDIR}\browser-extension\*.*"
  SetOutPath "$INSTDIR"

  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\URLInsight" "InstallDir" "$INSTDIR"

  ; Chrome へネイティブメッセージングホストを登録(HKCU)
  nsExec::ExecToLog '"$INSTDIR\URLInsight.exe" --register-native-host'

  CreateDirectory "$SMPROGRAMS\URL Insight"
  CreateShortcut "$SMPROGRAMS\URL Insight\URL Insight.lnk" "$INSTDIR\URLInsight.exe"
  CreateShortcut "$SMPROGRAMS\URL Insight\Chrome拡張フォルダ.lnk" "$INSTDIR\browser-extension"
  CreateShortcut "$SMPROGRAMS\URL Insight\アンインストール.lnk" "$INSTDIR\Uninstall.exe"

  ; 「アプリと機能」への登録
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayName" "${APPNAME}"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINSTKEY}" "Publisher" "URL Insight"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayIcon" "$INSTDIR\URLInsight.exe"
  WriteRegStr HKCU "${UNINSTKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTKEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKCU "${UNINSTKEY}" "EstimatedSize" "$0"
SectionEnd

Section "Uninstall"
  nsExec::Exec 'taskkill /IM URLInsight.exe /F'
  nsExec::Exec 'taskkill /IM URLInsight.NativeHost.exe /F'
  Sleep 500

  ; ネイティブホスト登録と自動起動を解除
  nsExec::ExecToLog '"$INSTDIR\URLInsight.exe" --unregister-native-host'

  MessageBox MB_YESNO|MB_ICONQUESTION "設定・キャッシュ・保存したAPIキー・ログ（$LOCALAPPDATA\URLInsight）も削除しますか？$\n「いいえ」を選ぶと残します（再インストール時に引き継がれます）。" /SD IDNO IDNO keepdata
    nsExec::ExecToLog '"$INSTDIR\URLInsight.exe" --purge-user-data'
    RMDir /r "$LOCALAPPDATA\URLInsight"
  keepdata:

  Delete "$INSTDIR\URLInsight.exe"
  Delete "$INSTDIR\URLInsight.NativeHost.exe"
  Delete "$INSTDIR\README.md"
  Delete "$INSTDIR\LICENSES.md"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir /r "$INSTDIR\docs"
  RMDir /r "$INSTDIR\browser-extension"
  RMDir "$INSTDIR"

  RMDir /r "$SMPROGRAMS\URL Insight"
  DeleteRegKey HKCU "${UNINSTKEY}"
  DeleteRegKey HKCU "Software\URLInsight"
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "URLInsight"
SectionEnd
