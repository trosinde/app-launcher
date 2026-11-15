using System.Security.AccessControl;
using System.Security.Principal;

namespace AppStart.Setup.Engine.Verification;

/// <summary>
/// Target DACL of \\.\pipe\AppStart (design D9):
/// <c>D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;0x12018b;;;&lt;AppStart Users&gt;)(D;;GA;;;NU)</c>.
/// Source of the values: src/AppStart.Service/Pipe/PipeFactory.cs (BuildSddl, ClientRights); Setup does not
/// reference the service, so they are repeated here. Keep both in sync.
/// </summary>
internal static class PipeRules
{
    /// <summary>FILE_GENERIC_READ | FILE_WRITE_DATA | FILE_WRITE_ATTRIBUTES (PipeFactory.ClientRights).</summary>
    public const int ClientRights = 0x0012018B;

    private const int GenericAll = 0x10000000;

    /// <summary>GENERIC_ALL as stored after the pipe file system mapped it (FILE_ALL_ACCESS).</summary>
    private const int FileAllAccess = 0x001F01FF;

    private static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier Network = new(WellKnownSidType.NetworkSid, null);

    /// <summary>The SDDL the service builds (for documentation and tests).</summary>
    public static string BuildSddl(SecurityIdentifier operatorGroup) =>
        $"D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;0x{ClientRights:x};;;{operatorGroup.Value})(D;;GA;;;NU)";

    /// <summary>Findings (German) for a pipe descriptor; empty when it matches D9 exactly.</summary>
    public static IReadOnlyList<string> Evaluate(
        RawSecurityDescriptor descriptor, SecurityIdentifier operatorGroup, string operatorGroupName, Func<SecurityIdentifier, string> describe)
    {
        var findings = new List<string>();
        if (descriptor.DiscretionaryAcl is null)
        {
            findings.Add(VerificationTexts.PipeNoDacl);
            return findings;
        }

        if ((descriptor.ControlFlags & ControlFlags.DiscretionaryAclProtected) == 0)
        {
            findings.Add(VerificationTexts.PipeNotProtected);
        }

        var expected = new List<(AceQualifier Qualifier, SecurityIdentifier Sid, Func<int, bool> Mask, string Description)>
        {
            (AceQualifier.AccessAllowed, LocalSystem, IsGenericAll, VerificationTexts.PipeEntrySystem),
            (AceQualifier.AccessAllowed, Administrators, IsGenericAll, VerificationTexts.PipeEntryAdministrators),
            (AceQualifier.AccessAllowed, operatorGroup, m => m == ClientRights,
                string.Format(VerificationTexts.PipeEntryOperators, operatorGroupName)),
            (AceQualifier.AccessDenied, Network, IsGenericAll, VerificationTexts.PipeEntryNetworkDenied),
        };
        var found = new bool[expected.Count];

        foreach (var ace in descriptor.DiscretionaryAcl)
        {
            if (ace is not QualifiedAce qualified)
            {
                findings.Add(string.Format(VerificationTexts.PipeAceUnexpected, ace.AceType, "?", 0));
                continue;
            }

            var index = expected.FindIndex(e =>
                e.Qualifier == qualified.AceQualifier && e.Sid == qualified.SecurityIdentifier && e.Mask(qualified.AccessMask));
            if (index >= 0 && !found[index])
            {
                found[index] = true;
                continue;
            }

            var kind = qualified.AceQualifier == AceQualifier.AccessDenied ? VerificationTexts.AceDeny : VerificationTexts.AceAllow;
            findings.Add(string.Format(VerificationTexts.PipeAceUnexpected, kind, describe(qualified.SecurityIdentifier), qualified.AccessMask));
        }

        for (var i = 0; i < expected.Count; i++)
        {
            if (!found[i])
            {
                findings.Add(string.Format(VerificationTexts.PipeAceMissing, expected[i].Description));
            }
        }

        return findings;
    }

    private static bool IsGenericAll(int mask) => mask == GenericAll || mask == FileAllAccess;
}
