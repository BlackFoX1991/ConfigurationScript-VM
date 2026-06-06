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
        private readonly bool _canRead;
        private readonly bool _canWrite;

        public bool IsOpen => _fs != null;

        public FileHandle(string path, int mode, FileStream fs, bool canRead, bool canWrite)
        {
            Path = path;
            Mode = mode;
            _fs = fs;
            _canRead = canRead;
            _canWrite = canWrite;
            if (canRead)
                _reader = new StreamReader(_fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
            if (canWrite)
                _writer = new StreamWriter(_fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 1024, leaveOpen: true) { AutoFlush = false };
        }

        public override string ToString() => _fs == null ? $"<file closed '{Path}'>" : $"<file '{Path}'>";

        public void Dispose()
        {
            try { _writer?.Flush(); }
            catch (ObjectDisposedException) { }
            catch (IOException) { }
            _writer?.Dispose(); _reader?.Dispose(); _fs?.Dispose();
            _writer = null; _reader = null; _fs = null;
            GC.SuppressFinalize(this);
        }
        ~FileHandle() { Dispose(); }

        private FileStream FS => _fs ?? throw new ObjectDisposedException(nameof(FileHandle));
        private StreamWriter Writer => _writer ?? throw new InvalidOperationException("file not opened for writing");
        private StreamReader Reader => _reader ?? throw new InvalidOperationException("file not opened for reading");

        private void EnsureReadable()
        {
            if (!_canRead)
                throw new InvalidOperationException("file not opened for reading");
        }

        private void EnsureWritable()
        {
            if (!_canWrite)
                throw new InvalidOperationException("file not opened for writing");
        }

        private void PrepareRawRead()
        {
            EnsureReadable();
            _writer?.Flush();
            _reader?.DiscardBufferedData();
        }

        private void PrepareRawWrite()
        {
            EnsureWritable();
            _writer?.Flush();
            _reader?.DiscardBufferedData();
        }

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
        public int ReadByte()
        {
            PrepareRawRead();
            return FS.ReadByte();
        }

        public byte[] ReadBytes(int count)
        {
            PrepareRawRead();
            if (count <= 0)
                return Array.Empty<byte>();

            byte[] buffer = new byte[count];
            int total = 0;
            while (total < count)
            {
                int read = FS.Read(buffer, total, count - total);
                if (read <= 0)
                    break;

                total += read;
            }

            if (total == count)
                return buffer;

            byte[] trimmed = new byte[total];
            Array.Copy(buffer, trimmed, total);
            return trimmed;
        }

        public void WriteByte(byte value)
        {
            PrepareRawWrite();
            FS.WriteByte(value);
        }

        public void WriteBytes(byte[] bytes)
        {
            PrepareRawWrite();
            if (bytes == null || bytes.Length == 0)
                return;

            FS.Write(bytes, 0, bytes.Length);
        }

        public long Tell()
        {
            _writer?.Flush();
            return FS.Position;
        }

        public long Length()
        {
            _writer?.Flush();
            return FS.Length;
        }

        public bool Eof() => Reader.EndOfStream && FS.Position >= FS.Length;
        public long Seek(long offset, SeekOrigin origin)
        {
            _writer?.Flush();
            long position = FS.Seek(offset, origin);
            _reader?.DiscardBufferedData();
            return position;
        }

        public long SetPosition(long position) => Seek(position, SeekOrigin.Begin);
        public void Close() => Dispose();
    }
}

