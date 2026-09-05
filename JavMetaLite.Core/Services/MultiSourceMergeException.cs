namespace JavMetaLite.Core.Services;

public sealed class MultiSourceMergeException : Exception
{
    public MultiSourceMergeException(
        string id,
        IReadOnlyList<MetadataSourceSearchAttempt> attempts,
        InvalidDataException innerException)
        : base($"影片 {id} 的多来源结果无法安全合并：{innerException.Message}", innerException)
    {
        Id = id;
        Attempts = attempts;
    }

    public string Id { get; }

    public IReadOnlyList<MetadataSourceSearchAttempt> Attempts { get; }
}
