namespace JavMetaLite.Core.Services;

public sealed class MetadataSourceTimeoutException : TimeoutException
{
    public MetadataSourceTimeoutException(
        string sourceName,
        string sourceDisplayName,
        TimeSpan timeout,
        Exception? innerException = null)
        : base($"资料来源 {sourceDisplayName} 在 {timeout.TotalSeconds:0.#} 秒内没有响应。", innerException)
    {
        SourceName = sourceName;
        SourceDisplayName = sourceDisplayName;
        Timeout = timeout;
    }

    public string SourceName { get; }

    public string SourceDisplayName { get; }

    public TimeSpan Timeout { get; }
}
