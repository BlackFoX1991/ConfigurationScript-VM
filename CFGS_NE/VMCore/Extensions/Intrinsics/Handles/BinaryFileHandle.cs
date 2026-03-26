namespace CFGS_VM.VMCore.Extensions.Intrinsics.Handles
{
    public sealed class BinaryFileHandle : IDisposable
    {
        public string Path { get; }
        public int Mode { get; }

        private FileStream? _fs;
        private readonly bool _canRead;
        private readonly bool _canWrite;

        public bool IsOpen => _fs != null;

        public BinaryFileHandle(string path, int mode, FileStream fs, bool canRead, bool canWrite)
        {
            Path = path;
            Mode = mode;
            _fs = fs;
            _canRead = canRead;
            _canWrite = canWrite;
        }

        public override string ToString() => _fs == null ? $"<binary-file closed '{Path}'>" : $"<binary-file '{Path}'>";

        public void Dispose()
        {
            _fs?.Dispose();
            _fs = null;
            GC.SuppressFinalize(this);
        }

        ~BinaryFileHandle() { Dispose(); }

        private FileStream FS => _fs ?? throw new ObjectDisposedException(nameof(BinaryFileHandle));

        private void EnsureReadable()
        {
            if (!_canRead)
                throw new InvalidOperationException("binary file not opened for reading");
        }

        private void EnsureWritable()
        {
            if (!_canWrite)
                throw new InvalidOperationException("binary file not opened for writing");
        }

        public int ReadByte()
        {
            EnsureReadable();
            return FS.ReadByte();
        }

        public byte[] ReadBytes(int count)
        {
            EnsureReadable();
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
            EnsureWritable();
            FS.WriteByte(value);
        }

        public void WriteBytes(byte[] bytes)
        {
            EnsureWritable();
            if (bytes == null || bytes.Length == 0)
                return;

            FS.Write(bytes, 0, bytes.Length);
        }

        public void Flush()
        {
            EnsureWritable();
            FS.Flush();
        }

        public long Tell() => FS.Position;

        public bool Eof()
        {
            EnsureReadable();
            return FS.Position >= FS.Length;
        }

        public long Seek(long offset, SeekOrigin origin) => FS.Seek(offset, origin);

        public void Close() => Dispose();
    }
}
