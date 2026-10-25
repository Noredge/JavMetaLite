using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace JavMetaLite.Core.Services;

// Metadata for an existing container, not an enumeration of its unselected parent.
// This does not authorize a file replacement, directory creation or directory move.
internal static class MacDirectoryName
{
    internal static string Read(string path)
    {
        var attributes = new AttributeList { BitmapCount = 5, Common = 1 }; // ATTR_CMN_NAME
        var buffer = new byte[4096];
        // Public Darwin sys/attr.h: attrlist (24 bytes), then uint32 length and
        // attrreference_t { int32 offset; uint32 length }, relative to that reference.
        if (Marshal.SizeOf<AttributeList>() != 24)
            throw new IOException("Unsupported directory metadata ABI.");
        if (GetAttributes(path, ref attributes, buffer, (nuint)buffer.Length, 1) != 0) // FSOPT_NOFOLLOW
        {
            var errno = Marshal.GetLastPInvokeError();
            if (errno is 1 or 13) throw new UnauthorizedAccessException($"Cannot validate the selected directory name (errno {errno}).");
            throw new IOException($"Cannot validate the selected directory name (errno {errno}).");
        }
        var total = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        var offset = 4L + BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(4));
        var length = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(8));
        if (total < 12 || total > buffer.Length || offset < 12 || length < 2 || offset + length > total || buffer[offset + length - 1] != 0)
            throw new IOException("Invalid directory metadata layout.");
        return new UTF8Encoding(false, true).GetString(buffer, (int)offset, (int)length - 1);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AttributeList
    {
        internal ushort BitmapCount, Reserved;
        internal uint Common, Volume, Directory, File, Fork;
    }

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "getattrlist", SetLastError = true)]
    private static extern int GetAttributes([MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        ref AttributeList attributes, [Out] byte[] buffer, nuint bufferSize, nuint options);
}
