using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UrlInsight.Core;
using UrlInsight.Core.Storage;

namespace UrlInsight.Mac.Platform;

/// <summary>
/// macOS のキーチェーン(ログインキーチェーン)に API キーを保存する(Windows 版の DPAPI の代わり)。
/// サービス名「URL Insight」、アカウント名はキーの種類(provider-anthropic など)。
/// 平文のファイルには決して保存しない(保存できない場合は例外)。
/// アプリを更新すると、macOS が「キーチェーンの使用を許可しますか」と確認する場合がある(「常に許可」を選ぶ)。
/// </summary>
internal sealed partial class KeychainSecretStore : ISecretStore
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const int errSecSuccess = 0;
    private const int errSecItemNotFound = -25300;
    private static readonly byte[] Service = Encoding.UTF8.GetBytes("URL Insight");

    [DllImport(Security)]
    private static extern int SecKeychainAddGenericPassword(IntPtr keychain, uint serviceNameLength, byte[] serviceName,
        uint accountNameLength, byte[] accountName, uint passwordLength, byte[] passwordData, IntPtr itemRef);

    [DllImport(Security)]
    private static extern int SecKeychainFindGenericPassword(IntPtr keychainOrArray, uint serviceNameLength, byte[] serviceName,
        uint accountNameLength, byte[] accountName, out uint passwordLength, out IntPtr passwordData, out IntPtr itemRef);

    [DllImport(Security)]
    private static extern int SecKeychainItemModifyAttributesAndData(IntPtr itemRef, IntPtr attrList, uint length, byte[] data);

    [DllImport(Security)]
    private static extern int SecKeychainItemDelete(IntPtr itemRef);

    [DllImport(Security)]
    private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

    [GeneratedRegex("^[a-z0-9_-]{1,64}$")]
    private static partial Regex NamePattern();

    private static byte[] Account(string name)
    {
        if (!NamePattern().IsMatch(name)) throw new ArgumentException("invalid secret name", nameof(name));
        return Encoding.UTF8.GetBytes(name);
    }

    public void Save(string name, string secret)
    {
        if (string.IsNullOrEmpty(secret)) throw new ArgumentException("empty secret", nameof(secret));
        var account = Account(name);
        var data = Encoding.UTF8.GetBytes(secret);
        try
        {
            int status = SecKeychainFindGenericPasswordNoData(IntPtr.Zero, (uint)Service.Length, Service, (uint)account.Length, account,
                IntPtr.Zero, IntPtr.Zero, out var item);
            if (status == errSecSuccess)
            {
                try { status = SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)data.Length, data); }
                finally { MacNative.CFRelease(item); }
            }
            else if (status == errSecItemNotFound)
            {
                status = SecKeychainAddGenericPassword(IntPtr.Zero, (uint)Service.Length, Service, (uint)account.Length, account,
                    (uint)data.Length, data, IntPtr.Zero);
            }
            if (status != errSecSuccess)
                throw new InsightException(ErrorCode.KeyStorageFailed, $"keychain write failed ({status})");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            throw new InsightException(ErrorCode.KeyStorageFailed, "keychain is not available", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(data);
        }
    }

    public string? TryGet(string name)
    {
        var account = Account(name);
        try
        {
            if (SecKeychainFindGenericPassword(IntPtr.Zero, (uint)Service.Length, Service, (uint)account.Length, account,
                    out var length, out var data, out var item) != errSecSuccess) return null;
            try
            {
                var bytes = new byte[length];
                Marshal.Copy(data, bytes, 0, (int)length);
                try { return Encoding.UTF8.GetString(bytes); }
                finally { CryptographicOperations.ZeroMemory(bytes); }
            }
            finally
            {
                SecKeychainItemFreeContent(IntPtr.Zero, data);
                MacNative.CFRelease(item);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    public void Delete(string name)
    {
        var account = Account(name);
        try
        {
            if (SecKeychainFindGenericPasswordNoData(IntPtr.Zero, (uint)Service.Length, Service, (uint)account.Length, account,
                    IntPtr.Zero, IntPtr.Zero, out var item) != errSecSuccess) return;
            try { SecKeychainItemDelete(item); }
            finally { MacNative.CFRelease(item); }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    public bool Exists(string name)
    {
        var account = Account(name);
        try
        {
            // パスワード本体は読まずに(passwordData に NULL を渡す)、項目があるかだけ調べる
            if (SecKeychainFindGenericPasswordNoData(IntPtr.Zero, (uint)Service.Length, Service, (uint)account.Length, account,
                    IntPtr.Zero, IntPtr.Zero, out var item) != errSecSuccess) return false;
            MacNative.CFRelease(item);
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport(Security, EntryPoint = "SecKeychainFindGenericPassword")]
    private static extern int SecKeychainFindGenericPasswordNoData(IntPtr keychainOrArray, uint serviceNameLength, byte[] serviceName,
        uint accountNameLength, byte[] accountName, IntPtr passwordLength, IntPtr passwordData, out IntPtr itemRef);
}
