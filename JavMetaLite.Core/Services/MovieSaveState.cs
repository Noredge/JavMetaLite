namespace JavMetaLite.Core.Services;

public enum MovieSaveState
{
    Idle,
    Saving,
    Completed,
    SaveFailed,
    Conflict,
    SaveCanceled
}
