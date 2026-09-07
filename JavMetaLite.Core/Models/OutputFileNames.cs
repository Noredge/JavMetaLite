namespace JavMetaLite.Core.Models;

// File names only: the same names are used in preview, staging and final commit.
public sealed record OutputFileNames(string Nfo, string Poster, string Fanart)
{
    public static OutputFileNames Create(string baseName, OutputNamingMode mode) =>
        mode is OutputNamingMode.MovieFolder
            ? new("movie.nfo", "poster.jpg", "fanart.jpg")
            : new($"{baseName}.nfo", $"{baseName}-poster.jpg", $"{baseName}-fanart.jpg");

    public string Resolve(string directory, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || name is "." or "..")
            throw new InvalidDataException("Output name must be a file name, not a path.");
        return Path.Combine(directory, name);
    }
}
