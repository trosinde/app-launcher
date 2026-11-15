namespace AppStart.Core;

/// <summary>
/// Identifier of a released application: only a-z, 0-9 and '-', 1 to 32 characters (spec app-launch).
/// </summary>
public static class AppId
{
    public const int MaxLength = 32;

    public static bool IsValid(ReadOnlySpan<char> value)
    {
        if (value.Length is 0 or > MaxLength)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Byte variant for the pipe parser: same rule on ASCII bytes.</summary>
    public static bool IsValid(ReadOnlySpan<byte> value)
    {
        if (value.Length is 0 or > MaxLength)
        {
            return false;
        }

        foreach (var b in value)
        {
            if (b is not ((>= (byte)'a' and <= (byte)'z') or (>= (byte)'0' and <= (byte)'9') or (byte)'-'))
            {
                return false;
            }
        }

        return true;
    }
}
