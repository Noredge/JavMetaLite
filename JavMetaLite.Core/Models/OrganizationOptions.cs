namespace JavMetaLite.Core.Models;

public sealed record OrganizationOptions(
    bool CreateMovieFolder,
    bool RenameVideo);

public enum PlannedChangeKind
{
    CreateFolder,
    MoveVideo,
    RenameVideo,
    MoveAndRenameVideo,
    CreateFile,
    OverwriteFile
}

public sealed record PlannedFileChange(
    PlannedChangeKind Kind,
    string Description,
    string DestinationPath,
    string? SourcePath = null,
    bool RequiresOverwrite = false,
    bool IsBlocking = false);

public sealed record SavePlan(
    string SourceVideoPath,
    string TargetVideoPath,
    string TargetDirectory,
    string TargetBaseName,
    SaveOptions SaveOptions,
    OrganizationOptions OrganizationOptions,
    IReadOnlyList<PlannedFileChange> Changes,
    IReadOnlyList<string> OverwriteConflicts,
    IReadOnlyList<string> BlockingConflicts)
{
    public bool HasBlockingConflicts => BlockingConflicts.Count > 0;

    public bool VideoWillMove =>
        !string.Equals(SourceVideoPath, TargetVideoPath, StringComparison.OrdinalIgnoreCase);
}

public sealed record OrganizedSaveResult(
    SaveResult Outputs,
    string VideoPath,
    bool VideoMoved);
