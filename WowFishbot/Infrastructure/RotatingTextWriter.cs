using System.Text;

namespace WowFishbot.Infrastructure;

internal sealed class RotatingTextWriter : TextWriter
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false);
    private readonly object _gate = new();
    private readonly string _path;
    private readonly int _maxBytes;
    private readonly int _archiveCount;
    private StreamWriter _writer;

    internal RotatingTextWriter(string path, int maxBytes, int archiveCount)
    {
        _path = path;
        _maxBytes = maxBytes;
        _archiveCount = archiveCount;
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        if (File.Exists(path) && new FileInfo(path).Length > 0) RotateFiles();
        _writer = OpenWriter();
    }

    public override Encoding Encoding => Utf8;
    public override void Write(char value) => Write(value.ToString());
    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        lock (_gate)
        {
            if (_writer.BaseStream.Length > 0 && _writer.BaseStream.Length + Utf8.GetByteCount(value) > _maxBytes)
            {
                _writer.Dispose();
                RotateFiles();
                _writer = OpenWriter();
            }
            _writer.Write(value);
        }
    }

    public override void WriteLine(string? value) => Write((value ?? string.Empty) + NewLine);
    public override void Flush() { lock (_gate) _writer.Flush(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { lock (_gate) _writer.Dispose(); }
        base.Dispose(disposing);
    }

    private StreamWriter OpenWriter() => new(
        new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete), Utf8)
    { AutoFlush = true };

    private void RotateFiles()
    {
        if (_archiveCount == 0)
        {
            File.Delete(_path);
            return;
        }
        File.Delete(_path + "." + _archiveCount);
        for (var i = _archiveCount - 1; i >= 1; i--)
        {
            var source = _path + "." + i;
            if (File.Exists(source)) File.Move(source, _path + "." + (i + 1), true);
        }
        if (File.Exists(_path)) File.Move(_path, _path + ".1", true);
    }
}
