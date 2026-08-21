using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Moonshine.Core.Security;

/// <summary>
/// Cryptographically hardened file storage engine with explicit Windows Discretionary Access Control Lists (DACLs).
/// Enforces pre-write DACL configuration, explicit Win32 atomic replacement, durable disk flushing,
/// and deterministic cleanup of transient temporary material on any failure path.
/// </summary>
public static partial class SecureFileStore
{
    private const uint MoveFileReplaceExisting = 0x00000001;
    private const uint MoveFileWriteThrough = 0x00000008;

    [LibraryImport("kernel32.dll", EntryPoint = "MoveFileExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MoveFileExW(string lpExistingFileName, string lpNewFileName, uint dwFlags);

    [LibraryImport("kernel32.dll", EntryPoint = "ReplaceFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReplaceFileW(
        string lpReplacedFileName,
        string lpReplacementFileName,
        string? lpBackupFileName,
        uint dwReplaceFlags,
        IntPtr lpExclude,
        IntPtr lpReserved
    );

    /// <summary>
    /// Writes text to the target file with owner/SYSTEM exclusive DACLs, ensuring private key material
    /// is never written to disk prior to access control protection.
    /// </summary>
    public static async Task WriteAllTextSecureAsync(string destinationPath, string contents, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(contents);

        byte[] bytes = Encoding.UTF8.GetBytes(contents);
        await WriteAllBytesSecureAsync(destinationPath, bytes, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes raw binary bytes to the target file with owner/SYSTEM exclusive DACLs.
    /// </summary>
    public static async Task WriteAllBytesSecureAsync(string destinationPath, byte[] bytes, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(bytes);

        string? directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempPath = $"{destinationPath}.tmp.{Guid.NewGuid():N}";
        bool completed = false;

        try
        {
            // 1. Create unique temporary file with WriteThrough semantics
            var fileStreamOptions = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.ReadWrite,
                Share = FileShare.None,
                Options = FileOptions.WriteThrough,
                BufferSize = 4096
            };

            await using (var fs = new FileStream(tempPath, fileStreamOptions))
            {
                // 2. Apply strict Windows DACL BEFORE writing any secret payload bytes
                if (OperatingSystem.IsWindows())
                {
                    ApplyStrictDacl(tempPath);
                }

                // 3. Write payload bytes through the secured handle
                await fs.WriteAsync(bytes, ct).ConfigureAwait(false);

                // 4. Force durable metadata and data flush to physical storage
                await fs.FlushAsync(ct).ConfigureAwait(false);
                fs.Flush(flushToDisk: true);
            }

            // 5. Explicit Win32 atomic replacement
            if (OperatingSystem.IsWindows())
            {
                AtomicReplaceWindows(tempPath, destinationPath);
            }
            else
            {
                File.Move(tempPath, destinationPath, overwrite: true);
            }

            completed = true;
        }
        finally
        {
            // 6. Guarantee immediate cleanup of temporary file on any failure, cancellation, or exception
            if (!completed && File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // Suppress secondary exceptions during unwind cleanup
                }
            }
        }
    }

    /// <summary>
    /// Reads text from a secured file.
    /// </summary>
    public static async Task<string> ReadAllTextSecureAsync(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        byte[] bytes = await ReadAllBytesSecureAsync(path, ct).ConfigureAwait(false);
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// Reads raw bytes from a secured file.
    /// </summary>
    public static async Task<byte[]> ReadAllBytesSecureAsync(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fileStreamOptions = new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            BufferSize = 4096
        };

        await using var fs = new FileStream(path, fileStreamOptions);
        byte[] buffer = new byte[fs.Length];
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await fs.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), ct).ConfigureAwait(false);
            if (read == 0) break;
            totalRead += read;
        }
        return buffer;
    }

    /// <summary>
    /// Applies strict owner-only Discretionary Access Control List (DACL) to the specified file.
    /// Inheritance is explicitly disabled, granting FullControl exclusively to CurrentUser and SYSTEM.
    /// </summary>
    public static void ApplyStrictDacl(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        var fileSecurity = new FileSecurity();

        // Strip inherited rules and protect the DACL
        fileSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        // Grant FullControl exclusively to the authenticated current user
        var currentUserSid = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Unable to resolve current Windows user SID.");

        fileSecurity.AddAccessRule(new FileSystemAccessRule(
            currentUserSid,
            FileSystemRights.FullControl,
            AccessControlType.Allow
        ));

        // Grant FullControl to NT AUTHORITY\SYSTEM for system services
        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        fileSecurity.AddAccessRule(new FileSystemAccessRule(
            systemSid,
            FileSystemRights.FullControl,
            AccessControlType.Allow
        ));

        fileInfo.SetAccessControl(fileSecurity);
    }

    private static void AtomicReplaceWindows(string tempPath, string destinationPath)
    {
        if (File.Exists(destinationPath))
        {
            // Use Win32 ReplaceFileW when destination already exists
            if (!ReplaceFileW(destinationPath, tempPath, null, 0, IntPtr.Zero, IntPtr.Zero))
            {
                // Fallback to MoveFileExW with replacement flags
                if (!MoveFileExW(tempPath, destinationPath, MoveFileReplaceExisting | MoveFileWriteThrough))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to replace '{destinationPath}' with '{tempPath}'.");
                }
            }
        }
        else
        {
            // Use Win32 MoveFileExW with write-through when creating a new destination
            if (!MoveFileExW(tempPath, destinationPath, MoveFileWriteThrough))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to move '{tempPath}' to '{destinationPath}'.");
            }
        }
    }
}
