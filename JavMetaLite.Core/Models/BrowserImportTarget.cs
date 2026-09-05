namespace JavMetaLite.Core.Models;

public sealed record BrowserImportTarget(
    string SourceName,
    string DisplayName,
    string InitialUrl,
    bool HasExistingResult,
    string RequestedMovieId = "");
