namespace JavMetaLite.Core.Services;

public static class VideoFileSupport
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mkv", ".avi", ".wmv", ".mov", ".webm", ".ts", ".m2ts"
    };

    public const string OpenFileDialogFilter =
        "影片文件|*.mp4;*.m4v;*.mkv;*.avi;*.wmv;*.mov;*.webm;*.ts;*.m2ts|所有文件|*.*";

    public const string SupportedExtensionsDisplay =
        "MP4、M4V、MKV、AVI、WMV、MOV、WEBM、TS 或 M2TS";

    public static bool HasSupportedExtension(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Extensions.Contains(Path.GetExtension(path));

    public static bool IsSupportedExistingFile(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path) && HasSupportedExtension(path);
}
