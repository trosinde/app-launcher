using System.Runtime.InteropServices;
using System.Security.Principal;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.Security.Authentication.Identity;

namespace AppStart.Setup.Engine.Accounts;

/// <summary>User rights assignment ("User Rights Assignment") of the local LSA policy.</summary>
internal sealed unsafe class LsaPolicy : IDisposable
{
    // ntlsa.h policy access rights
    private const uint PolicyViewLocalInformation = 0x00000001;
    private const uint PolicyCreateAccount = 0x00000010;
    private const uint PolicyLookupNames = 0x00000800;

    private LSA_HANDLE handle;

    private LsaPolicy(LSA_HANDLE handle) => this.handle = handle;

    /// <summary>Read access (LsaEnumerateAccountRights).</summary>
    public static LsaPolicy OpenForRead() => Open(PolicyLookupNames | PolicyViewLocalInformation);

    /// <summary>Write access (add/remove rights). Needs admin rights.</summary>
    public static LsaPolicy OpenForWrite() => Open(PolicyLookupNames | PolicyCreateAccount | PolicyViewLocalInformation);

    private static LsaPolicy Open(uint access)
    {
        var attributes = new LSA_OBJECT_ATTRIBUTES { Length = (uint)sizeof(LSA_OBJECT_ATTRIBUTES) };
        LSA_HANDLE h;
        AccountErrors.Check(PInvoke.LsaOpenPolicy(null, &attributes, access, &h), "Open local security policy (LsaOpenPolicy)");
        return new LsaPolicy(h);
    }

    public void AddRights(SecurityIdentifier sid, params string[] rights)
    {
        // Pin every right name and point an LSA_UNICODE_STRING at it.
        var pins = new GCHandle[rights.Length];
        var strings = new LSA_UNICODE_STRING[rights.Length];
        try
        {
            for (var i = 0; i < rights.Length; i++)
            {
                pins[i] = GCHandle.Alloc(rights[i], GCHandleType.Pinned);
                var bytes = (ushort)(rights[i].Length * sizeof(char));
                strings[i] = new LSA_UNICODE_STRING { Length = bytes, MaximumLength = bytes, Buffer = (char*)pins[i].AddrOfPinnedObject() };
            }

            fixed (LSA_UNICODE_STRING* p = strings)
            fixed (byte* s = SidBytes(sid))
            {
                AccountErrors.Check(PInvoke.LsaAddAccountRights(handle, (PSID)s, p, (uint)rights.Length),
                    $"Set user rights {string.Join(", ", rights)} for {sid}");
            }
        }
        finally
        {
            foreach (var pin in pins)
            {
                if (pin.IsAllocated)
                {
                    pin.Free();
                }
            }
        }
    }

    /// <summary>
    /// Removes all rights of the SID and with it the LSA account object, so no orphaned SID stays in the policy.
    /// No error when the SID holds no rights.
    /// </summary>
    public void RemoveAllRights(SecurityIdentifier sid)
    {
        fixed (byte* s = SidBytes(sid))
        {
            var status = PInvoke.LsaRemoveAccountRights(handle, (PSID)s, true, null, 0);
            if (status != NTSTATUS.STATUS_OBJECT_NAME_NOT_FOUND)
            {
                AccountErrors.Check(status, $"Remove user rights of {sid}");
            }
        }
    }

    /// <summary>Rights and privileges assigned directly to the SID (not via groups). Empty when it holds none.</summary>
    public IReadOnlyList<string> EnumerateRights(SecurityIdentifier sid)
    {
        fixed (byte* s = SidBytes(sid))
        {
            LSA_UNICODE_STRING* rights = null;
            uint count;
            var status = PInvoke.LsaEnumerateAccountRights(handle, (PSID)s, &rights, &count);
            if (status == NTSTATUS.STATUS_OBJECT_NAME_NOT_FOUND)
            {
                return [];
            }

            AccountErrors.Check(status, $"Read user rights of {sid}");
            try
            {
                var result = new List<string>((int)count);
                for (var i = 0; i < count; i++)
                {
                    result.Add(new string(rights[i].Buffer.Value, 0, rights[i].Length / sizeof(char)));
                }

                return result;
            }
            finally
            {
                PInvoke.LsaFreeMemory(rights);
            }
        }
    }

    public void Dispose()
    {
        if (handle != default)
        {
            PInvoke.LsaClose(handle);
            handle = default;
        }
    }

    internal static byte[] SidBytes(SecurityIdentifier sid)
    {
        var bytes = new byte[sid.BinaryLength];
        sid.GetBinaryForm(bytes, 0);
        return bytes;
    }
}
