using JavMetaLite.Core.Services;
using SkiaSharp;

internal static partial class Program
{
    private static async Task FreshSidecarsAndAliases()
    {
        using var temp = new Temp();
        var video = temp.Write("movie/DEMO-001.mp4", "UNCHANGED SYNTHETIC VIDEO");
        var first = LocalSidecarLocator.Locate(video);
        Check(first.NfoPath is null && first.PosterPath is null, "Unexpected baseline sidecar");
        var nfo = temp.Write("movie/DEMO-001.nfo", Fixture("round-trip.nfo"));
        var poster = WriteImage(temp, "movie/DEMO-001-poster.jpg", SKColors.Green);
        var second = LocalSidecarLocator.Locate(video);
        Check(FileSystemPathSemantics.PathsEqual(nfo, second.NfoPath!), "A late NFO was hidden by stale directory state");
        Equal(poster, second.PosterPath);
        var loaded = await NfoReader.ReadAsync(second);
        Check(loaded.HasUnknownXml, "Fresh NFO did not retain its round-trip baseline");
        if (!OperatingSystem.IsWindows())
        {
            var alias = Path.Combine(Path.GetDirectoryName(video)!, "same-nfo-alias.nfo");
            File.CreateSymbolicLink(alias, FileSystemPathSemantics.CanonicalizePath(nfo));
            await Throws<IOException>(() => Task.FromResult(LocalSidecarLocator.Locate(video)));
            File.Delete(alias);
            var outside = temp.Write("outside.nfo", "<movie><title>DO NOT MODIFY</title></movie>");
            File.Delete(nfo); File.CreateSymbolicLink(nfo, outside);
            await Throws<IOException>(() => NfoReader.ReadAsync(LocalSidecarLocator.Locate(video)));
            Equal("<movie><title>DO NOT MODIFY</title></movie>", File.ReadAllText(outside));
        }
        Equal("UNCHANGED SYNTHETIC VIDEO", File.ReadAllText(video));
    }
}
