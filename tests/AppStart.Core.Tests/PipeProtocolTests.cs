using System.Text;
using AppStart.Core.Protocol;

namespace AppStart.Core.Tests;

public class PipeProtocolTests
{
    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    [Fact]
    public void Valid_request_is_parsed()
    {
        Assert.True(PipeProtocol.TryParseRequest(Ascii("START app-a"), out var id));
        Assert.Equal("app-a", id);
        Assert.Equal(Ascii("START app-a"), PipeProtocol.FormatRequest("app-a"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("START")]
    [InlineData("START ")]
    [InlineData("start app-a")]
    [InlineData("START  app-a")]
    [InlineData("START app-a ")]
    [InlineData("START app-a\n")]
    [InlineData("START app-a\r\n")]
    [InlineData("START App-A")]
    [InlineData("START app-a C:\\Windows\\System32\\cmd.exe")]
    [InlineData("STOP app-a")]
    [InlineData(" START app-a")]
    public void Malformed_request_is_rejected(string message) =>
        Assert.False(PipeProtocol.TryParseRequest(Ascii(message), out _));

    [Fact]
    public void Request_over_64_bytes_is_rejected()
    {
        var tooLong = new byte[65];
        "START "u8.CopyTo(tooLong);
        tooLong.AsSpan(6).Fill((byte)'a');
        Assert.False(PipeProtocol.TryParseRequest(tooLong, out _));
    }

    [Fact]
    public void Non_ascii_and_nul_are_rejected()
    {
        Assert.False(PipeProtocol.TryParseRequest(Encoding.UTF8.GetBytes("START äpp"), out _));
        Assert.False(PipeProtocol.TryParseRequest([.. "START app"u8, 0, .. "x"u8], out _));
    }

    [Fact]
    public void Invalid_app_id_cannot_be_formatted() =>
        Assert.Throws<ArgumentException>(() => PipeProtocol.FormatRequest("../cmd"));

    [Fact]
    public void Ok_response_roundtrips()
    {
        Assert.True(PipeProtocol.TryParseResponse(PipeProtocol.FormatOk(), out var r));
        Assert.True(r.Success);
    }

    [Theory]
    [InlineData(DenyReason.UnknownApp, 1, "DENIED unknown-app 1")]
    [InlineData(DenyReason.Integrity, 4711, "DENIED integrity 4711")]
    [InlineData(DenyReason.Config, 9_999_999_999, "DENIED config 9999999999")]
    [InlineData(DenyReason.Busy, 12, "DENIED busy 12")]
    [InlineData(DenyReason.Error, 99, "DENIED error 99")]
    public void Denied_response_roundtrips(DenyReason reason, long id, string wire)
    {
        var bytes = PipeProtocol.FormatDenied(reason, id);
        Assert.Equal(wire, Encoding.ASCII.GetString(bytes));
        Assert.True(bytes.Length <= PipeProtocol.MaxMessageBytes);

        Assert.True(PipeProtocol.TryParseResponse(bytes, out var r));
        Assert.False(r.Success);
        Assert.Equal(reason, r.Reason);
        Assert.Equal(id, r.IncidentId);
    }

    [Theory]
    [InlineData("ok")]
    [InlineData("OK ")]
    [InlineData("DENIED")]
    [InlineData("DENIED integrity")]
    [InlineData("DENIED integrity 0")]
    [InlineData("DENIED integrity 012")]
    [InlineData("DENIED integrity 12345678901")]
    [InlineData("DENIED integrity -5")]
    [InlineData("DENIED tampered 5")]
    [InlineData("DENIED integrity 5 C:\\x.exe")]
    public void Malformed_response_is_rejected(string message) =>
        Assert.False(PipeProtocol.TryParseResponse(Ascii(message), out _));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10_000_000_000)]
    public void Incident_id_out_of_range_is_refused(long id) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PipeProtocol.FormatDenied(DenyReason.Error, id));
}
