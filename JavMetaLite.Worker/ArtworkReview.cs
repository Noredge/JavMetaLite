using System.Text.Json;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;

namespace JavMetaLite.Worker;

internal sealed partial class Workspace
{
    private static string[] SampleSources(Entry entry, string location)
    {
        var sources = entry.Job.SourceResults.Where(metadata => metadata.ScreenshotUrls.Contains(location))
            .Select(metadata => MetadataCandidateSource.FromMetadata(metadata).Name).Distinct(StringComparer.Ordinal).ToArray();
        return sources.Length > 0 ? sources : ["sources"];
    }

    private object SelectArtworkSource(JsonElement p)
    {
        lock (_gate)
        {
            var entry = GetJob(p); EnsureIdle(entry); RequireRevision(entry, p);
            var source = Text(p, "source");
            var local = source == "local";
            var candidate = entry.Job.ArtworkReview.Candidates.FirstOrDefault(c => local
                ? c.IsSidecarPair : string.Equals(c.Source.Name, source, StringComparison.Ordinal));
            var metadata = entry.Job.SourceResults.FirstOrDefault(m =>
                MetadataCandidateSource.FromMetadata(m).Name == source);
            if (local ? candidate is null && entry.Job.LocalExtrafanartPaths.Count == 0 : candidate is null && metadata is null)
                throw new RpcError(-32602, "ARTWORK_NOT_FOUND", "Choose a registered artwork source from this job.");
            // Validate the complete source before any mutation. A sample-only source never
            // invents a cover pair, and a source switch cannot leave hidden samples selected.
            var samples = local ? entry.Job.LocalExtrafanartPaths.ToArray()
                : (metadata?.ScreenshotUrls ?? []).Where(location => ArtworkLocationHelper.TryGetRemoteUri(location, out _)).Distinct().ToArray();
            if (candidate is not null) entry.Job.SelectArtworkSource(candidate.Source.Name);
            entry.Job.Metadata.ScreenshotUrls = samples;
            InvalidatePlans(entry.Id);
            return Snapshot(entry);
        }
    }

    private static string NormalizeReviewSource(string source) => source == "local-nfo" ? "local" : source;
    private static bool HasCurrentMovieId(Entry entry, MovieMetadata source) =>
        !string.IsNullOrWhiteSpace(MovieIdParser.Normalize(entry.Job.Metadata.Id)) &&
        string.Equals(MovieIdParser.Normalize(source.Id), MovieIdParser.Normalize(entry.Job.Metadata.Id), StringComparison.OrdinalIgnoreCase);
    private static bool IsCurrentOnlineSource(Entry entry, string source) => entry.Job.SourceResults.Any(metadata =>
        string.Equals(metadata.SourceName, source, StringComparison.OrdinalIgnoreCase) && HasCurrentMovieId(entry, metadata));
    private static string[] AvailableReviewSources(Entry entry) => new[] { "local", "libredmm", "r18dev" }
        .Where(source => (source == "local" || IsCurrentOnlineSource(entry, source)) &&
            entry.Job.MetadataReview.Sources.Any(snapshot => NormalizeReviewSource(snapshot.Source.Name) == source)).ToArray();

    private static string CurrentReviewSource(Entry entry)
    {
        var selectedArtwork = entry.Job.ArtworkReview.SelectedCandidate;
        var matches = AvailableReviewSources(entry).Where(source =>
        {
            var fieldSource = source == "local" ? "local-nfo" : source;
            var fields = FieldMap.Values.Where(field => entry.Job.MetadataReview.GetCandidates(field)
                .Any(candidate => candidate.Source.Name == fieldSource)).ToArray();
            var cover = entry.Job.ArtworkReview.Candidates.FirstOrDefault(candidate => source == "local"
                ? candidate.IsSidecarPair : candidate.Source.Name == source);
            // Missing fields/covers were intentionally retained. They do not
            // hide the source chosen for all of its available values.
            return fields.Length > 0 && fields.All(field => entry.Job.MetadataReview.GetSelectedCandidate(field)?.Source.Name == fieldSource) &&
                (cover is null || selectedArtwork?.Source.Name == cover.Source.Name);
        }).ToArray();
        return matches.Length == 1 ? matches[0] : "";
    }

