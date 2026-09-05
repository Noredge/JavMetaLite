namespace JavMetaLite.Core.Services;

// Only Windows sharing/lock violations are transient here. Never retry name conflicts,
// permissions, disk errors or whole transactions, and never overwrite a move destination.
internal static class FileSharingRetry
{
    private static readonly int[] DelaysMs = [100, 200, 400];

    internal static Task MoveAsync(string source, string destination, CancellationToken token = default) =>
        RunAsync(() => File.Move(source, destination), "move", $"{source} -> {destination}", token);

    internal static Task DeleteAsync(string path) =>
        RunAsync(() => File.Delete(path), "rollback-delete", path);

    internal static async Task RunAsync(Action action, string operation, string path,
        CancellationToken token = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                action();
                return;
            }
            catch (IOException exception) when (exception.HResult is
                unchecked((int)0x80070020) or unchecked((int)0x80070021))
            {
                if (attempt == DelaysMs.Length)
                {
                    AppLog.Warning($"文件持续被占用 operation={operation} path={path}", exception);
                    throw;
                }
                AppLog.Info($"文件被占用，有限重试 operation={operation} path={path} " +
                    $"retry={attempt + 1}/{DelaysMs.Length} delayMs={DelaysMs[attempt]}");
                await Task.Delay(DelaysMs[attempt], token).ConfigureAwait(false);
            }
        }
    }
}
