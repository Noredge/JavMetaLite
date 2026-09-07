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
    ReplaceImage,
    RemoveFile
}

public sealed record PlannedFileChange(
    PlannedChangeKind Kind,
    string Description,
    string DestinationPath,
    string? SourcePath = null,
    bool RequiresOverwrite = false,
    bool IsBlocking = false);

public sealed record VideoFileTransfer(
    string SourcePath,
    string TargetPath,
    int? PartNumber,
    bool RequiresVerifiedCopy)
{
    public bool WillMove => !Path.GetFullPath(SourcePath)
        .Equals(Path.GetFullPath(TargetPath), StringComparison.OrdinalIgnoreCase);
}

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

    public string OutputAnchorPath { get; init; } = TargetVideoPath;

    public OutputNamingMode OutputNamingMode { get; init; } = OutputNamingMode.VideoBase;

    public OutputFileNames? OutputFileNames { get; init; }

    public IReadOnlyList<VideoFileTransfer> VideoTransfers { get; init; } =
        [new(SourceVideoPath, TargetVideoPath, null, false)];

    public IReadOnlyList<LocalSidecarTransfer> SidecarTransfers { get; init; } = [];

    public IReadOnlyList<SourceFileExpectation> SourceFileExpectations { get; init; } = [];

    public IReadOnlyList<string> SourcePathsToRetire { get; init; } = [];

    public IReadOnlyList<string>? ExtrafanartSourceLocations { get; init; }

    public bool RequiresVerifiedVideoCopy { get; init; }

    public bool HasBlockingConflicts => BlockingConflicts.Count > 0;

    public bool VideoWillMove => VideoTransfers.Any(transfer => transfer.WillMove);

    public bool HasActualChanges => VideoWillMove || Changes.Any(change =>
        change.Kind is not PlannedChangeKind.KeepFile);
}

public sealed record LocalSaveContext(
    LocalMetadataBundle? MetadataBundle,
    ArtworkCoverCandidate? LocalArtwork,
    ArtworkCoverCandidate? SelectedArtwork)
{
    public IReadOnlyList<string> LocalExtrafanartPaths { get; init; } = [];

    public bool CanReplaceLocalExtrafanart { get; init; }
}

public enum LocalSidecarRole
{
    Nfo,
    Poster,
    Fanart,
    Extrafanart
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
    bool VideoMoved)
{
    public IReadOnlyList<string> VideoPaths { get; init; } = [VideoPath];
}

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
