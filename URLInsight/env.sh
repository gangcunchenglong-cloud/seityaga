# 開発用(任意): Microsoft 版 .NET 8 SDK を /opt/msdotnet-root に展開している場合だけ、それを使う。
# Linux から WPF をクロスビルドするには Microsoft.NET.Sdk.WindowsDesktop を含む Microsoft 版 SDK が必要。
if [ -z "${DOTNET_ROOT:-}" ] && [ -x /opt/msdotnet-root/usr/share/dotnet/dotnet ]; then
  export DOTNET_ROOT=/opt/msdotnet-root/usr/share/dotnet
  export PATH=$DOTNET_ROOT:$PATH
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
