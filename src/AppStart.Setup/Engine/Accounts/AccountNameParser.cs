using System.Security.Principal;

namespace AppStart.Setup.Engine.Accounts;

internal enum AccountNameKind
{
    /// <summary>Account of this computer ("user", ".\user" or "COMPUTER\user").</summary>
    Local,

    /// <summary>Account of another authority, usually a domain ("DOMAIN\user").</summary>
    Domain,

    /// <summary>SID string ("S-1-5-21-..."), e.g. an orphaned member recorded in setup-state.json.</summary>
    Sid,
}

/// <summary>A parsed, normalised account name. <see cref="Qualified"/> is "DOMAIN\user" (local: "COMPUTER\user") or the SID string.</summary>
internal sealed record AccountName(AccountNameKind Kind, string? Domain, string? User, SecurityIdentifier? Sid)
{
    public string Qualified => Kind == AccountNameKind.Sid ? Sid!.Value : $"{Domain}\\{User}";

    public override string ToString() => Qualified;
}

/// <summary>
/// Parses the account forms accepted by add-user / remove-user: "DOMAIN\User", ".\user", "user" (= local account
/// of this computer, never a domain lookup) and SID strings. Pure logic, no system calls.
/// </summary>
internal static class AccountNameParser
{
    // Characters not allowed in SAM account names (plus '@': UPNs are not accepted, they are ambiguous here).
    private static readonly char[] InvalidChars = ['"', '/', '\\', '[', ']', ':', ';', '|', '=', ',', '+', '*', '?', '<', '>', '@'];

    public static AccountName Parse(string? input, string machineName)
    {
        if (!TryParse(input, machineName, out var name, out var error))
        {
            throw new ArgumentException(error, nameof(input));
        }

        return name;
    }

    public static bool TryParse(string? input, string machineName, out AccountName name, out string error)
    {
        name = null!;
        error = string.Empty;
        ArgumentException.ThrowIfNullOrWhiteSpace(machineName);

        var text = input?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            error = "No account name given.";
            return false;
        }

        if (text.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                name = new AccountName(AccountNameKind.Sid, null, null, new SecurityIdentifier(text));
                return true;
            }
            catch (ArgumentException)
            {
                error = $"\"{text}\" is not a valid SID.";
                return false;
            }
        }

        var parts = text.Split('\\');
        if (parts.Length > 2)
        {
            error = $"Invalid account name \"{text}\": at most one \"\\\" allowed (DOMAIN\\User).";
            return false;
        }

        var user = parts[^1];
        if (!IsValidPart(user, out var userError))
        {
            error = $"Invalid account name \"{text}\": user name {userError}";
            return false;
        }

        if (parts.Length == 1)
        {
            name = new AccountName(AccountNameKind.Local, machineName, user, null);
            return true;
        }

        var domain = parts[0];
        if (domain == "." || string.Equals(domain, machineName, StringComparison.OrdinalIgnoreCase))
        {
            name = new AccountName(AccountNameKind.Local, machineName, user, null);
            return true;
        }

        if (!IsValidPart(domain, out var domainError))
        {
            error = $"Invalid account name \"{text}\": domain {domainError}";
            return false;
        }

        name = new AccountName(AccountNameKind.Domain, domain, user, null);
        return true;
    }

    private static bool IsValidPart(string part, out string error)
    {
        error = string.Empty;
        if (part.Length == 0)
        {
            error = "is missing.";
            return false;
        }

        if (part != part.Trim())
        {
            error = "must not start or end with a space.";
            return false;
        }

        if (part.IndexOfAny(InvalidChars) >= 0 || part.Any(char.IsControl))
        {
            error = part.Contains('@')
                ? "contains \"@\". Please use the form DOMAIN\\User."
                : "contains invalid characters.";
            return false;
        }

        if (part.All(c => c is '.' or ' '))
        {
            error = "must not consist only of dots and spaces.";
            return false;
        }

        return true;
    }
}
