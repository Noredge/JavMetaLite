using JavMetaLite.Core.Services;

internal static partial class Program
{
    private static Task SidecarInventoryLookup()
    {
        using var temp = new Temp();
        var first = temp.Write("flat/DEMO-771.mp4", "FIRST");
        var second = temp.Write("flat/DEMO-772.mp4", "SECOND");
        var firstNfo = temp.Write("flat/DEMO-771.nfo", "<movie><title>first</title></movie>");
        var secondNfo = temp.Write("flat/DEMO-772.nfo", "<movie><title>second</title></movie>");
        temp.Write("flat/movie.nfo", "<movie><title>folder must not own two movies</title></movie>");
        temp.Write("flat/poster.jpg", "UNRELATED FOLDER POSTER");
        var firstPoster = temp.Write("flat/DEMO-771-poster.png", "FIRST POSTER");
        temp.Write("flat/extrafanart/fanart10.jpg", "TEN");
        temp.Write("flat/extrafanart/fanart2.jpg", "TWO");
        temp.Write("flat/extrafanart/notes.txt", "KEEP NOTES");
        if (!OperatingSystem.IsWindows())
            File.CreateSymbolicLink(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(first)!, "extrafanart", "broken-note.txt"),
                System.IO.Path.Combine(temp.Path, "unavailable-note"));
        var scope = new LocalSidecarInventoryScope();
        foreach (var path in new[] { first, second })
        {
            var plain = LocalSidecarLocator.Locate(path);
            var shared = LocalSidecarLocator.Locate(path, null, false, scope, default);
            Equal(plain.VideoPath, shared.VideoPath); Equal(plain.NfoPath, shared.NfoPath);
            Equal(plain.PosterPath, shared.PosterPath); Equal(plain.FanartPath, shared.FanartPath);
            Check(plain.ExtrafanartPaths.SequenceEqual(shared.ExtrafanartPaths), "Scoped samples changed eligibility/order");
        }
        Equal(FileSystemPathSemantics.CanonicalizePath(firstNfo), LocalSidecarLocator.Locate(first, null, false, scope, default).NfoPath);
        Equal(FileSystemPathSemantics.CanonicalizePath(secondNfo), LocalSidecarLocator.Locate(second, null, false, scope, default).NfoPath);
        Equal(FileSystemPathSemantics.CanonicalizePath(firstPoster), LocalSidecarLocator.Locate(first, null, false, scope, default).PosterPath);
        Equal(null, LocalSidecarLocator.Locate(second, null, false, scope, default).PosterPath);
        scope.ValidateReusedDirectories(default);
        var cd1 = temp.Write("parts/DEMO-773-CD1.mp4", "PART ONE");
        var cd2 = temp.Write("parts/DEMO-773-CD2.mp4", "PART TWO");
        var folderNfo = temp.Write("parts/movie.nfo", "<movie><title>parts owner</title></movie>");
        foreach (var path in new[] { cd1, cd2 })
            Equal(FileSystemPathSemantics.CanonicalizePath(folderNfo), LocalSidecarLocator.Locate(path, "DEMO-773", true, scope, default).NfoPath);
        scope.ValidateReusedDirectories(default);
        return Task.CompletedTask;
    }

    private static Task SidecarInventoryFreshness()
    {
        var mutations = new[] { "new-nfo", "deleted-nfo", "new-extra-folder", "deleted-extra-folder", "new-extra-file", "deleted-extra-file",
            "link-to-file", "file-to-link", "link-retarget", "alias-collision", "case-alias-collision", "unicode-alias-collision", "ancestor-link" };
        foreach (var mutation in mutations)
        {
            if (OperatingSystem.IsWindows() && (mutation.Contains("link") || mutation.Contains("alias"))) continue;
            using var temp = new Temp();
            var first = temp.Write("inputs/DEMO-781.mp4", "FIRST VIDEO");
            var second = temp.Write("inputs/DEMO-782.mp4", "SECOND VIDEO");
            var nfo = temp.Write("inputs/DEMO-781.nfo", "<movie><title>unchanged</title></movie>");
            var outside = temp.Write("outside/one.nfo", "OUTSIDE ONE");
            var other = temp.Write("outside/two.nfo", "OUTSIDE TWO");
            var directory = System.IO.Path.GetDirectoryName(first)!;
            var link = System.IO.Path.Combine(directory, "pointer.nfo");
            var unicode = mutation == "unicode-alias-collision" ? temp.Write("inputs/Caf\u00e9.nfo", "UNICODE SOURCE") : null;
            if (mutation is "link-to-file" or "link-retarget") File.CreateSymbolicLink(link, outside);
            if (mutation is "file-to-link") File.WriteAllText(link, "ORDINARY");
            var extra = System.IO.Path.Combine(directory, "extrafanart");
            if (mutation is "deleted-extra-folder" or "new-extra-file" or "deleted-extra-file")
                temp.Write("inputs/extrafanart/fanart1.jpg", "ORIGINAL SAMPLE");
            var scope = new LocalSidecarInventoryScope();
            LocalSidecarLocator.Locate(first, null, false, scope, default);
            LocalSidecarLocator.Locate(second, null, false, scope, default);
            var directoryTime = Directory.GetLastWriteTimeUtc(directory);
            switch (mutation)
            {
                case "new-nfo": File.WriteAllText(System.IO.Path.Combine(directory, "DEMO-782.nfo"), "<movie/>"); break;
                case "deleted-nfo": File.Delete(nfo); break;
                case "new-extra-folder": Directory.CreateDirectory(extra); break;
                case "deleted-extra-folder": Directory.Delete(extra, true); break;
                case "new-extra-file": File.WriteAllText(System.IO.Path.Combine(extra, "fanart2.jpg"), "NEW SAMPLE"); break;
                case "deleted-extra-file": File.Delete(System.IO.Path.Combine(extra, "fanart1.jpg")); break;
                case "link-to-file": File.Delete(link); File.WriteAllText(link, "NOW ORDINARY"); break;
                case "file-to-link": File.Delete(link); File.CreateSymbolicLink(link, outside); break;
                case "link-retarget": File.Delete(link); File.CreateSymbolicLink(link, other); break;
                case "alias-collision": File.CreateSymbolicLink(link, nfo); break;
                case "case-alias-collision":
                    var caseAlias = nfo.ToUpperInvariant();
                    File.CreateSymbolicLink(link, File.Exists(caseAlias) ? caseAlias : nfo); break;
                case "unicode-alias-collision":
                    var normalizedAlias = unicode!.Normalize(System.Text.NormalizationForm.FormD);
                    File.CreateSymbolicLink(link, File.Exists(normalizedAlias) ? normalizedAlias : unicode); break;
                case "ancestor-link":
                    var moved = directory + "-original"; Directory.Move(directory, moved);
                    Directory.CreateSymbolicLink(directory, moved); break;
            }
            if (mutation != "ancestor-link") Directory.SetLastWriteTimeUtc(directory, directoryTime);
            ThrowsSync<SidecarInventoryChangedException>(() => scope.ValidateReusedDirectories(default));
            Equal("OUTSIDE ONE", File.ReadAllText(outside)); Equal("OUTSIDE TWO", File.ReadAllText(other));
        }
        using (var temp = new Temp())
        {
            var first = temp.Write("fresh/DEMO-791.mp4", "FIRST");
            var second = temp.Write("fresh/DEMO-792.mp4", "SECOND");
            var scope = new LocalSidecarInventoryScope();
            LocalSidecarLocator.Locate(first, null, false, scope, default);
            LocalSidecarLocator.Locate(second, null, false, scope, default);
            var arrived = temp.Write("fresh/DEMO-792.nfo", "<movie/>");
            ThrowsSync<SidecarInventoryChangedException>(() => scope.ValidateReusedDirectories(default));
            Equal(FileSystemPathSemantics.CanonicalizePath(arrived), LocalSidecarLocator.Locate(second).NfoPath);
            var next = new LocalSidecarInventoryScope();
            Equal(FileSystemPathSemantics.CanonicalizePath(arrived), LocalSidecarLocator.Locate(second, null, false, next, default).NfoPath);
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            ThrowsSync<OperationCanceledException>(() => next.ValidateReusedDirectories(cancellation.Token));
        }
        return Task.CompletedTask;
    }
}
