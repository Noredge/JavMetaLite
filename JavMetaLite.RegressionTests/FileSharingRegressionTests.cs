using System.Diagnostics;
using System.IO;
using System.Net.Http;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;

namespace JavMetaLite.RegressionTests;

internal static class FileSharingRegressionTests
{
    public static IReadOnlyList<RegressionTestCase> All { get; } =
    [
        new("sharing", "仅共享冲突有限重试，取消与其它错误不重试", TestPolicy),
        new("sharing", "单片及多分段提交遇短暂占用后成功", TestTransientCommit),
        new("sharing", "新封套被占用后取消，等待释放并完整回滚", () => TestLateLock(false)),
        new("sharing", "新封套持续被占用，回滚失败保留旧备份", () => TestLateLock(true))
    ];

    private static async Task TestPolicy()
    {
        using var workspace = new TestWorkspace("sharing-policy");
        foreach (var code in new[] { 0x20, 0x21 })
        {
            var calls = 0;
            await FileSharingRetry.RunAsync(() =>
            {
                if (++calls < 2) throw new IOException("busy", unchecked((int)0x80070000) | code);
            }, "test", "synthetic");
            AssertEx.Equal(2, calls);
        }
        var attempts = 0;
        await AssertEx.ThrowsAsync<IOException>(() => FileSharingRetry.RunAsync(() =>
        {
            attempts++;
            throw new IOException("busy", unchecked((int)0x80070020));
        }, "test", "synthetic"), "A permanent lock did not stop.");
        AssertEx.Equal(4, attempts, "Sharing retry exceeded its bound.");
        foreach (var exception in new Exception[]
        {
            new UnauthorizedAccessException("denied"),
            new IOException("access denied", unchecked((int)0x80070005)),
            new IOException("file exists", unchecked((int)0x80070050)),
            new IOException("already exists", unchecked((int)0x800700B7)),
            new IOException("disk full", unchecked((int)0x80070070)),
            new IOException("generic IO error")
        })
        {
            var calls = 0;
            var actual = await AssertEx.ThrowsAsync<Exception>(() => FileSharingRetry.RunAsync(() =>
            {
                calls++;
                throw exception;
            }, "test", "synthetic"), "A non-sharing error was swallowed.");
            AssertEx.True(ReferenceEquals(exception, actual), "Retry replaced the original exception.");
            AssertEx.Equal(1, calls, "A non-sharing error was retried.");
        }
        using var cancel = new CancellationTokenSource();
        var canceledCalls = 0;
        var pending = FileSharingRetry.RunAsync(() =>
        {
            canceledCalls++;
            throw new IOException("busy", unchecked((int)0x80070020));
        }, "test", "synthetic", cancel.Token);
        AssertEx.False(pending.IsCompleted, "Retry blocked instead of yielding asynchronously.");
        cancel.Cancel();
        await AssertEx.ThrowsAsync<OperationCanceledException>(() => pending, "Cancel was ignored during retry.");
        AssertEx.Equal(1, canceledCalls);
        var source = workspace.WriteFile("source.jpg", [1, 2, 3]);
        var target = workspace.WriteFile("target.jpg", [4, 5, 6]);
        var targetHash = AssertEx.Sha256(target);
        await AssertEx.ThrowsAsync<IOException>(() => FileSharingRetry.MoveAsync(source, target), "Move overwrote an existing target.");
        AssertEx.Equal(targetHash, AssertEx.Sha256(target));
        AssertEx.FileExists(source);
    }

    private static async Task TestTransientCommit()
    {
        foreach (var multipart in new[] { false, true })
        {
            using var fixture = new Fixture(multipart);
            var reachedCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var fileLock = new FileStream(fixture.Fanart, FileMode.Open, FileAccess.Read, FileShare.None);
            Task<OrganizedSaveResult>? execution = null;
            try
            {
                execution = fixture.Organizer.ExecuteAsync(fixture.Plan, fixture.Metadata, true, default,
                    new InlineProgress<FileTransactionProgress>(update =>
                    {
                        if (update.Stage == FileTransactionStage.Committing) reachedCommit.TrySetResult();
                    }));
                await reachedCommit.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Task.Delay(180);
                AssertEx.False(execution.IsCompleted, "The locked commit neither yielded nor retried.");
            }
            finally { fileLock.Dispose(); }
            var result = await execution!;
            AssertEx.True(result.Outputs.FanartPath is not null, "Retried commit omitted fanart.");
            AssertEx.True(new FileInfo(fixture.Fanart).Length > 3, "Old fanart was not replaced.");
            fixture.AssertVideosUnchanged();
            fixture.Workspace.AssertNoTemporaryArtifacts();
        }
    }

