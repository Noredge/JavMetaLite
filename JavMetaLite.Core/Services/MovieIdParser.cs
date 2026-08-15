using System.Text.RegularExpressions;

namespace JavMetaLite.Core.Services;

public static partial class MovieIdParser
{
    [GeneratedRegex(@"(?i)(?<![a-z0-9])FC2[\s._-]*(?:PPV[\s._-]*)?(?<number>\d{5,8})(?!\d)")]
    private static partial Regex Fc2Pattern();

    [GeneratedRegex(@"(?i)(?<![a-z0-9])(?<prefix>[a-z]{2,12})[\s._-]*(?<number>\d{2,6})(?![a-z0-9])")]
    private static partial Regex StandardPattern();

    [GeneratedRegex(@"(?i)(?<!\d)(?<date>\d{6})[\s._-]+(?<number>\d{2,4})(?!\d)")]
    private static partial Regex DateNumberPattern();

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

            if (IgnoredPrefixes.Contains(prefix) || IsLikelyResolution(number))
            {
                continue;
            }

            return $"{prefix}-{number}";
        }

        var dateNumber = DateNumberPattern().Match(fileName);
        return dateNumber.Success
            ? $"{dateNumber.Groups["date"].Value}-{dateNumber.Groups["number"].Value}"
            : null;
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return TryExtract(value) ?? value.Trim().ToUpperInvariant();
    }

    private static bool IsLikelyResolution(string number) =>
        number is "2160" or "1080" or "720" or "480";

    private static readonly HashSet<string> IgnoredPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "H264", "H265", "HEVC", "AVC", "AAC", "XVID", "DIVX", "WEB", "HD", "FHD", "UHD"
    };
}
