using System.Security.Cryptography;

namespace AppStart.Setup.Engine.Accounts;

/// <summary>
/// Random password for appstart-app that nobody knows and that is never stored: 32 bytes from
/// <see cref="RandomNumberGenerator"/> as Base64 (44 characters) plus one random character of each class
/// (upper, lower, digit, symbol), so every complexity policy is met. Writes into a caller-owned buffer
/// (no managed string), the caller zeroes it after use.
/// </summary>
internal static class PasswordGenerator
{
    public const int RandomBytes = 32;

    /// <summary>Characters written by <see cref="Generate"/> (without terminator).</summary>
    public const int Length = 44 + 4;

    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!#$%&()*+-.:;<=>?@_";

    /// <summary>Fills <paramref name="destination"/> (at least <see cref="Length"/> chars) and returns the number written.</summary>
    public static int Generate(Span<char> destination)
    {
        if (destination.Length < Length)
        {
            throw new ArgumentException($"Puffer zu klein, mindestens {Length} Zeichen nötig.", nameof(destination));
        }

        Span<byte> bytes = stackalloc byte[RandomBytes];
        try
        {
            RandomNumberGenerator.Fill(bytes);
            if (!Convert.TryToBase64Chars(bytes, destination, out var written) || written != 44)
            {
                throw new InvalidOperationException("Zufallspasswort konnte nicht erzeugt werden.");
            }

            destination[44] = Upper[RandomNumberGenerator.GetInt32(Upper.Length)];
            destination[45] = Lower[RandomNumberGenerator.GetInt32(Lower.Length)];
            destination[46] = Digits[RandomNumberGenerator.GetInt32(Digits.Length)];
            destination[47] = Symbols[RandomNumberGenerator.GetInt32(Symbols.Length)];
            return Length;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
