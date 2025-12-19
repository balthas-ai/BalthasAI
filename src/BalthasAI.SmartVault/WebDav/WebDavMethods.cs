namespace BalthasAI.SmartVault.WebDav;

/// <summary>
/// WebDAV HTTP 메서드 상수
/// </summary>
public static class WebDavMethods
{
    public const string PropFind = "PROPFIND";
    public const string PropPatch = "PROPPATCH";
    public const string MkCol = "MKCOL";
    public const string Copy = "COPY";
    public const string Move = "MOVE";
    public const string Lock = "LOCK";
    public const string Unlock = "UNLOCK";

    /// <summary>
    /// 모든 WebDAV 전용 메서드 목록
    /// </summary>
    public static readonly string[] AllMethods =
    [
        PropFind,
        PropPatch,
        MkCol,
        Copy,
        Move,
        Lock,
        Unlock
    ];
}
