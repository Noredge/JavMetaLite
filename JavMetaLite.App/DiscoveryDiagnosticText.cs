using JavMetaLite.Core.Services;

namespace JavMetaLite.App;

internal static class DiscoveryDiagnosticText
{
    public static string Format(MovieFileDiscoveryDiagnostic diagnostic)
    {
        var summary = LocalizationService.Get(diagnostic.Kind is MovieFileDiscoveryDiagnosticKind.PathUnavailable
            ? "Discovery.PathUnavailable" : "Discovery.ReadFailed");
        // Known application diagnostics are localized. OS/provider details remain verbatim.
        return diagnostic.Kind is MovieFileDiscoveryDiagnosticKind.PathUnavailable
            ? $"{diagnostic.Path}: {summary}"
            : $"{diagnostic.Path}: {summary}{Environment.NewLine}{diagnostic.Message}";
    }
}
