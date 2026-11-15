using System.Text;

namespace AppStart.Core.Protocol;

/// <summary>Why the service refused a start. Wire names are fixed (spec pipe-security, design D8).</summary>
public enum DenyReason
{
    UnknownApp,
    Integrity,
    Config,
    Busy,
    Error,
}

/// <summary>Parsed service answer.</summary>
public sealed record LaunchResponse(bool Success, DenyReason? Reason, long IncidentId)
{
    public static LaunchResponse Ok { get; } = new(true, null, 0);

    public static LaunchResponse Denied(DenyReason reason, long incidentId) => new(false, reason, incidentId);
}

/// <summary>
/// Wire format between launcher and service. Request: "START &lt;AppId&gt;". Response: "OK" or
/// "DENIED &lt;reason&gt; &lt;incident-id&gt;". ASCII only, at most 64 bytes, nothing else accepted.
/// </summary>
public static class PipeProtocol
{
    public const int MaxMessageBytes = 64;

    /// <summary>Incident ids are positive and at most 10 digits.</summary>
    public const long MaxIncidentId = 9_999_999_999;

    private static ReadOnlySpan<byte> StartPrefix => "START "u8;

    private static ReadOnlySpan<byte> OkMessage => "OK"u8;

    private static ReadOnlySpan<byte> DeniedPrefix => "DENIED "u8;

    public static byte[] FormatRequest(string appId)
    {
        if (!AppId.IsValid(appId))
        {
            throw new ArgumentException("Ungültige AppId.", nameof(appId));
        }

        return Encoding.ASCII.GetBytes("START " + appId);
    }

    /// <summary>Strict parser: exact prefix, valid AppId, no trailing bytes, length limit.</summary>
    public static bool TryParseRequest(ReadOnlySpan<byte> message, out string appId)
    {
        appId = string.Empty;
        if (message.Length > MaxMessageBytes || !message.StartsWith(StartPrefix))
        {
            return false;
        }

        var id = message[StartPrefix.Length..];
        if (!AppId.IsValid(id))
        {
            return false;
        }

        appId = Encoding.ASCII.GetString(id);
        return true;
    }

    public static byte[] FormatOk() => OkMessage.ToArray();

    public static byte[] FormatDenied(DenyReason reason, long incidentId)
    {
        if (incidentId is <= 0 or > MaxIncidentId)
        {
            throw new ArgumentOutOfRangeException(nameof(incidentId));
        }

        return Encoding.ASCII.GetBytes($"DENIED {ToWire(reason)} {incidentId}");
    }

    public static bool TryParseResponse(ReadOnlySpan<byte> message, out LaunchResponse response)
    {
        response = LaunchResponse.Ok;
        if (message.Length > MaxMessageBytes)
        {
            return false;
        }

        if (message.SequenceEqual(OkMessage))
        {
            return true;
        }

        if (!message.StartsWith(DeniedPrefix))
        {
            return false;
        }

        var rest = message[DeniedPrefix.Length..];
        var space = rest.IndexOf((byte)' ');
        if (space <= 0 || !TryFromWire(rest[..space], out var reason))
        {
            return false;
        }

        var digits = rest[(space + 1)..];
        if (digits.Length is 0 or > 10 || digits[0] == (byte)'0')
        {
            return false;
        }

        long id = 0;
        foreach (var b in digits)
        {
            if (b is < (byte)'0' or > (byte)'9')
            {
                return false;
            }

            id = (id * 10) + (b - '0');
        }

        response = LaunchResponse.Denied(reason, id);
        return true;
    }

    public static string ToWire(DenyReason reason) => reason switch
    {
        DenyReason.UnknownApp => "unknown-app",
        DenyReason.Integrity => "integrity",
        DenyReason.Config => "config",
        DenyReason.Busy => "busy",
        DenyReason.Error => "error",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    private static bool TryFromWire(ReadOnlySpan<byte> wire, out DenyReason reason)
    {
        foreach (var candidate in Enum.GetValues<DenyReason>())
        {
            if (wire.SequenceEqual(Encoding.ASCII.GetBytes(ToWire(candidate))))
            {
                reason = candidate;
                return true;
            }
        }

        reason = default;
        return false;
    }
}
