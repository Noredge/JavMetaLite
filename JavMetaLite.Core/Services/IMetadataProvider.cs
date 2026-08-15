using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

public interface IMetadataProvider : IDisposable
{
    string Name { get; }
    string DisplayName { get; }
    Task<MovieMetadata> SearchAsync(string rawId, CancellationToken cancellationToken = default);
}
