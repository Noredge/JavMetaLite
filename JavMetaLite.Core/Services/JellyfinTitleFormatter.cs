namespace JavMetaLite.Core.Services;

public static class JellyfinTitleFormatter
{
    public const string Separator = " · ";

    public static string Format(string? movieId, string? title, bool includeId)
    {
        var normalizedId = MovieIdParser.Normalize(movieId);
        var normalizedTitle = (title ?? string.Empty).Trim();
        if (normalizedId.Length == 0)
        {
            return normalizedTitle;
        }

        var cleanTitle = RemoveGeneratedPrefix(normalizedId, normalizedTitle);
        if (!includeId)
        {
            return cleanTitle.Length == 0 ? normalizedTitle : cleanTitle;
        }

        return cleanTitle.Length == 0 || cleanTitle.Equals(normalizedId, StringComparison.OrdinalIgnoreCase)
            ? normalizedId
            : normalizedId + Separator + cleanTitle;
    }

    private static string RemoveGeneratedPrefix(string normalizedId, string title)
    {
        var prefix = normalizedId + Separator;
        return title.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? title[prefix.Length..].TrimStart()
            : title;
    }
}
