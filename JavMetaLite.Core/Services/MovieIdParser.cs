using System.Text.RegularExpressions;

namespace JavMetaLite.Core.Services;

public static partial class MovieIdParser
{
    [GeneratedRegex(@"(?i)(?<![a-z0-9])FC2[\s._-]*(?:PPV[\s._-]*)?(?<number>\d{5,8})(?!\d)")]
    private static partial Regex Fc2Pattern();

    [GeneratedRegex(@"(?i)(?<![a-z0-9])(?<prefix>[a-z]{2,12})[\s._-]*(?<number>\d{2,6})(?![a-z0-9])")]
    private static partial Regex StandardPattern();

    public static string? TryExtract(string? fileNameOrPath)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrPath))
        {
            return null;
        }

        var fileName = Path.GetFileNameWithoutExtension(fileNameOrPath)
            .Replace('—', '-')
            .Replace('–', '-')
            .Replace('－', '-');

        var fc2 = Fc2Pattern().Match(fileName);
        if (fc2.Success)
        {
            return $"FC2-PPV-{fc2.Groups["number"].Value}";
        }

        foreach (Match match in StandardPattern().Matches(fileName))
        {
            var prefix = match.Groups["prefix"].Value.ToUpperInvariant();
            var number = match.Groups["number"].Value;

            if (IgnoredPrefixes.Contains(prefix))
            {
                continue;
            }

            return $"{prefix}-{number}";
        }

        return null;
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return TryExtract(value) ?? value.Trim().ToUpperInvariant();
    }

    private static readonly HashSet<string> IgnoredPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "H264", "H265", "HEVC", "AVC", "AAC", "XVID", "DIVX", "WEB", "HD", "FHD", "UHD"
    };
}
