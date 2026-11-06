namespace JavMetaLite.Core.Services;

public enum MovieSearchState
{
    NeedsId,
    Searchable,
    Searching,
    NeedsReview,
    SearchFailed,
    SearchCanceled
}
