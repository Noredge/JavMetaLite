namespace JavMetaLite.Core.Models;

public enum OrganizationTargetMode
{
    VideoDirectory,
    SourceNumberFolder,
    CustomRootNumberFolder
}

public sealed record OrganizationOptions
{
    public OrganizationOptions(bool createMovieFolder, bool renameVideo)
        : this(
            createMovieFolder
                ? OrganizationTargetMode.SourceNumberFolder
                : OrganizationTargetMode.VideoDirectory,
            renameVideo)
    {
    }

    public OrganizationOptions(
        OrganizationTargetMode targetMode,
        bool renameVideo,
        string? customRootDirectory = null)
    {
        TargetMode = targetMode;
        RenameVideo = renameVideo;
        CustomRootDirectory = customRootDirectory;
    }

    public OrganizationTargetMode TargetMode { get; }

    public bool RenameVideo { get; }

    public string? CustomRootDirectory { get; }

    public bool CreateMovieFolder => TargetMode is not OrganizationTargetMode.VideoDirectory;

    public bool UsesCustomRoot => TargetMode is OrganizationTargetMode.CustomRootNumberFolder;

    public void Deconstruct(out bool createMovieFolder, out bool renameVideo)
    {
        createMovieFolder = CreateMovieFolder;
        renameVideo = RenameVideo;
    }
}

public sealed record OrganizationPathPlan(
    string SourceVideoPath,
    string SourceDirectory,
    string NormalizedId,
    string TargetRootDirectory,
    string TargetDirectory,
    string TargetBaseName,
    string TargetVideoPath,
    bool UsesCustomRoot);

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
