using System.Text;

namespace CFGS_VM.VMCore.Extensions.Intrinsics.Handles
{
    public sealed class FileHandle : IDisposable
    {
        public string Path { get; }
        public int Mode { get; }
        private FileStream? _fs;
        private StreamReader? _reader;
        private StreamWriter? _writer;

        public bool IsOpen => _fs != null;

        public FileHandle(string path, int mode, FileStream fs, bool canRead, bool canWrite)
        {
            Path = path;
            Mode = mode;
            _fs = fs;
            if (canRead)
                _reader = new StreamReader(_fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
            if (canWrite)
                _writer = new StreamWriter(_fs, Encoding.UTF8, bufferSize: 1024, leaveOpen: true) { AutoFlush = false };
        }

        public override string ToString() => _fs == null ? $"<file closed '{Path}'>" : $"<file '{Path}'>";

        public void Dispose()
        {
            try { _writer?.Flush(); } catch { }
            _writer?.Dispose(); _reader?.Dispose(); _fs?.Dispose();
            _writer = null; _reader = null; _fs = null;
            GC.SuppressFinalize(this);
        }
        ~FileHandle() { Dispose(); }

        private FileStream FS => _fs ?? throw new ObjectDisposedException(nameof(FileHandle));
        private StreamWriter Writer => _writer ?? throw new InvalidOperationException("file not opened for writing");
        private StreamReader Reader => _reader ?? throw new InvalidOperationException("file not opened for reading");

        public void Write(string s) { Writer.Write(s); }
        public void Writeln(string s) { Writer.WriteLine(s); }
        public string Read(int count)
        {
            if (count <= 0) return string.Empty;
            char[] buf = new char[count];
            int read = Reader.Read(buf, 0, count);
            return new string(buf, 0, read);
        }
        public string ReadLine() => Reader.ReadLine() ?? "";
        public void Flush() => Writer.Flush();
        public long Tell() => FS.Position;
        public bool Eof() => Reader.EndOfStream && FS.Position >= FS.Length;
        public long Seek(long offset, SeekOrigin origin) => FS.Seek(offset, origin);
        public void Close() => Dispose();
    }
}

