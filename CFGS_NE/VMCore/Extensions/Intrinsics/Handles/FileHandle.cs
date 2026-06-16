using System.Text;

namespace CFGS_VM.VMCore.Extensions.Intrinsics.Handles
{
    public sealed class FileHandle : IDisposable
    {
        public string Path { get; }
        public int Mode { get; }
        private FileStream? _fs;
        private readonly Encoding _textEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
        private Decoder? _decoder;
        private StreamWriter? _writer;
        private readonly bool _canRead;
        private readonly bool _canWrite;
        private readonly byte[] _singleByte = new byte[1];
        private readonly char[] _decodedChars = new char[2];
        private char? _pendingChar;
        private bool _textReadInitialized;

        public bool IsOpen => _fs != null;

        public FileHandle(string path, int mode, FileStream fs, bool canRead, bool canWrite)
        {
            Path = path;
            Mode = mode;
            _fs = fs;
            _canRead = canRead;
            _canWrite = canWrite;
            if (canRead)
                _decoder = _textEncoding.GetDecoder();
            if (canWrite)
                _writer = new StreamWriter(_fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 1024, leaveOpen: true) { AutoFlush = false };
        }

        public override string ToString() => _fs == null ? $"<file closed '{Path}'>" : $"<file '{Path}'>";

        public void Dispose()
        {
            try { _writer?.Flush(); }
            catch (ObjectDisposedException) { }
            catch (IOException) { }
            _writer?.Dispose(); _fs?.Dispose();
            _writer = null; _decoder = null; _fs = null;
            GC.SuppressFinalize(this);
        }
        ~FileHandle() { Dispose(); }

        private FileStream FS => _fs ?? throw new ObjectDisposedException(nameof(FileHandle));
        private StreamWriter Writer => _writer ?? throw new InvalidOperationException("file not opened for writing");
        private Decoder Decoder => _decoder ?? throw new InvalidOperationException("file not opened for reading");

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
            ResetTextDecoder();
        }

        private void PrepareRawWrite()
        {
            EnsureWritable();
            _writer?.Flush();
            ResetTextDecoder();
        }

        private void ResetTextDecoder()
        {
            _decoder?.Reset();
            _pendingChar = null;
            _textReadInitialized = false;
        }

        private void EnsureTextReadInitialized()
        {
            if (_textReadInitialized)
                return;

            _textReadInitialized = true;
            if (FS.Position != 0 || FS.Length < 3)
                return;

            Span<byte> bom = stackalloc byte[3];
            int read = FS.Read(bom);
            if (read == 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
                return;

            FS.Position = 0;
        }

        private char? ReadDecodedChar()
        {
            EnsureReadable();
            _writer?.Flush();

            if (_pendingChar is char pending)
            {
                _pendingChar = null;
                return pending;
            }

            EnsureTextReadInitialized();

            while (true)
            {
                int next = FS.ReadByte();
                if (next < 0)
                    return null;

                _singleByte[0] = (byte)next;
                int chars = Decoder.GetChars(_singleByte, 0, 1, _decodedChars, 0, flush: false);
                if (chars == 0)
                    continue;

                if (chars > 1)
                    _pendingChar = _decodedChars[1];

                return _decodedChars[0];
            }
        }

        private void ConsumeLineFeedAfterCarriageReturn()
        {
            if (_pendingChar is not null || FS.Position >= FS.Length)
                return;

            long position = FS.Position;
            int next = FS.ReadByte();
            if (next == '\n')
                return;

            FS.Position = position;
        }

        public void Write(string s) { Writer.Write(s); }
        public void Writeln(string s) { Writer.WriteLine(s); }
        public string Read(int count)
        {
            if (count <= 0) return string.Empty;
            StringBuilder sb = new(count);
            while (sb.Length < count)
            {
                char? ch = ReadDecodedChar();
                if (ch is null)
                    break;

                sb.Append(ch.Value);
            }

            return sb.ToString();
        }
        public string ReadLine()
        {
            StringBuilder sb = new();
            while (true)
            {
                char? ch = ReadDecodedChar();
                if (ch is null)
                    return sb.ToString();

                if (ch.Value == '\n')
                {
                    if (sb.Length > 0 && sb[^1] == '\r')
                        sb.Length--;
                    return sb.ToString();
                }

                if (ch.Value == '\r')
                {
                    ConsumeLineFeedAfterCarriageReturn();
                    return sb.ToString();
                }

                sb.Append(ch.Value);
            }
        }
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

        public bool Eof() => _pendingChar is null && FS.Position >= FS.Length;
        public long Seek(long offset, SeekOrigin origin)
        {
            _writer?.Flush();
            long position = FS.Seek(offset, origin);
            ResetTextDecoder();
            return position;
        }

        public long SetPosition(long position) => Seek(position, SeekOrigin.Begin);
        public void Close() => Dispose();
    }
}