    private static async Task TestLateLock(bool permanent)
    {
        foreach (var multipart in new[] { false, true })
        {
            using var fixture = new Fixture(multipart);
            using var cancel = new CancellationTokenSource();
            FileStream? stagedLock = null;
            FileStream? committedLock = null;
            string? stagedSample = null;
            Task<OrganizedSaveResult>? execution = null;
            try
            {
                execution = fixture.Organizer.ExecuteAsync(fixture.Plan, fixture.Metadata, true, cancel.Token,
                    new InlineProgress<FileTransactionProgress>(update =>
                    {
                        if (update.Stage != FileTransactionStage.Committing) return;
                        var stage = Directory.EnumerateDirectories(fixture.SourceDirectory, ".JavMetaLite-*.tmp").Single();
                        var sample = Directory.EnumerateFiles(stage, "fanart1.jpg", SearchOption.AllDirectories).Single();
                        stagedSample = sample;
                        // Fanart is committed first. Blocking the following sample gives the
                        // observer a deterministic window to lock the newly committed fanart.
                        stagedLock = new FileStream(sample, FileMode.Open, FileAccess.Read, FileShare.None);
                    }));
                var timer = Stopwatch.StartNew();
                while (stagedSample is null || !ReadTestLog().Contains(
                    $"文件被占用，有限重试 operation=move path={stagedSample} ->", StringComparison.Ordinal))
                {
                    if (execution.IsCompleted) await execution;
                    if (timer.Elapsed > TimeSpan.FromSeconds(5)) throw new InvalidOperationException("Sample commit did not reach its retry boundary.");
                    await Task.Delay(5);
                }
                // File visibility alone is too early: the moving thread may still be
                // returning from the OS call. The following sample retry proves fanart
                // was committed AND recorded before this test locks/cancels it.
                committedLock = new FileStream(fixture.Fanart, FileMode.Open, FileAccess.Read, FileShare.None);
                if (!permanent)
                {
                    cancel.Cancel();
                    stagedLock!.Dispose();
                    stagedLock = null;
                    await Task.Delay(180);
                    AssertEx.False(execution.IsCompleted, "Recovery did not wait for the new fanart lock. " +
                        execution.Exception + Environment.NewLine + ReadTestLog());
                    committedLock.Dispose();
                    committedLock = null;
                    await AssertEx.ThrowsAsync<OperationCanceledException>(() => execution, "Cancellation was not propagated after recovery.");
                    AssertEx.Equal(fixture.OldFanartHash, AssertEx.Sha256(fixture.Fanart));
                    fixture.Workspace.AssertNoTemporaryArtifacts();
                }
                else
                {
                    var error = await AssertEx.ThrowsAsync<IOException>(() => execution, "Persistent lock was hidden.");
                    AssertEx.True(error.Message.Contains("恢复不完整", StringComparison.Ordinal), "Incomplete recovery was not reported.");
                    var recovery = Directory.EnumerateDirectories(fixture.SourceDirectory, ".JavMetaLite-*.tmp").Single();
                    var backup = Path.Combine(recovery, "backup", "existing", Path.GetFileName(fixture.Fanart));
                    AssertEx.Equal(fixture.OldFanartHash, AssertEx.Sha256(backup), "Old fanart backup was removed or changed.");
                    AssertEx.FileExists(fixture.Fanart);
                }
                fixture.AssertVideosUnchanged();
            }
            finally
            {
                stagedLock?.Dispose();
                committedLock?.Dispose();
                cancel.Cancel();
                if (execution is not null)
                {
                    try { await execution; } catch { /* Already asserted; drain before deleting the synthetic fixture. */ }
                }
            }
        }
    }

    private static string ReadTestLog()
    {
        using var stream = new FileStream(AppLog.CurrentLogPath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed class Fixture : IDisposable
    {
        public TestWorkspace Workspace { get; } = new("sharing-files");
        public MovieMetadata Metadata { get; } = new()
        {
            Id = "LCK-555", Title = "Synthetic sharing test", SourceName = "libredmm",
            CoverUrl = "https://images.invalid/cover.jpg", ScreenshotUrls = ["https://images.invalid/sample.jpg"]
        };
        public SavePlan Plan { get; }
        public string Fanart { get; }
        public string OldFanartHash { get; }
        public string SourceDirectory => Path.GetDirectoryName(Plan.SourceVideoPath)!;
        public FileOrganizationService Organizer { get; }
        private readonly Dictionary<string, string> _videos = [];
        private readonly HttpClient _http;
        private readonly OutputService _output;

        public Fixture(bool multipart)
        {
            var names = multipart ? new[] { "LCK-555-cd1.mp4", "LCK-555-cd2.mp4" } : ["LCK-555.mp4"];
            foreach (var name in names)
            {
                var path = Workspace.WriteFile((multipart ? "LCK-555/" : "") + name, [8, 6, 4, 2]);
                _videos[path] = AssertEx.Sha256(path);
            }
            Plan = FileOrganizationService.BuildPlan(_videos.Keys, Metadata,
                new SaveOptions(false, false, true, true, true, true), new OrganizationOptions(false, false));
            Fanart = Path.Combine(Plan.TargetDirectory,
                Plan.OutputNamingMode == OutputNamingMode.MovieFolder ? "fanart.jpg" : Plan.TargetBaseName + "-fanart.jpg");
            File.WriteAllBytes(Fanart, [1, 2, 3]);
            OldFanartHash = AssertEx.Sha256(Fanart);
            _http = new HttpClient(new StaticImageHandler(TestImageFactory.CreateJpeg()));
            _output = new OutputService(_http);
            Organizer = new FileOrganizationService(_output);
        }

        public void AssertVideosUnchanged()
        {
            foreach (var (path, hash) in _videos) AssertEx.Equal(hash, AssertEx.Sha256(path));
        }

        public void Dispose() { _output.Dispose(); _http.Dispose(); Workspace.Dispose(); }
    }
}
