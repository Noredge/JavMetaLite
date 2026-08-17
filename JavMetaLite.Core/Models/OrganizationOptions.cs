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
    OverwriteFile,
    UpdateFile,
    KeepFile,
    ReplaceImage
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
    public SaveOptions OutputGenerationOptions { get; init; } = SaveOptions;

    public LocalSaveContext? LocalContext { get; init; }

    public NfoWriteContext? NfoWriteContext { get; init; }

    public IReadOnlyList<LocalSidecarTransfer> SidecarTransfers { get; init; } = [];

    public IReadOnlyList<SourceFileExpectation> SourceFileExpectations { get; init; } = [];

    public IReadOnlyList<string> SourcePathsToRetire { get; init; } = [];

    public bool HasBlockingConflicts => BlockingConflicts.Count > 0;

    public bool VideoWillMove =>
        !string.Equals(SourceVideoPath, TargetVideoPath, StringComparison.OrdinalIgnoreCase);

    public bool HasActualChanges => VideoWillMove || Changes.Any(change =>
        change.Kind is not PlannedChangeKind.KeepFile);
}

public sealed record LocalSaveContext(
    LocalMetadataBundle? MetadataBundle,
    ArtworkCoverCandidate? LocalArtwork,
    ArtworkCoverCandidate? SelectedArtwork);

public enum LocalSidecarRole
{
    Nfo,
    Poster,
    Fanart
}

public sealed record LocalSidecarTransfer(
    LocalSidecarRole Role,
    string SourcePath,
    string DestinationPath,
    string ExpectedSha256);

public sealed record SourceFileExpectation(
    string Path,
    string ExpectedSha256,
    string Description);

public sealed record OrganizedSaveResult(
    SaveResult Outputs,
    string VideoPath,
    bool VideoMoved);
