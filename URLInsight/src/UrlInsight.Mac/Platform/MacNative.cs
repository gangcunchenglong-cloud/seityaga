using System.Runtime.InteropServices;

namespace UrlInsight.Mac.Platform;

/// <summary>
/// macOS の機能の呼び出し(CoreFoundation・アクセシビリティ(AX)・CoreGraphics)。
/// AX は読み上げソフト(VoiceOver)などが使う仕組みで、カーソル位置の画面要素(リンクなど)を調べるのに使う。
/// 使うには「システム設定 → プライバシーとセキュリティ → アクセシビリティ」で URL Insight を許可する必要がある。
/// Copy/Create で受け取った CF オブジェクトは CFRelease で解放する。
/// </summary>
internal static class MacNative
{
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string ApplicationServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    public const int AXErrorSuccess = 0;
    private const int kAXValueCGPointType = 1;
    private const int kAXValueCFRangeType = 4;

    [StructLayout(LayoutKind.Sequential)]
    public struct CGPoint
    {
        public double X;
        public double Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CFRange
    {
        public nint Location;
        public nint Length;
    }

    // ---------- CoreFoundation ----------

    [DllImport(CoreFoundation)]
    public static extern void CFRelease(IntPtr cf);

    [DllImport(CoreFoundation)]
    private static extern nuint CFGetTypeID(IntPtr cf);

    [DllImport(CoreFoundation)]
    private static extern nuint CFStringGetTypeID();

    [DllImport(CoreFoundation)]
    private static extern nuint CFURLGetTypeID();

    [DllImport(CoreFoundation)]
    private static extern nuint CFBooleanGetTypeID();

