namespace JavMetaLite.Core.Services;

public enum StartupVideoRequestKind
{
    None,
    Video,
    Invalid
}

public sealed record StartupVideoRequest(
    StartupVideoRequestKind Kind,
    string? VideoPath = null,
    string? ErrorMessage = null)
{
    public static StartupVideoRequest None { get; } = new(StartupVideoRequestKind.None);

    public static StartupVideoRequest OpenVideo(string path) =>
        new(StartupVideoRequestKind.Video, path);

    public static StartupVideoRequest Invalid(string message) =>
        new(StartupVideoRequestKind.Invalid, ErrorMessage: message);
}

public static class StartupVideoRequestResolver
{
    public static StartupVideoRequest Resolve(IReadOnlyList<string>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return StartupVideoRequest.None;
        }

        if (arguments.Count != 1)
        {
            return StartupVideoRequest.Invalid(
                "启动时一次只能打开一个影片文件。请只传入一个影片路径。");
        }

        if (string.IsNullOrWhiteSpace(arguments[0]))
        {
            return StartupVideoRequest.Invalid("启动参数中的影片路径为空。");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(arguments[0]);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return StartupVideoRequest.Invalid($"启动参数中的影片路径无效：{exception.Message}");
        }

        if (Directory.Exists(fullPath))
        {
            return StartupVideoRequest.Invalid("启动参数指向文件夹；请选择一个影片文件。");
        }

        if (!File.Exists(fullPath))
        {
            return StartupVideoRequest.Invalid($"启动参数指向的影片不存在：{fullPath}");
        }

        if (!VideoFileSupport.HasSupportedExtension(fullPath))
        {
            return StartupVideoRequest.Invalid(
                $"不支持该影片格式。支持：{VideoFileSupport.SupportedExtensionsDisplay}。");
        }

        return StartupVideoRequest.OpenVideo(fullPath);
    }
}
