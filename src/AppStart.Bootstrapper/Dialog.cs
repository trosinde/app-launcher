using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.WindowsAndMessaging;

namespace AppStart.Bootstrapper;

/// <summary>Native TaskDialogs: messages, help and the marquee progress dialog while msiexec runs.</summary>
internal static unsafe class Dialog
{
    // State of the single progress dialog (the callback is a static unmanaged function).
    private static Task? runningWork;

    public static void Show(BootstrapperMessage message)
    {
        var content = message.Content;
        var footer = message.Footer;
        fixed (char* title = BootstrapperTexts.Title)
        fixed (char* heading = message.Heading)
        fixed (char* text = content)
        fixed (char* foot = footer)
        {
            var config = new TASKDIALOGCONFIG
            {
                cbSize = (uint)sizeof(TASKDIALOGCONFIG),
                dwFlags = TASKDIALOG_FLAGS.TDF_POSITION_RELATIVE_TO_WINDOW | TASKDIALOG_FLAGS.TDF_SIZE_TO_CONTENT
                          | TASKDIALOG_FLAGS.TDF_ALLOW_DIALOG_CANCELLATION,
                dwCommonButtons = TASKDIALOG_COMMON_BUTTON_FLAGS.TDCBF_OK_BUTTON,
                pszWindowTitle = title,
                pszMainInstruction = heading,
                pszContent = text,
                pszFooter = footer is null ? null : foot,
            };
            config.Anonymous1.pszMainIcon = Icon(message.Icon);
            config.Anonymous2.pszFooterIcon = footer is null ? null : PInvoke.TD_INFORMATION_ICON;

            PInvoke.TaskDialogIndirect(&config, null, null, null);
        }
    }

    /// <summary>
    /// Shows «AppStart wird installiert …» with a marquee bar until <paramref name="work"/> has finished.
    /// The dialog has a Cancel button only because TaskDialog needs one; it is disabled and clicks are
    /// ignored until the work is done (msiexec /qn is not interrupted halfway).
    /// </summary>
    public static void ShowProgressUntil(Task work)
    {
        runningWork = work;
        fixed (char* title = BootstrapperTexts.Title)
        fixed (char* heading = BootstrapperTexts.ProgressHeading)
        fixed (char* text = BootstrapperTexts.ProgressText)
        {
            var config = new TASKDIALOGCONFIG
            {
                cbSize = (uint)sizeof(TASKDIALOGCONFIG),
                dwFlags = TASKDIALOG_FLAGS.TDF_SHOW_MARQUEE_PROGRESS_BAR | TASKDIALOG_FLAGS.TDF_CALLBACK_TIMER
                          | TASKDIALOG_FLAGS.TDF_POSITION_RELATIVE_TO_WINDOW,
                dwCommonButtons = TASKDIALOG_COMMON_BUTTON_FLAGS.TDCBF_CANCEL_BUTTON,
                pszWindowTitle = title,
                pszMainInstruction = heading,
                pszContent = text,
                pfCallback = &ProgressCallback,
            };
            config.Anonymous1.pszMainIcon = PInvoke.TD_INFORMATION_ICON;

            PInvoke.TaskDialogIndirect(&config, null, null, null);
        }

        // Closed by the window manager in some unexpected way: still never continue before msiexec has ended.
        work.Wait();
        runningWork = null;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static HRESULT ProgressCallback(HWND hwnd, uint notification, WPARAM wParam, LPARAM lParam, nint refData)
    {
        var done = runningWork?.IsCompleted ?? true;
        switch ((TASKDIALOG_NOTIFICATIONS)notification)
        {
            case TASKDIALOG_NOTIFICATIONS.TDN_CREATED:
                PInvoke.SendMessage(hwnd, (uint)TASKDIALOG_MESSAGES.TDM_SET_PROGRESS_BAR_MARQUEE, new WPARAM(1), new LPARAM(0));
                PInvoke.SendMessage(hwnd, (uint)TASKDIALOG_MESSAGES.TDM_ENABLE_BUTTON, new WPARAM((nuint)MESSAGEBOX_RESULT.IDCANCEL), new LPARAM(0));
                break;

            case TASKDIALOG_NOTIFICATIONS.TDN_TIMER when done:
                PInvoke.SendMessage(hwnd, (uint)TASKDIALOG_MESSAGES.TDM_CLICK_BUTTON, new WPARAM((nuint)MESSAGEBOX_RESULT.IDCANCEL), new LPARAM(0));
                break;

            case TASKDIALOG_NOTIFICATIONS.TDN_BUTTON_CLICKED when !done:
                // S_FALSE keeps the dialog open: closing early must not look like a finished installation.
                return HRESULT.S_FALSE;
        }

        return HRESULT.S_OK;
    }

    private static PCWSTR Icon(MessageIcon icon) => icon switch
    {
        MessageIcon.Warning => PInvoke.TD_WARNING_ICON,
        MessageIcon.Error => PInvoke.TD_ERROR_ICON,
        _ => PInvoke.TD_INFORMATION_ICON,
    };
}
