using System.ComponentModel;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace AppStart.Setup.Engine.Accounts;

/// <summary>German exceptions for Win32, NetApi and NTSTATUS errors. Access denied becomes <see cref="UnauthorizedAccessException"/>.</summary>
internal static class AccountErrors
{
    public static Exception FromWin32(uint code, string what)
    {
        var inner = new Win32Exception((int)code);
        if (code == (uint)WIN32_ERROR.ERROR_ACCESS_DENIED)
        {
            return new UnauthorizedAccessException($"{what}: Zugriff verweigert. Dafür sind Administratorrechte nötig.", inner);
        }

        var text = code switch
        {
            PInvoke.NERR_UserExists => "Das Konto existiert bereits.",
            PInvoke.NERR_GroupNotFound => "Die Gruppe existiert nicht.",
            PInvoke.NERR_PasswordTooShort => "Das Passwort erfüllt die Kennwortrichtlinie dieses Computers nicht (Länge, Komplexität oder Verlauf).",
            (uint)WIN32_ERROR.ERROR_NO_SUCH_MEMBER => "Das Konto existiert nicht.",
            (uint)WIN32_ERROR.ERROR_NONE_MAPPED => "Das Konto wurde nicht gefunden.",
            _ => inner.Message,
        };
        return new Win32Exception((int)code, $"{what} fehlgeschlagen (Fehler {code}): {text}");
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
