namespace JavMetaLite.Core.Services;

public sealed class MultiSourceSearchException : Exception
{
    public MultiSourceSearchException(
        string id,
        IReadOnlyList<MetadataSourceSearchAttempt> attempts)
        : base(BuildMessage(id, attempts), new AggregateException(
            attempts.Where(attempt => attempt.Error is not null).Select(attempt => attempt.Error!)))
    {
        Attempts = attempts;
    }

    public IReadOnlyList<MetadataSourceSearchAttempt> Attempts { get; }

    private static string BuildMessage(string id, IReadOnlyList<MetadataSourceSearchAttempt> attempts)
    {
        var failures = string.Join("；", attempts.Select(attempt =>
            $"{attempt.SourceDisplayName}：{attempt.Error?.Message ?? "没有返回资料"}"));
        return $"多来源搜索均失败（{id}）：{failures}";
    }
}
