using System.Runtime.InteropServices;

namespace JavMetaLite.Core.Services;

// Native identity is additional evidence, never sufficient on its own to authorize
// an overwrite or retirement. Rollback also checks staged content and metadata.
internal readonly record struct MacFileIdentity(uint Device, ulong FileId)
{
    internal static MacFileIdentity ReadDirectory(string path)
    {
        if (Marshal.SizeOf<AttributeList>() != 24 || Marshal.SizeOf<IdentityAttributes>() != 16)
            throw new IOException("Directory identity ABI layout is unsupported.");
        var attributes = new AttributeList { BitmapCount = 5, Common = 0x02000002 };
        var result = new IdentityAttributes();
        // A detached mountpoint can remain as an ordinary directory on its parent
        // device. Its name and readability alone cannot prove the original volume.
        if (GetDirectoryAttributes(path, ref attributes, ref result,
                (nuint)Marshal.SizeOf<IdentityAttributes>(), 1) != 0) // FSOPT_NOFOLLOW
            throw new IOException($"Directory identity could not be verified (errno {Marshal.GetLastPInvokeError()}): {path}");
        if (result.Length != 16 || result.FileId == 0)
            throw new IOException("Directory identity returned an unsupported layout: " + path);
        return new(result.Device, result.FileId);
    }

    internal static bool TryRead(string path, out MacFileIdentity identity, out Exception? error)
    {
        identity = default;
        error = null;
        if (!OperatingSystem.IsMacOS()) return false;
        try
        {
            if (Marshal.SizeOf<AttributeList>() != 24 || Marshal.SizeOf<IdentityAttributes>() != 16)
            {
                error = new IOException("File identity ABI layout is unsupported; path was kept separate.");
                return false;
            }
            using var file = File.OpenHandle(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            // Public Darwin sys/attr.h: ATTR_BIT_MAP_COUNT=5,
            // ATTR_CMN_DEVID=0x2 (dev_t), ATTR_CMN_FILEID=0x02000000 (uint64_t).
            // Attributes are returned in bitmap order, preceded by a uint32 length.
            var attributes = new AttributeList { BitmapCount = 5, Common = 0x02000002 };
            var result = new IdentityAttributes();
            var status = GetAttributes(file.DangerousGetHandle().ToInt32(), ref attributes,
                ref result, (nuint)Marshal.SizeOf<IdentityAttributes>(), 0);
            GC.KeepAlive(file);
            if (status != 0)
            {
                var errno = Marshal.GetLastPInvokeError();
                error = errno is 1 or 13
                    ? new UnauthorizedAccessException($"File identity could not be read (errno {errno}); path was kept separate.")
                    : new IOException($"File identity could not be verified (errno {errno}); path was kept separate.");
                return false;
            }
            if (result.Length != 16 || result.FileId == 0)
            {
                error = new IOException("File identity returned an unsupported layout; path was kept separate.");
                return false;
            }
            identity = new(result.Device, result.FileId);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            NotSupportedException or DllNotFoundException or EntryPointNotFoundException)
        {
            error = exception;
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AttributeList
    {
        internal ushort BitmapCount, Reserved;
        internal uint Common, Volume, Directory, File, Fork;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IdentityAttributes
    {
        internal uint Length, Device;
        internal ulong FileId;
    }

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "fgetattrlist", SetLastError = true)]
    private static extern int GetAttributes(int descriptor, ref AttributeList attributes,
        ref IdentityAttributes buffer, nuint bufferSize, nuint options);

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "getattrlist", SetLastError = true)]
    private static extern int GetDirectoryAttributes([MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        ref AttributeList attributes, ref IdentityAttributes buffer, nuint bufferSize, nuint options);
}
