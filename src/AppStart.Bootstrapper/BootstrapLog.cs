using System.Globalization;
using System.Text;

namespace AppStart.Bootstrapper;

/// <summary>
/// The bootstrapper's own log (C:\ProgramData\AppStart\Logs\bootstrapper-….log), kept next to the msiexec log.
/// Lines written before the protected Logs folder exists are buffered and written once it is opened.
/// </summary>
internal sealed class BootstrapLog : IDisposable
{
    private readonly List<string> pending = [];
    private StreamWriter? writer;

    public string? FilePath { get; private set; }

    public void Write(string line)
    {
        var stamped = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + line;
        if (writer is null)
        {
            pending.Add(stamped);
            return;
        }

        writer.WriteLine(stamped);
    }

    /// <summary>Opens the log file inside the (already protected) Logs folder; it inherits the folder's DACL.</summary>
    public void Open(string path)
    {
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
        FilePath = path;
        foreach (var line in pending)
        {
            writer.WriteLine(line);
        }

        pending.Clear();
    }

    public void Dispose() => writer?.Dispose();
}
