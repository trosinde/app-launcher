using System.ComponentModel;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace AppStart.Setup.Engine.Accounts;

/// <summary>Readable exceptions for Win32, NetApi and NTSTATUS errors. Access denied becomes <see cref="UnauthorizedAccessException"/>.</summary>
internal static class AccountErrors
{
    public static Exception FromWin32(uint code, string what)
    {
        var inner = new Win32Exception((int)code);
        if (code == (uint)WIN32_ERROR.ERROR_ACCESS_DENIED)
        {
            return new UnauthorizedAccessException($"{what}: access denied. This requires administrator rights.", inner);
        }

        var text = code switch
        {
            PInvoke.NERR_UserExists => "The account already exists.",
            PInvoke.NERR_GroupNotFound => "The group does not exist.",
            PInvoke.NERR_PasswordTooShort => "The password does not meet the password policy of this computer (length, complexity or history).",
            (uint)WIN32_ERROR.ERROR_NO_SUCH_MEMBER => "The account does not exist.",
            (uint)WIN32_ERROR.ERROR_NONE_MAPPED => "The account was not found.",
            _ => inner.Message,
        };
        return new Win32Exception((int)code, $"{what} failed (error {code}): {text}");
    }

    public static Exception FromNtStatus(NTSTATUS status, string what) =>
        FromWin32(PInvoke.LsaNtStatusToWinError(status), $"{what} (NTSTATUS 0x{status.Value:X8})");

    public static void Check(NTSTATUS status, string what)
    {
        if (status.SeverityCode != NTSTATUS.Severity.Success)
        {
            throw FromNtStatus(status, what);
        }
    }
}
