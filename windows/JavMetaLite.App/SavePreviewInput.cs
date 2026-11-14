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
    string? Issue)
{
    internal static SavePreviewInput Capture(MovieJob job)
    {
        var issue = job.VideoPath is null ? LocalizationService.Get("Error.SelectVideo")
            : job.LocalNfoSaveBlocked ? LocalizationService.Get("Error.LocalNfoBlocked")
            : job.SaveConfiguration is null ? LocalizationService.Get("Error.MissingJobSaveConfiguration")
            : null;
        return new(job, job.ReviewRevision, job.VideoPaths.ToArray(), job.CaptureMetadata(), job.SaveConfiguration,
            job.CreateLocalSaveContext() with { LocalExtrafanartPaths = job.LocalExtrafanartPaths.ToArray() }, issue);
    }

    internal IEnumerable<string> SourcePaths()
    {
        var sidecars = Context.MetadataBundle?.Sidecars;
        return VideoPaths
            .Concat(new[] { sidecars?.NfoPath, sidecars?.PosterPath, sidecars?.FanartPath,
                Context.LocalArtwork?.LocalPosterPath, Context.LocalArtwork?.LocalFanartPath }.OfType<string>())
            .Concat(sidecars?.ExtrafanartPaths ?? [])
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
            return new(null, exception.Message);
        }
    }
}

internal sealed record BatchSavePreparation(SavePlan? Plan, string? Issue);
