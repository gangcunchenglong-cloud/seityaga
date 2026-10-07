using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace UrlInsight.Core.Storage;

public interface ISecretStore
{
    /// <summary>保存。暗号化できない場合は例外(<see cref="ErrorCode.KeyStorageFailed"/>)。平文には決してフォールバックしない。</summary>
    void Save(string name, string secret);
    string? TryGet(string name);
    void Delete(string name);
    bool Exists(string name);
}

/// <summary>
/// Windows DPAPI(CurrentUser スコープ)でAPIキーを暗号化して保存する。
/// 暗号化データのみをユーザープロファイル配下に置く。DPAPI が使えない環境では保存を拒否する。
/// </summary>
public sealed partial class DpapiSecretStore : ISecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("URLInsight.LinkLens.secret.v1");
    private readonly string _dir;

    public DpapiSecretStore(string directory) => _dir = directory;

    [GeneratedRegex("^[a-z0-9_-]{1,64}$")]
    private static partial Regex NamePattern();

    private string PathFor(string name)
    {
        if (!NamePattern().IsMatch(name)) throw new ArgumentException("invalid secret name", nameof(name));
        return Path.Combine(_dir, name + ".bin");
    }

    public void Save(string name, string secret)
    {
        var path = PathFor(name);
        if (string.IsNullOrEmpty(secret)) throw new ArgumentException("empty secret", nameof(secret));
        if (!OperatingSystem.IsWindows())
            throw new InsightException(ErrorCode.KeyStorageFailed, "DPAPI is not available on this OS");

        byte[] protectedBytes;
        var plain = Encoding.UTF8.GetBytes(secret);
        try
        {
            protectedBytes = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException ex)
        {
            throw new InsightException(ErrorCode.KeyStorageFailed, "DPAPI protect failed", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }

        try
        {
            Directory.CreateDirectory(_dir);
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, protectedBytes);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InsightException(ErrorCode.KeyStorageFailed, "write failed", ex);
        }
    }

    public string? TryGet(string name)
    {
        var path = PathFor(name);
        if (!File.Exists(path) || !OperatingSystem.IsWindows()) return null;
        try
        {
            var data = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(data); }
            finally { CryptographicOperations.ZeroMemory(data); }
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            // 破損・別ユーザーのデータ: 読めないものとして扱う(平文の推測はしない)
            return null;
        }
    }

    public void Delete(string name)
    {
        var path = PathFor(name);
        if (File.Exists(path)) File.Delete(path);
    }

    public bool Exists(string name) => File.Exists(PathFor(name));
}

/// <summary>保存に失敗した場合に、ユーザーが明示的に選んだときだけ使う「このセッションのみ」の保管。</summary>
public sealed class SessionSecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _values = new();
    public void Save(string name, string secret) { lock (_values) _values[name] = secret; }
    public string? TryGet(string name) { lock (_values) return _values.TryGetValue(name, out var v) ? v : null; }
    public void Delete(string name) { lock (_values) _values.Remove(name); }
    public bool Exists(string name) { lock (_values) return _values.ContainsKey(name); }
}

/// <summary>永続ストアを優先し、なければセッションストアを参照する。</summary>
public sealed class LayeredSecretStore : ISecretStore
{
    public LayeredSecretStore(ISecretStore persistent, SessionSecretStore session)
    {
        Persistent = persistent;
        Session = session;
    }

    public ISecretStore Persistent { get; }
    public SessionSecretStore Session { get; }

    public void Save(string name, string secret)
    {
        Persistent.Save(name, secret);
        Session.Delete(name);
    }

    public string? TryGet(string name) => Persistent.TryGet(name) ?? Session.TryGet(name);

    public void Delete(string name)
    {
        Persistent.Delete(name);
        Session.Delete(name);
    }

    public bool Exists(string name) => Persistent.Exists(name) || Session.Exists(name);

    public static string ProviderKeyName(string providerId) => "provider-" + providerId;
    public const string YouTubeKeyName = "youtube-data-api";
}
