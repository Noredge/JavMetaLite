using System.IO;
using JavMetaLite.Core.Services;

namespace JavMetaLite.App;

internal sealed record MovieQueueImportPlan(IReadOnlyList<MovieFileSet> FileSets, int DuplicatePathCount);
internal sealed record MovieQueueLoadItem(MovieFileSet FileSet, MovieJob? Job, MovieJobLoadResult? Result, Exception? Error);

internal static class MovieQueueImport
{
    public static Task<MovieQueueImportPlan> PrepareAsync(string[] paths, string[] existingPaths, CancellationToken token) =>
        Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            var existing = existingPaths.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var sets = MovieFileSet.Group(paths);
            token.ThrowIfCancellationRequested();
            var duplicates = sets.SelectMany(set => set.VideoPaths).Distinct(StringComparer.OrdinalIgnoreCase).Count(existing.Contains);
            return new MovieQueueImportPlan(sets, duplicates);
        }, token);

    // Only NEW, unattached jobs cross this boundary. No UI-bound job is mutated on a worker.
    public static Task<(MovieJob Job, MovieJobLoadResult Result)> LoadNewAsync(MovieFileSet fileSet, CancellationToken token) =>
        Task.Run(() => LoadNewCoreAsync(fileSet, token), token);

    private static async Task<(MovieJob Job, MovieJobLoadResult Result)> LoadNewCoreAsync(MovieFileSet fileSet, CancellationToken token)
    {
        var job = new MovieJob();
        try
        {
            var result = await MovieJobLoader.LoadAsync(job, fileSet.VideoPaths, token);
            token.ThrowIfCancellationRequested();
            return (job, result);
        }
        catch { job.Dispose(); throw; }
    }

    public static Task<IReadOnlyList<MovieQueueLoadItem>> LoadBatchAsync(MovieFileSet[] fileSets, CancellationToken token) =>
        Task.Run<IReadOnlyList<MovieQueueLoadItem>>(async () =>
        {
            var loaded = new List<MovieQueueLoadItem>();
            try
            {
                foreach (var fileSet in fileSets)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var item = await LoadNewCoreAsync(fileSet, token);
                        loaded.Add(new MovieQueueLoadItem(fileSet, item.Job, item.Result, null));
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception error) { loaded.Add(new MovieQueueLoadItem(fileSet, null, null, error)); }
                }
                token.ThrowIfCancellationRequested();
                return loaded;
            }
            catch
            {
                foreach (var item in loaded) item.Job?.Dispose();
                throw;
            }
        }, token);
}
