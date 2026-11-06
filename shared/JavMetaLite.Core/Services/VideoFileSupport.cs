namespace JavMetaLite.Core.Services;

public enum VideoInputPathStatus
{
    Success,
    UnsupportedPath,
    FolderHasNoVideo,
    FolderHasMultipleVideos
}

public sealed record VideoInputPathResolution(
    VideoInputPathStatus Status,
    string? VideoPath = null)
{
    public bool Success => Status is VideoInputPathStatus.Success && VideoPath is not null;
}

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

    public static VideoInputPathResolution ResolveInputPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new VideoInputPathResolution(VideoInputPathStatus.UnsupportedPath);
        }

        if (File.Exists(path))
        {
            return HasSupportedExtension(path)
                ? new VideoInputPathResolution(
                    VideoInputPathStatus.Success,
                    Path.GetFullPath(path))
                : new VideoInputPathResolution(VideoInputPathStatus.UnsupportedPath);
        }

        if (!Directory.Exists(path))
        {
            return new VideoInputPathResolution(VideoInputPathStatus.UnsupportedPath);
        }

        var videos = Directory
            .EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly)
            .Where(HasSupportedExtension)
            .Take(2)
            .Select(Path.GetFullPath)
            .ToArray();
        return videos.Length switch
        {
            0 => new VideoInputPathResolution(VideoInputPathStatus.FolderHasNoVideo),
            1 => new VideoInputPathResolution(VideoInputPathStatus.Success, videos[0]),
            _ => new VideoInputPathResolution(VideoInputPathStatus.FolderHasMultipleVideos)
        };
    }
}
