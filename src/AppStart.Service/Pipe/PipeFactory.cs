using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using AppStart.Core;
using AppStart.Core.Protocol;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Pipes;

namespace AppStart.Service.Pipe;

/// <summary>Raised when \\.\pipe\AppStart exists before the service creates it (possible fake pipe).</summary>
public sealed class PipeAlreadyExistsException(string message) : Exception(message);

/// <summary>
/// Creates instances of \\.\pipe\AppStart with exactly the flags of design D9: message mode, 64-byte
/// buffers, PIPE_REJECT_REMOTE_CLIENTS, FILE_FLAG_FIRST_PIPE_INSTANCE for the first instance, and a DACL
/// that lets only "AppStart Users" connect (without FILE_CREATE_PIPE_INSTANCE) and denies network logons.
/// </summary>
internal static unsafe class PipeFactory
{
    public const int MaxInstances = 4;

    /// <summary>Client rights: FILE_GENERIC_READ | FILE_WRITE_DATA | FILE_WRITE_ATTRIBUTES (no FILE_CREATE_PIPE_INSTANCE).</summary>
    public const string ClientRights = "0x12018b";

    public static string BuildSddl(SecurityIdentifier operatorGroup) =>
        $"D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;{ClientRights};;;{operatorGroup.Value})(D;;GA;;;NU)";

    public static SecurityIdentifier ResolveOperatorGroup() =>
        (SecurityIdentifier)new NTAccount(AppStartConstants.OperatorGroupName).Translate(typeof(SecurityIdentifier));

    public static NamedPipeServerStream Create(string sddl, bool firstInstance)
    {
        PSECURITY_DESCRIPTOR descriptor;
        fixed (char* s = sddl)
        {
            if (!PInvoke.ConvertStringSecurityDescriptorToSecurityDescriptor(s, PInvoke.SDDL_REVISION_1, &descriptor, null))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "ConvertStringSecurityDescriptorToSecurityDescriptor");
            }
        }

        try
        {
            var attributes = new SECURITY_ATTRIBUTES
            {
                nLength = (uint)sizeof(SECURITY_ATTRIBUTES),
                lpSecurityDescriptor = descriptor.Value,
                bInheritHandle = false,
            };

            var openMode = FILE_FLAGS_AND_ATTRIBUTES.PIPE_ACCESS_DUPLEX | FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_OVERLAPPED;
            if (firstInstance)
            {
                openMode |= FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_FIRST_PIPE_INSTANCE;
            }

            var pipeMode = NAMED_PIPE_MODE.PIPE_TYPE_MESSAGE | NAMED_PIPE_MODE.PIPE_READMODE_MESSAGE
                           | NAMED_PIPE_MODE.PIPE_WAIT | NAMED_PIPE_MODE.PIPE_REJECT_REMOTE_CLIENTS;

            HANDLE handle;
            fixed (char* name = @"\\.\pipe\" + AppStartConstants.PipeName)
            {
                handle = PInvoke.CreateNamedPipe(name, openMode, pipeMode, MaxInstances,
                    PipeProtocol.MaxMessageBytes, PipeProtocol.MaxMessageBytes, 0, &attributes);
            }

            if (handle == (HANDLE)(-1))
            {
                var error = Marshal.GetLastWin32Error();
                // With FILE_FLAG_FIRST_PIPE_INSTANCE any failure while the name already exists means someone
                // else created it first, whatever error code Windows reports (5, 231, 87 ...).
                if (firstInstance && PipeExists())
                {
                    throw new PipeAlreadyExistsException(
                        $"\\\\.\\pipe\\{AppStartConstants.PipeName} existiert bereits (Windows-Fehler {error}). Möglicherweise eine gefälschte Pipe.");
                }

                throw new Win32Exception(error, $"CreateNamedPipe: Windows-Fehler {error} ({new Win32Exception(error).Message})");
            }

            var safe = new SafePipeHandle((IntPtr)handle.Value, ownsHandle: true);
            var stream = new NamedPipeServerStream(PipeDirection.InOut, isAsync: true, isConnected: false, safe);
            // .NET does not learn the mode from a foreign handle; without this IsMessageComplete throws.
            stream.ReadMode = PipeTransmissionMode.Message;
            return stream;
        }
        finally
        {
            PInvoke.LocalFree((HLOCAL)descriptor.Value);
        }
    }

    private static bool PipeExists() =>
        Directory.GetFiles(@"\\.\pipe\").Any(p => string.Equals(Path.GetFileName(p), AppStartConstants.PipeName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Session of the connected client, taken from the pipe (never from the message).</summary>
    public static uint GetClientSessionId(NamedPipeServerStream server)
    {
        uint session;
        if (!PInvoke.GetNamedPipeClientSessionId((HANDLE)server.SafePipeHandle.DangerousGetHandle(), &session))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetNamedPipeClientSessionId");
        }

        return session;
    }
}
