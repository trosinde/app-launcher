using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.Security.Authentication.Identity;

namespace AppStart.Service.Launch;

/// <summary>
/// Token for the app account without a password: LsaLogonUser with MSV1_0_S4U_LOGON, logon type Batch
/// (design D1, confirmed in spike 1.1). Returns a full (unfiltered) primary token.
/// </summary>
internal static unsafe class S4ULogon
{
    private const string AuthPackage = "MICROSOFT_AUTHENTICATION_PACKAGE_V1_0";
    private const string Origin = "AppStart";

    public static HANDLE LogonBatch(string user, string domain)
    {
        HANDLE lsa;
        uint mode;
        fixed (byte* originBytes = Ascii(Origin))
        {
            var origin = AnsiString(originBytes, Origin.Length);
            Check(PInvoke.LsaRegisterLogonProcess(&origin, &lsa, &mode), "LsaRegisterLogonProcess");
        }

        try
        {
            uint package;
            fixed (byte* pkgBytes = Ascii(AuthPackage))
            {
                var pkg = AnsiString(pkgBytes, AuthPackage.Length);
                Check(PInvoke.LsaLookupAuthenticationPackage(lsa, &pkg, &package), "LsaLookupAuthenticationPackage");
            }

            var token = Logon(lsa, package, user, domain);
            return PreferLinkedFullToken(token);
        }
        finally
        {
            PInvoke.LsaDeregisterLogonProcess(lsa);
        }
    }

    private static HANDLE Logon(HANDLE lsa, uint package, string user, string domain)
    {
        // MSV1_0_S4U_LOGON followed by both strings in one buffer; the UNICODE_STRINGs point into it.
        var userBytes = Encoding.Unicode.GetBytes(user);
        var domainBytes = Encoding.Unicode.GetBytes(domain);
        var header = sizeof(MSV1_0_S4U_LOGON);
        var total = header + userBytes.Length + domainBytes.Length;
        var buffer = (byte*)NativeMemory.AllocZeroed((nuint)total);
        try
        {
            var logon = (MSV1_0_S4U_LOGON*)buffer;
            logon->MessageType = MSV1_0_LOGON_SUBMIT_TYPE.MsV1_0S4ULogon;
            var userPtr = buffer + header;
            var domainPtr = userPtr + userBytes.Length;
            userBytes.CopyTo(new Span<byte>(userPtr, userBytes.Length));
            domainBytes.CopyTo(new Span<byte>(domainPtr, domainBytes.Length));
            logon->UserPrincipalName = new LSA_UNICODE_STRING { Length = (ushort)userBytes.Length, MaximumLength = (ushort)userBytes.Length, Buffer = (char*)userPtr };
            logon->DomainName = new LSA_UNICODE_STRING { Length = (ushort)domainBytes.Length, MaximumLength = (ushort)domainBytes.Length, Buffer = (char*)domainPtr };

            var source = new TOKEN_SOURCE();
            "AppStart"u8.CopyTo(new Span<byte>(&source.SourceName, 8));

            void* profile = null;
            uint profileLength;
            LUID logonId;
            HANDLE token;
            QUOTA_LIMITS quotas;
            int subStatus;
            fixed (byte* originBytes = Ascii(Origin))
            {
                var origin = AnsiString(originBytes, Origin.Length);
                var status = PInvoke.LsaLogonUser(lsa, &origin, SECURITY_LOGON_TYPE.Batch, package, buffer, (uint)total,
                    null, &source, &profile, &profileLength, &logonId, &token, &quotas, &subStatus);
                if (profile is not null)
                {
                    PInvoke.LsaFreeReturnBuffer(profile);
                }

                Check(status, $"LsaLogonUser(S4U, Batch, {domain}\\{user}), SubStatus 0x{subStatus:X8}");
            }

            return token;
        }
        finally
        {
            NativeMemory.Free(buffer);
        }
    }

    // An admin account can come back UAC-filtered on some systems; as SYSTEM we may take the linked full token.
    private static HANDLE PreferLinkedFullToken(HANDLE token)
    {
        TOKEN_ELEVATION_TYPE type;
        uint length;
        if (!PInvoke.GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenElevationType, &type, (uint)sizeof(TOKEN_ELEVATION_TYPE), &length))
        {
            var error = Marshal.GetLastWin32Error();
            PInvoke.CloseHandle(token);
            throw new Win32Exception(error, "GetTokenInformation(TokenElevationType)");
        }

        if (type != TOKEN_ELEVATION_TYPE.TokenElevationTypeLimited)
        {
            return token;
        }

        TOKEN_LINKED_TOKEN linked;
        var ok = PInvoke.GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenLinkedToken, &linked, (uint)sizeof(TOKEN_LINKED_TOKEN), &length);
        var linkedError = Marshal.GetLastWin32Error();
        PInvoke.CloseHandle(token);
        return ok ? linked.LinkedToken : throw new Win32Exception(linkedError, "GetTokenInformation(TokenLinkedToken)");
    }

    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s + "\0");

    private static LSA_STRING AnsiString(byte* bytes, int length) =>
        new() { Length = (ushort)length, MaximumLength = (ushort)(length + 1), Buffer = (PSTR)bytes };

    private static void Check(NTSTATUS status, string what)
    {
        if (status.SeverityCode != NTSTATUS.Severity.Success)
        {
            throw new Win32Exception((int)PInvoke.LsaNtStatusToWinError(status), $"{what} fehlgeschlagen (NTSTATUS 0x{status.Value:X8})");
        }
    }
}
