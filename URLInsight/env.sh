# 開発用: Microsoft版 .NET 8 SDK を使う (Linux から WPF をクロスビルドするため)
export DOTNET_ROOT=${DOTNET_ROOT:-/opt/msdotnet-root/usr/share/dotnet}
export PATH=$DOTNET_ROOT:$PATH
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
