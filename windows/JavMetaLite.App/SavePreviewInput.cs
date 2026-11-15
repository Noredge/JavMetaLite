using System.IO;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;

namespace JavMetaLite.App;

internal sealed record SavePreviewInput(
    MovieJob Job,
    long ReviewRevision,
    IReadOnlyList<string> VideoPaths,
    MovieMetadata Metadata,
    MovieSaveConfiguration? Configuration,
    LocalSaveContext Context,
    LocalSidecarPaths? Sidecars,
    string? Issue)
{
    private string? FolderNfoOwnershipChangedMessage { get; init; }

    internal static SavePreviewInput Capture(MovieJob job, LocalSidecarPaths? discoveredSidecars = null)
    {
        var issue = job.VideoPath is null ? LocalizationService.Get("Error.SelectVideo")
            : job.LocalNfoSaveBlocked ? LocalizationService.Get("Error.LocalNfoBlocked")
            : job.SaveConfiguration is null ? LocalizationService.Get("Error.MissingJobSaveConfiguration")
            : null;
        var context = job.CreateLocalSaveContext() with { LocalExtrafanartPaths = job.LocalExtrafanartPaths.ToArray() };
        // Import can discover these paths even when it cannot read their contents.
        var sidecars = context.MetadataBundle?.Sidecars;
        if (discoveredSidecars is not null && job.VideoPaths.Contains(discoveredSidecars.VideoPath, FileSystemPathSemantics.PathComparer))
            sidecars ??= discoveredSidecars;
        return new(job, job.ReviewRevision, job.VideoPaths.ToArray(), job.CaptureMetadata(), job.SaveConfiguration,
            context, sidecars, issue)
        {
            FolderNfoOwnershipChangedMessage = context.MetadataBundle?.Sidecars.NfoPath is { } path
                ? LocalizationService.Get("Error.FolderNfoOwnershipChanged", path) : null
        };
    }

    internal IEnumerable<string> SourcePaths()
    {
        return VideoPaths
            .Concat(new[] { Sidecars?.NfoPath, Sidecars?.PosterPath, Sidecars?.FanartPath,
                Context.LocalArtwork?.LocalPosterPath, Context.LocalArtwork?.LocalFanartPath }.OfType<string>())
            .Concat(Sidecars?.ExtrafanartPaths ?? [])
            .Concat(Context.LocalExtrafanartPaths)
            .Where(Path.IsPathFullyQualified);
    }

    internal Task<SavePlan> PrepareAsync(CancellationToken token, IReadOnlyList<string>? orderedParts = null)
    {
        token.ThrowIfCancellationRequested();
        var configuration = Configuration!;
        var plan = orderedParts is null
            ? FileOrganizationService.BuildPlan(VideoPaths, Metadata, configuration.SaveOptions,
                configuration.OrganizationOptions, Context, token)
            : FileOrganizationService.BuildConfirmedMultipartPlan(orderedParts, Metadata, configuration.SaveOptions,
                configuration.OrganizationOptions, Context, token);
        return FileOrganizationService.BindPreviewTargetsAsync(plan, Metadata, token);
    }

    internal async Task<BatchSavePreparation> PrepareForBatchAsync(
        CancellationToken token, IReadOnlyList<string>? orderedParts = null)
    {
        if (Issue is not null) return new(null, Issue);
        try
        {
            var plan = await PrepareAsync(token, orderedParts).ConfigureAwait(false);
            return plan.HasBlockingConflicts
                ? new(null, string.Join(Environment.NewLine, plan.BlockingConflicts))
                : new(plan, null);
        }
        catch (Exception exception) when (!token.IsCancellationRequested)
        {
            AppLog.Warning($"批量保存预检失败 id={Metadata.Id} path={VideoPaths.FirstOrDefault()}", exception);
            return new(null, exception is FolderNfoOwnershipChangedException
                ? FolderNfoOwnershipChangedMessage ?? exception.Message : exception.Message);
        }
    }
}

internal sealed record BatchSavePreparation(SavePlan? Plan, string? Issue);
