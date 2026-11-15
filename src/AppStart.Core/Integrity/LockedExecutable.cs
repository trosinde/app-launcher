namespace AppStart.Core.Integrity;

/// <summary>
/// An EXE opened so that nobody can write, rename or delete it while the handle is held
/// (FileShare.Read only). The service verifies through this handle and keeps it open until
/// the process is created, so check and start see the same content (design D7).
/// </summary>
public sealed class LockedExecutable : IDisposable
{
    private LockedExecutable(string path, FileStream stream)
    {
        Path = path;
        Stream = stream;
    }

    public string Path { get; }

    public FileStream Stream { get; }

    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="IOException">The file is open for writing by someone else.</exception>
    /// <exception cref="UnauthorizedAccessException">No read access.</exception>
    public static LockedExecutable Open(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, FileOptions.SequentialScan);
        return new LockedExecutable(path, stream);
    }

    public void Dispose() => Stream.Dispose();
}
