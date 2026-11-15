using System.Runtime.InteropServices;
using System.Security;

namespace AppStart.Setup.ViewModels;

/// <summary>Compares SecureStrings without creating managed string copies of the password.</summary>
internal static class SecureStrings
{
    public static bool AreEqual(SecureString? a, SecureString? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        if (a.Length != b.Length)
        {
            return false;
        }

        var pa = IntPtr.Zero;
        var pb = IntPtr.Zero;
        try
        {
            pa = Marshal.SecureStringToGlobalAllocUnicode(a);
            pb = Marshal.SecureStringToGlobalAllocUnicode(b);
            var diff = 0;
            for (var i = 0; i < a.Length * 2; i++)
            {
                diff |= Marshal.ReadByte(pa, i) ^ Marshal.ReadByte(pb, i);
            }

            return diff == 0;
        }
        finally
        {
            if (pa != IntPtr.Zero)
            {
                Marshal.ZeroFreeGlobalAllocUnicode(pa);
            }

            if (pb != IntPtr.Zero)
            {
                Marshal.ZeroFreeGlobalAllocUnicode(pb);
            }
        }
    }
}