    [DllImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool CFBooleanGetValue(IntPtr boolean);

    [DllImport(CoreFoundation, CharSet = CharSet.Unicode)]
    private static extern IntPtr CFStringCreateWithCharacters(IntPtr alloc, string chars, nint numChars);

    [DllImport(CoreFoundation)]
    private static extern nint CFStringGetLength(IntPtr str);

    // UTF-16 のまま受け取る(CharSet を指定しないと char[] が ANSI として変換され、日本語が壊れる)
    [DllImport(CoreFoundation, CharSet = CharSet.Unicode)]
    private static extern void CFStringGetCharacters(IntPtr str, CFRange range, [Out] char[] buffer);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFURLGetString(IntPtr url);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDictionaryCreate(IntPtr alloc, IntPtr[] keys, IntPtr[] values, nint count, IntPtr keyCallBacks, IntPtr valueCallBacks);

    // ---------- アクセシビリティ(AX) ----------

    [DllImport(ApplicationServices)]
    public static extern IntPtr AXUIElementCreateSystemWide();

    [DllImport(ApplicationServices)]
    public static extern IntPtr AXUIElementCreateApplication(int pid);

    [DllImport(ApplicationServices)]
    public static extern int AXUIElementCopyElementAtPosition(IntPtr application, float x, float y, out IntPtr element);

    [DllImport(ApplicationServices)]
    private static extern int AXUIElementCopyAttributeValue(IntPtr element, IntPtr attribute, out IntPtr value);

    [DllImport(ApplicationServices)]
    private static extern int AXUIElementCopyParameterizedAttributeValue(IntPtr element, IntPtr attribute, IntPtr parameter, out IntPtr value);

    [DllImport(ApplicationServices)]
    private static extern int AXUIElementSetAttributeValue(IntPtr element, IntPtr attribute, IntPtr value);

    [DllImport(ApplicationServices)]
    public static extern int AXUIElementGetPid(IntPtr element, out int pid);

    [DllImport(ApplicationServices)]
    public static extern int AXUIElementSetMessagingTimeout(IntPtr element, float timeoutInSeconds);

    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool AXIsProcessTrusted();

    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool AXIsProcessTrustedWithOptions(IntPtr options);

    [DllImport(ApplicationServices)]
    private static extern IntPtr AXValueCreate(int type, ref CGPoint value);

    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool AXValueGetValue(IntPtr value, int type, out CFRange range);

    // ---------- CoreGraphics ----------

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGEventCreate(IntPtr source);

    [DllImport(CoreGraphics)]
    private static extern CGPoint CGEventGetLocation(IntPtr evt);

    /// <summary>カーソルの位置(ポイント単位。メインの画面の左上が原点)。</summary>
    public static CGPoint CursorPosition()
    {
        var e = CGEventCreate(IntPtr.Zero);
        if (e == IntPtr.Zero) return default;
        try { return CGEventGetLocation(e); }
        finally { CFRelease(e); }
    }

    // ---------- 補助 ----------

    private static readonly Dictionary<string, IntPtr> StringCache = new();

    /// <summary>属性名などの固定の CFString(アプリ終了まで解放しない)。</summary>
    public static IntPtr CFStr(string s)
    {
        lock (StringCache)
        {
            if (StringCache.TryGetValue(s, out var p)) return p;
            p = CFStringCreateWithCharacters(IntPtr.Zero, s, s.Length);
            StringCache[s] = p;
            return p;
        }
    }

    public static string? ToManagedString(IntPtr cfString)
    {
        if (cfString == IntPtr.Zero || CFGetTypeID(cfString) != CFStringGetTypeID()) return null;
        var length = CFStringGetLength(cfString);
        if (length <= 0) return string.Empty;
        var buffer = new char[length];
        CFStringGetCharacters(cfString, new CFRange { Location = 0, Length = length }, buffer);
        return new string(buffer);
    }

    /// <summary>要素の属性を文字列として読む(文字列・URL の属性。読めなければ null)。</summary>
    public static string? GetString(IntPtr element, string attribute)
    {
        if (AXUIElementCopyAttributeValue(element, CFStr(attribute), out var value) != AXErrorSuccess || value == IntPtr.Zero) return null;
        try
        {
            var type = CFGetTypeID(value);
            if (type == CFURLGetTypeID()) return ToManagedString(CFURLGetString(value));
            return ToManagedString(value);
        }
        finally { CFRelease(value); }
    }

    public static bool GetBool(IntPtr element, string attribute)
    {
        if (AXUIElementCopyAttributeValue(element, CFStr(attribute), out var value) != AXErrorSuccess || value == IntPtr.Zero) return false;
        try { return CFGetTypeID(value) == CFBooleanGetTypeID() && CFBooleanGetValue(value); }
        finally { CFRelease(value); }
    }

    /// <summary>要素を指す属性(AXParent など)を読む。受け取った要素は呼び出し側で CFRelease する。</summary>
    public static IntPtr GetElement(IntPtr element, string attribute)
        => AXUIElementCopyAttributeValue(element, CFStr(attribute), out var value) == AXErrorSuccess ? value : IntPtr.Zero;

    public static bool SetBool(IntPtr element, string attribute, bool on)
        => AXUIElementSetAttributeValue(element, CFStr(attribute), on ? KCFBooleanTrue : KCFBooleanFalse) == AXErrorSuccess;

    /// <summary>画面上の点にある文字の位置(文字列中の何文字目か)。対応していない要素は null。</summary>
    public static int? CharIndexAt(IntPtr element, CGPoint point)
    {
        var p = AXValueCreate(kAXValueCGPointType, ref point);
        if (p == IntPtr.Zero) return null;
        try
        {
            if (AXUIElementCopyParameterizedAttributeValue(element, CFStr("AXRangeForPosition"), p, out var value) != AXErrorSuccess
                || value == IntPtr.Zero) return null;
            try { return AXValueGetValue(value, kAXValueCFRangeType, out var range) ? (int)range.Location : null; }
            finally { CFRelease(value); }
        }
        finally { CFRelease(p); }
    }

    // ---------- 定数(フレームワーク内の大域変数) ----------

    private static readonly Lazy<IntPtr> CfHandle = new(() => NativeLibrary.Load(CoreFoundation));
    private static readonly Lazy<IntPtr> AsHandle = new(() => NativeLibrary.Load(ApplicationServices));

    private static IntPtr ReadGlobal(IntPtr lib, string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(lib, name));

    private static IntPtr KCFBooleanTrue => ReadGlobal(CfHandle.Value, "kCFBooleanTrue");
    private static IntPtr KCFBooleanFalse => ReadGlobal(CfHandle.Value, "kCFBooleanFalse");

    /// <summary>
    /// アクセシビリティの許可があるか調べる。prompt が true なら、許可が無いときに macOS の案内(システム設定を開くボタン付き)を出し、
    /// 「アクセシビリティ」の一覧に URL Insight を追加させる。
    /// </summary>
    public static bool IsAccessibilityTrusted(bool prompt)
    {
        if (!prompt) return AXIsProcessTrusted();
        var key = ReadGlobal(AsHandle.Value, "kAXTrustedCheckOptionPrompt");
        var keyCallBacks = NativeLibrary.GetExport(CfHandle.Value, "kCFTypeDictionaryKeyCallBacks");
        var valueCallBacks = NativeLibrary.GetExport(CfHandle.Value, "kCFTypeDictionaryValueCallBacks");
        var dict = CFDictionaryCreate(IntPtr.Zero, new[] { key }, new[] { KCFBooleanTrue }, 1, keyCallBacks, valueCallBacks);
        try { return AXIsProcessTrustedWithOptions(dict); }
        finally { if (dict != IntPtr.Zero) CFRelease(dict); }
    }
}
