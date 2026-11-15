using System.Runtime.Versioning;
using Microsoft.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.ApplicationInstallationAndServicing;
using Windows.Win32;

namespace AppStart.Bootstrapper;

/// <summary>Version and upgrade code read from the MSI's Property table.</summary>
internal sealed record MsiInfo(string? ProductVersion, string? UpgradeCode);

/// <summary>Highest installed product with the same upgrade code.</summary>
internal sealed record InstalledProduct(string ProductCode, string? Version, string? InstallLocation);

/// <summary>Embedded MSI and Windows Installer queries (msi.dll, read-only).</summary>
[SupportedOSPlatform("windows10.0.17763")] // Windows 10 and 11 only (project.md).
internal static unsafe class MsiPackage
{
    /// <summary>The embedded MSI, or null in builds without -p:AppStartMsi.</summary>
    public static Stream? OpenEmbedded() =>
        typeof(MsiPackage).Assembly.GetManifestResourceStream(BootstrapperLogic.MsiResourceName);

    /// <summary>Writes the MSI into the (already protected, new) folder; the file inherits its DACL.</summary>
    public static void Extract(Stream source, string target)
    {
        using var file = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20, FileOptions.WriteThrough);
        source.CopyTo(file, 1 << 20);
        file.Flush(flushToDisk: true);
    }

    public static MsiInfo ReadInfo(string msiPath)
    {
        MSIHANDLE database;
        fixed (char* path = msiPath)
        {
            // szPersist = MSIDBOPEN_READONLY (0).
            if (PInvoke.MsiOpenDatabase(path, (char*)0, &database) != 0)
            {
                return new MsiInfo(null, null);
            }
        }

        try
        {
            return new MsiInfo(ReadProperty(database, "ProductVersion"), ReadProperty(database, "UpgradeCode"));
        }
        finally
        {
            PInvoke.MsiCloseHandle(database);
        }
    }

    /// <summary>Installed product with the highest version among those sharing <paramref name="upgradeCode"/>.</summary>
    public static InstalledProduct? FindInstalled(string? upgradeCode)
    {
        if (string.IsNullOrWhiteSpace(upgradeCode))
        {
            return null;
        }

        InstalledProduct? best = null;
        var buffer = stackalloc char[39];
        fixed (char* upgrade = upgradeCode)
        {
            for (uint index = 0; ; index++)
            {
                var result = PInvoke.MsiEnumRelatedProducts(upgrade, 0, index, buffer);
                if (result != (uint)WIN32_ERROR.ERROR_SUCCESS)
                {
                    break;
                }

                var productCode = new string(buffer);
                var candidate = new InstalledProduct(productCode, GetProductInfo(productCode, "VersionString"), GetProductInfo(productCode, "InstallLocation"));
                if (best is null || IsNewer(candidate.Version, best.Version))
                {
                    best = candidate;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// InstallLocation from the uninstall entry Velopack writes (HKLM, 64-bit view), else null.
    /// HKLM is writable by administrators only; the caller still restricts the value to Program Files.
    /// </summary>
    public static string? VelopackInstallLocation()
    {
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\AppStart");
            return key?.GetValue("InstallLocation") as string;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    private static bool IsNewer(string? candidate, string? current) =>
        BootstrapperLogic.TryParseMsiVersion(candidate, out var a)
        && (!BootstrapperLogic.TryParseMsiVersion(current, out var b) || a > b);

    private static string? GetProductInfo(string productCode, string property)
    {
        fixed (char* product = productCode)
        fixed (char* name = property)
        {
            uint length = 0;
            var empty = stackalloc char[1];
            var result = PInvoke.MsiGetProductInfo(product, name, empty, &length);
            if (result != (uint)WIN32_ERROR.ERROR_SUCCESS && result != (uint)WIN32_ERROR.ERROR_MORE_DATA)
            {
                return null;
            }

            length++;
            var buffer = new char[length];
            fixed (char* b = buffer)
            {
                return PInvoke.MsiGetProductInfo(product, name, b, &length) == (uint)WIN32_ERROR.ERROR_SUCCESS
                    ? new string(b, 0, (int)length)
                    : null;
            }
        }
    }

    private static string? ReadProperty(MSIHANDLE database, string property)
    {
        // Property names are fixed literals above; no user input reaches the query.
        var query = $"SELECT `Value` FROM `Property` WHERE `Property` = '{property}'";
        MSIHANDLE view;
        fixed (char* q = query)
        {
            if (PInvoke.MsiDatabaseOpenView(database, q, &view) != 0)
            {
                return null;
            }
        }

        try
        {
            if (PInvoke.MsiViewExecute(view, default) != 0)
            {
                return null;
            }

            MSIHANDLE record;
            if (PInvoke.MsiViewFetch(view, &record) != 0)
            {
                return null;
            }

            try
            {
                uint length = 0;
                var empty = stackalloc char[1];
                var result = PInvoke.MsiRecordGetString(record, 1, empty, &length);
                if (result != (uint)WIN32_ERROR.ERROR_SUCCESS && result != (uint)WIN32_ERROR.ERROR_MORE_DATA)
                {
                    return null;
                }

                length++;
                var buffer = new char[length];
                fixed (char* b = buffer)
                {
                    return PInvoke.MsiRecordGetString(record, 1, b, &length) == (uint)WIN32_ERROR.ERROR_SUCCESS
                        ? new string(b, 0, (int)length)
                        : null;
                }
            }
            finally
            {
                PInvoke.MsiCloseHandle(record);
            }
        }
        finally
        {
            PInvoke.MsiCloseHandle(view);
        }
    }
}