    private object SelectReviewSource(JsonElement p)
    {
        lock (_gate)
        {
            var entry = GetJob(p); EnsureIdle(entry); RequireRevision(entry, p);
            var source = Text(p, "source");
            if (!AvailableReviewSources(entry).Contains(source))
                throw new RpcError(-32602, "INVALID_PARAMS", "Choose a local NFO or an already queried metadata source.");
            var fieldSource = source == "local" ? "local-nfo" : source;
            var fields = FieldMap.Values.Where(field => entry.Job.MetadataReview.GetCandidates(field)
                .Any(candidate => candidate.Source.Name == fieldSource)).ToArray();
            var cover = entry.Job.ArtworkReview.Candidates.FirstOrDefault(candidate => source == "local"
                ? candidate.IsSidecarPair : candidate.Source.Name == source);
            var metadata = entry.Job.SourceResults.FirstOrDefault(value => value.SourceName == fieldSource);
            var samples = source == "local" ? entry.Job.LocalExtrafanartPaths.ToArray()
                : (metadata?.ScreenshotUrls ?? []).Where(location => ArtworkLocationHelper.TryGetRemoteUri(location, out _)).Distinct().ToArray();
            // Validate every choice/capacity before mutation. Missing metadata fields
            // keep their current reviewed values; manual candidates remain recoverable.
            EnsureSnapshotCapacity(additionalBytes: 2L * fields.Sum(field =>
                System.Text.Encoding.UTF8.GetByteCount(entry.Job.MetadataReview.GetCandidates(field)
                    .First(candidate => candidate.Source.Name == fieldSource).Value)));
            foreach (var field in fields) entry.Job.MetadataReview.SelectCandidate(field, fieldSource);
            if (cover is not null) entry.Job.SelectArtworkSource(cover.Source.Name);
            entry.Job.Metadata.ScreenshotUrls = samples;
            var provenance = source == "local" ? entry.Job.LocalSourceMetadata : metadata;
            var sourceSnapshot = entry.Job.MetadataReview.Sources.First(snapshot => snapshot.Source.Name == fieldSource);
            entry.Job.Metadata.SourceName = fieldSource;
            entry.Job.Metadata.SourceDisplayName = sourceSnapshot.Source.DisplayName;
            entry.Job.Metadata.SourceUrl = provenance?.SourceUrl ?? "";
            entry.Job.Metadata.ContentId = provenance?.ContentId ?? "";
            InvalidatePlans(entry.Id);
            return Snapshot(entry);
        }
    }

    private static object CoverQuality(Entry entry)
    {
        var check = entry.Job.CoverResolution;
        return new { state = check?.IsMeasured == true ? check.IsLowResolution ? "lowResolution" : "ok" : "unknown",
            width = check?.IsMeasured == true ? (int?)check.Width : null,
            height = check?.IsMeasured == true ? (int?)check.Height : null,
            source = entry.Job.ArtworkReview.SelectedCandidate?.Source.Name,
            reason = check?.IsLowResolution == true ? "smallFullCover" : null };
    }

    private async Task<object> LoadReviewedArtworkPreview(Entry entry, ArtworkSource source, long qualityRevision, CancellationToken token)
    {
        var preview = await LoadArtworkPreview(source, token);
        token.ThrowIfCancellationRequested();
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            if (source.Kind == "fanart" && _jobs.GetValueOrDefault(entry.Id) == entry &&
                entry.Job.ArtworkReview.SelectedCandidate?.Source.Name == source.CandidateSource)
                entry.Job.TrySetCoverResolution(qualityRevision, new(preview.ResolvedLocation, preview.Width, preview.Height));
        }
        return preview;
    }
}
