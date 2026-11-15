using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace AppStart.Setup.Engine.Shortcuts;

/// <summary>
/// Minimal .lnk access via the shell's ShellLink COM object (IShellLinkW + IPersistFile).
/// ShellLink is registered with ThreadingModel "Both", so it works on STA (WPF UI thread) and MTA
/// (thread pool, background tasks) alike. Every instance is created, used and released within one call
/// on the calling thread; nothing is cached across threads.
/// </summary>
internal static class ShellLink
{
    private const int MaxPath = 32768;
    private const int MaxArguments = 32768;
    private const int StgmRead = 0;

    public static void Save(string path, string target, string arguments, string workingDirectory, string iconPath, int iconIndex, string description)
    {
        var link = (IShellLinkW)new ShellLinkCoClass();
        try
        {
            link.SetPath(target);
            link.SetArguments(arguments);
            link.SetWorkingDirectory(workingDirectory);
            if (iconPath.Length > 0)
            {
                link.SetIconLocation(iconPath, iconIndex);
            }

            if (description.Length > 0)
            {
                link.SetDescription(description);
            }

            ((IPersistFile)link).Save(path, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    /// <summary>Loads a shortcut. Throws COMException / IOException when the file is no readable shortcut.</summary>
    public static ShortcutInfo Load(string path)
    {
        var link = (IShellLinkW)new ShellLinkCoClass();
        try
        {
            ((IPersistFile)link).Load(path, StgmRead);

            var target = new StringBuilder(MaxPath);
            link.GetPath(target, target.Capacity, IntPtr.Zero, 0);

            var arguments = new StringBuilder(MaxArguments);
            link.GetArguments(arguments, arguments.Capacity);

            var workingDirectory = new StringBuilder(MaxPath);
            link.GetWorkingDirectory(workingDirectory, workingDirectory.Capacity);

            var icon = new StringBuilder(MaxPath);
            link.GetIconLocation(icon, icon.Capacity, out var iconIndex);

            var iconLocation = icon.Length == 0 ? string.Empty : $"{icon},{iconIndex}";
            return new ShortcutInfo(path, target.ToString(), arguments.ToString(), iconLocation, workingDirectory.ToString());
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    [ClassInterface(ClassInterfaceType.None)]
    private class ShellLinkCoClass;

    // Vtable order matters: methods are declared exactly as in shobjidl_core.h.
    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);

        void GetIDList(out IntPtr ppidl);

        void SetIDList(IntPtr pidl);

        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

        void GetHotkey(out short pwHotkey);

        void SetHotkey(short wHotkey);

        void GetShowCmd(out int piShowCmd);

        void SetShowCmd(int iShowCmd);

        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);

        void Resolve(IntPtr hwnd, uint fFlags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
