namespace JavMetaLite.Core.Models;

public enum OrganizationTargetMode
{
    VideoDirectory,
    SourceNumberFolder,
    CustomRootNumberFolder
}

public enum CrossVolumeVerificationMode
{
    FullSha256,
    FileSizeOnly
}

public sealed record OrganizationOptions
{
    public OrganizationOptions(bool createMovieFolder, bool renameVideo)
        : this(
            createMovieFolder
                ? OrganizationTargetMode.SourceNumberFolder
                : OrganizationTargetMode.VideoDirectory,
            renameVideo,
            crossVolumeVerification: CrossVolumeVerificationMode.FullSha256)
    {
    }

    public OrganizationOptions(
        OrganizationTargetMode targetMode,
        bool renameVideo,
        string? customRootDirectory = null,
        CrossVolumeVerificationMode crossVolumeVerification = CrossVolumeVerificationMode.FullSha256)
    {
        TargetMode = targetMode;
        RenameVideo = renameVideo;
        CustomRootDirectory = customRootDirectory;
        CrossVolumeVerification = Enum.IsDefined(crossVolumeVerification)
            ? crossVolumeVerification
            : CrossVolumeVerificationMode.FullSha256;
    }

    public OrganizationTargetMode TargetMode { get; }

    public bool RenameVideo { get; }

    public string? CustomRootDirectory { get; }

    public CrossVolumeVerificationMode CrossVolumeVerification { get; }

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
    bool UsesCustomRoot,
    bool RequiresVerifiedCopy);

public enum PlannedChangeKind
{
    CreateFolder,
    MoveVideo,
    RenameVideo,
    MoveAndRenameVideo,
    CopyAndVerifyVideo,
    CopyVideo,
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

    public bool RequiresVerifiedVideoCopy { get; init; }

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

public enum FileTransactionStage
{
    Preparing,
    CopyingMovie,
    VerifyingMovie,
    Committing,
    RetiringSource,
    RetiringSourceFast,
    Completed
}

public sealed record FileTransactionProgress(
    FileTransactionStage Stage,
    string Message,
    long BytesProcessed = 0,
    long TotalBytes = 0,
    string? TemporaryPath = null)
{
    public int Percentage => TotalBytes <= 0
        ? 0
        : (int)Math.Clamp(BytesProcessed * 100L / TotalBytes, 0, 100);
}
