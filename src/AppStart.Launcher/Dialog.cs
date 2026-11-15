using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.Controls;

namespace AppStart.Launcher;

/// <summary>Native dialogs (TaskDialog: title, icon, bold heading, text, footer, OK) as in the UI design.</summary>
internal static unsafe class Dialog
{
    public static void Show(string title, LauncherMessage message)
    {
        fixed (char* t = title)
        fixed (char* heading = message.Heading)
        fixed (char* text = message.Text)
        fixed (char* footer = message.Footer)
        {
            var config = new TASKDIALOGCONFIG
            {
                cbSize = (uint)sizeof(TASKDIALOGCONFIG),
                dwFlags = TASKDIALOG_FLAGS.TDF_POSITION_RELATIVE_TO_WINDOW | TASKDIALOG_FLAGS.TDF_SIZE_TO_CONTENT,
                dwCommonButtons = TASKDIALOG_COMMON_BUTTON_FLAGS.TDCBF_OK_BUTTON,
                pszWindowTitle = t,
                pszMainInstruction = heading,
                pszContent = text,
                pszFooter = message.Footer is null ? null : footer,
            };
            config.Anonymous1.pszMainIcon = message.Icon switch
            {
                MessageIcon.Warning => PInvoke.TD_WARNING_ICON,
                MessageIcon.Error => PInvoke.TD_ERROR_ICON,
                _ => PInvoke.TD_INFORMATION_ICON,
            };

            PInvoke.TaskDialogIndirect(&config, null, null, null);
        }
    }

    /// <summary>Path of the .lnk the launcher was started from (STARTF_TITLEISLINKNAME), else null.</summary>
    public static string? StartingShortcut()
    {
        STARTUPINFOW info;
        PInvoke.GetStartupInfo(&info);
        const uint StartfTitleIsLinkName = 0x00000800;
        return ((uint)info.dwFlags & StartfTitleIsLinkName) != 0 && info.lpTitle.Value is not null
            ? new string(info.lpTitle.Value)
            : null;
    }
}
