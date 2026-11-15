using System.Runtime.InteropServices;
using AppStart.Core.Security;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;

namespace AppStart.Bootstrapper;

/// <summary>A folder on the extraction or log path can be changed by non-admins, or is a junction/symlink.</summary>
internal sealed class UnsafeFolderException(string folder, string detail) : Exception($"{folder}: {detail}")
{
    public string Folder { get; } = folder;
}

/// <summary>
/// Creates folders with <see cref="BootstrapperPaths.ProtectedFolderSddl"/> applied atomically at creation
/// (CreateDirectory with a security descriptor), so there is no moment in which others could write.
/// Existing folders are accepted only when they are no reparse point and only trusted principals can write.
/// </summary>
internal static unsafe class ProtectedFolders
{
    /// <summary>Creates the folder protected, or checks an existing one.</summary>
    /// <exception cref="UnsafeFolderException">Existing folder is a reparse point or writable by others.</exception>
    public static void Ensure(string path)
    {
        if (!TryCreate(path, out var error) && error != WIN32_ERROR.ERROR_ALREADY_EXISTS)
        {
            throw new IOException($"Ordner {path} konnte nicht angelegt werden (Fehler {(int)error}).");
        }

        Check(path);
    }

    /// <summary>Creates a new folder that must not exist yet (the per-run Installer\&lt;Guid&gt; folder).</summary>
    public static void CreateNew(string path)
    {
        if (!TryCreate(path, out var error))
        {
            throw new IOException($"Ordner {path} konnte nicht angelegt werden (Fehler {(int)error}).");
        }

        Check(path);
    }

    private static void Check(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new UnsafeFolderException(path, "Verweis (Junction oder symbolischer Link).");
        }

        if ((attributes & FileAttributes.Directory) == 0)
        {
            throw new UnsafeFolderException(path, "kein Ordner.");
        }

        var violations = WriteAccessPolicy.FindViolations(path);
        if (violations.Count > 0)
        {
            throw new UnsafeFolderException(path, string.Join(" ", violations));
        }
    }

    private static bool TryCreate(string path, out WIN32_ERROR error)
    {
        PSECURITY_DESCRIPTOR descriptor;
        fixed (char* sddl = BootstrapperPaths.ProtectedFolderSddl)
        {
            if (!PInvoke.ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, PInvoke.SDDL_REVISION_1, &descriptor, null))
            {
                throw new IOException($"Sicherheitsbeschreibung ungültig (Fehler {Marshal.GetLastPInvokeError()}).");
            }
        }

        try
        {
            var attributes = new SECURITY_ATTRIBUTES
            {
                nLength = (uint)sizeof(SECURITY_ATTRIBUTES),
                lpSecurityDescriptor = descriptor.Value,
                bInheritHandle = false,
            };

            fixed (char* p = path)
            {
                if (PInvoke.CreateDirectory(p, &attributes))
                {
                    error = WIN32_ERROR.NO_ERROR;
                    return true;
                }

                error = (WIN32_ERROR)Marshal.GetLastPInvokeError();
                return false;
            }
        }
        finally
        {
            PInvoke.LocalFree((HLOCAL)descriptor.Value);
        }
    }
}
