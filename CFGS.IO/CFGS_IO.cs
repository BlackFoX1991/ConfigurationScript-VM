using CFGS_VM.VMCore.Extensions.Instance;
using CFGS_VM.VMCore.Extensions.Intrinsics.Handles;
using CFGS_VM.VMCore.Extensions.Core;
using CFGS_VM.VMCore.Plugin;
using CFGS_VM.VMCore;
using CFGS_VM.VMCore.Extensions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace CFGS.IO
{
    /// <summary>
    /// File-system plugin for CFGS. Provides the <c>fs</c>/<c>file</c> namespace, file and binary
    /// file handles, and DirectoryInfo intrinsics. Loaded explicitly via
    /// <c>import "CFGS.IO.dll"</c>; the non-IO standard library is built into the core runtime.
    /// </summary>
    public sealed class CFGS_IO : IVmPlugin
    {
        /// <summary>
        /// Process-wide opt-out for file I/O. Combined with the VM-scoped ambient flag so an
        /// embedding host can deny I/O per run; both must allow it.
        /// </summary>
        public static bool AllowFileIO { get; set; } = true;

        private static bool IsFileIoAllowed => AllowFileIO && VM.AllowFileIO;

        public void Register(IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics, INamespaceRegistry namespaces)
        {
            RegisterFile(intrinsics);
            RegisterBinaryFile(intrinsics);
            RegisterDirectoryInfo(intrinsics);
            RegisterFileSystemNamespace(intrinsics);
            RegisterNamespaces(namespaces);
        }

        private static void RegisterNamespaces(INamespaceRegistry namespaces)
        {
            RegisterNamespaceAliases(namespaces, "fs", FileSystemNamespace.Instance, FileSystemNamespaceMembers);
            RegisterNamespaceAliases(namespaces, "file", FileSystemNamespace.Instance, FileSystemNamespaceMembers);
        }

        private static readonly string[] FileSystemNamespaceMembers =
        [
            "open", "openBinary", "delete", "move",
            "copy", "exists", "readAllBytes", "readAllBytesAsync",
            "writeAllBytes", "readTextAsync", "writeTextAsync", "appendTextAsync", "size",
            "lastWriteTime", "lastWriteTimeUtc", "setWorkspace", "getWorkspace",
            "getDirectory", "directoryInfo"
        ];

        private static void RegisterNamespaceAliases(INamespaceRegistry namespaces, string name, object instance, IEnumerable<string> members)
        {
            string[] materializedMembers = members.ToArray();
            namespaces.Register(new NamespaceDescriptor(name, instance, materializedMembers));
            namespaces.Register(new NamespaceDescriptor($"std.{name}", instance, materializedMembers));
        }

        private static object FileDeleteVm(List<object> args, Instruction instr)
        {
            EnsureFileIo(instr);
            string path = ResolveRequiredWorkspacePath(args[0], instr, "fs.delete");

            if (!File.Exists(path))
                return 0;

            File.Delete(path);
            return 1;
        }

        private static object FileMoveVm(List<object> args, Instruction instr)
        {
            EnsureFileIo(instr);
            string originalDestination = args[1]?.ToString() ?? string.Empty;
            string source = ResolveRequiredWorkspacePath(args[0], instr, "fs.move");
            string destination = ResolveRequiredWorkspacePath(args[1], instr, "fs.move");
            var overwrite = args.Count >= 3 && Convert.ToBoolean(args[2], CultureInfo.InvariantCulture);

            var parent = Path.GetDirectoryName(Path.GetFullPath(destination));
            if (!string.IsNullOrWhiteSpace(parent))
                Directory.CreateDirectory(parent);

            File.Move(source, destination, overwrite);
            return originalDestination;
        }

        private static object FileCopyVm(List<object> args, Instruction instr)
        {
            EnsureFileIo(instr);
            string originalDestination = args[1]?.ToString() ?? string.Empty;
            string source = ResolveRequiredWorkspacePath(args[0], instr, "fs.copy");
            string destination = ResolveRequiredWorkspacePath(args[1], instr, "fs.copy");
            var overwrite = args.Count >= 3 && Convert.ToBoolean(args[2], CultureInfo.InvariantCulture);

            var parent = Path.GetDirectoryName(Path.GetFullPath(destination));
            if (!string.IsNullOrWhiteSpace(parent))
                Directory.CreateDirectory(parent);

            File.Copy(source, destination, overwrite);
            return originalDestination;
        }

        private static object FileOpenVm(List<object> args, Instruction instr)
        {
            (string path, int mode, FileStream fs, bool canRead, bool canWrite) = OpenFileStream(args, instr, "fs.open");
            return new FileHandle(path, mode, fs, canRead, canWrite);
        }

        private static object BinaryFileOpenVm(List<object> args, Instruction instr)
        {
            (string path, int mode, FileStream fs, bool canRead, bool canWrite) = OpenFileStream(args, instr, "fs.openBinary");
            return new BinaryFileHandle(path, mode, fs, canRead, canWrite);
        }

        private static object ReadAllBytesVm(List<object> args, Instruction instr)
        {
            EnsureFileIo(instr);
            string path = ResolveRequiredWorkspacePath(args[0], instr, "readAllBytes");
            try
            {
                return ToVmByteArray(File.ReadAllBytes(path));
            }
            catch (Exception ex)
            {
                throw new VMException(
                    $"Runtime error: readAllBytes('{path}') failed: {ex.GetType().Name}: {ex.Message}",
                    instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
            }
        }

        private static object ReadAllBytesAsyncVm(List<object> args, Instruction instr)
        {
            EnsureFileIo(instr);
            string path = ResolveRequiredWorkspacePath(args[0], instr, "readAllBytesAsync");
            return Task.Run<object?>(async () =>
            {
                try
                {
                    byte[] bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
                    return ToVmByteArray(bytes);
                }
                catch (Exception ex)
                {
                    throw new VMException(
                        $"Runtime error: readAllBytesAsync('{path}') failed: {ex.GetType().Name}: {ex.Message}",
                        instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
                }
            });
        }

        private static object WriteAllBytesVm(List<object> args, Instruction instr)
        {
            EnsureFileIo(instr);
            string path = ResolveRequiredWorkspacePath(args[0], instr, "writeAllBytes");
            byte[] bytes = ConvertVmValueToBytes(args[1], instr, "data");
            try
            {
                File.WriteAllBytes(path, bytes);
                return bytes.Length;
            }
            catch (Exception ex)
            {
                throw new VMException(
                    $"Runtime error: writeAllBytes('{path}', data) failed: {ex.GetType().Name}: {ex.Message}",
                    instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
            }
        }

        private static object ReadTextAsyncVm(List<object> args, Instruction instr)
        {
            EnsureFileIo(instr);
            string path = ResolveRequiredWorkspacePath(args[0], instr, "readTextAsync");
            return Task.Run<object?>(async () => await File.ReadAllTextAsync(path).ConfigureAwait(false));
        }

        private static object WriteTextAsyncVm(List<object> args, Instruction instr)
        {
            EnsureFileIo(instr);
            string path = ResolveRequiredWorkspacePath(args[0], instr, "writeTextAsync");
            string text = args[1]?.ToString() ?? "";
            return Task.Run<object?>(async () => { await File.WriteAllTextAsync(path, text).ConfigureAwait(false); return 1; });
        }

        private static object AppendTextAsyncVm(List<object> args, Instruction instr)
        {
            EnsureFileIo(instr);
            string path = ResolveRequiredWorkspacePath(args[0], instr, "appendTextAsync");
            string text = args[1]?.ToString() ?? "";
            return Task.Run<object?>(async () => { await File.AppendAllTextAsync(path, text).ConfigureAwait(false); return 1; });
        }

        private static object FileSizeVm(List<object> args, Instruction instr)
        {
            EnsureFileIo(instr);
            string path = ResolveRequiredWorkspacePath(args[0], instr, "fs.size");
            return File.Exists(path) ? new FileInfo(path).Length : -1L;
        }

        private static object FileLastWriteTimeVm(List<object> args, Instruction instr)
        {
            EnsureFileIo(instr);
            string path = ResolveRequiredWorkspacePath(args[0], instr, "fs.lastWriteTime");
            return File.GetLastWriteTime(path);
        }

        private static object FileLastWriteTimeUtcVm(List<object> args, Instruction instr)
        {
            EnsureFileIo(instr);
            string path = ResolveRequiredWorkspacePath(args[0], instr, "fs.lastWriteTimeUtc");
            return File.GetLastWriteTimeUtc(path);
        }

        private static object FileExistsVm(List<object> args, Instruction instr)
        {
            EnsureFileIo(instr);
            if (args[0] is not string)
                return false;
            return File.Exists(ResolveWorkspacePath(args[0].ToString() ?? string.Empty));
        }

        private static object SetWorkspaceVm(List<object> args, Instruction instr)
        {
            EnsureFileIo(instr);
            if (args[0] is not string path)
                throw new VMException("Runtime error: fs.setWorkspace requires a string path",
                    instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
            try
            {
                string resolved = ResolveWorkspacePath(path);
                if (!Directory.Exists(resolved))
                    throw new DirectoryNotFoundException(resolved);

                if (VM.CurrentVm is not VM vm)
                    throw new InvalidOperationException("no current VM is available");

                vm.WorkingDirectory = resolved;
                return 1;
            }
            catch (Exception ex)
            {
                throw new VMException($"Runtime error: fs.setWorkspace('{path}') failed: {ex.GetType().Name}: {ex.Message}",
                    instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
            }
        }

        private static object GetWorkspaceVm(Instruction instr)
        {
            EnsureFileIo(instr);
            return VM.CurrentVm?.WorkingDirectory ?? Environment.CurrentDirectory;
        }

        private static object GetDirectoryVm(List<object> args, Instruction instr)
        {
            EnsureFileIo(instr);
            return Path.GetDirectoryName(ResolveWorkspacePath(args[0]?.ToString() ?? "")) ?? "";
        }

        private static object DirectoryInfoVm(List<object> args, Instruction instr)
        {
            EnsureFileIo(instr);
            return new DirectoryInfo(ResolveWorkspacePath(args[0]?.ToString() ?? ""));
        }

        private static void RegisterFileSystemNamespace(IIntrinsicRegistry intrinsics)
        {
            Type T = typeof(FileSystemNamespace);

            intrinsics.Register(T, new IntrinsicDescriptor("open", 2, 2, (recv, a, instr) => FileOpenVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("openBinary", 2, 2, (recv, a, instr) => BinaryFileOpenVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("delete", 1, 1, (recv, a, instr) => FileDeleteVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("move", 2, 3, (recv, a, instr) => FileMoveVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("copy", 2, 3, (recv, a, instr) => FileCopyVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("exists", 1, 1, (recv, a, instr) => FileExistsVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("readAllBytes", 1, 1, (recv, a, instr) => ReadAllBytesVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("readAllBytesAsync", 1, 1, (recv, a, instr) => ReadAllBytesAsyncVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("writeAllBytes", 2, 2, (recv, a, instr) => WriteAllBytesVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("readTextAsync", 1, 1, (recv, a, instr) => ReadTextAsyncVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("writeTextAsync", 2, 2, (recv, a, instr) => WriteTextAsyncVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("appendTextAsync", 2, 2, (recv, a, instr) => AppendTextAsyncVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("size", 1, 1, (recv, a, instr) => FileSizeVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("lastWriteTime", 1, 1, (recv, a, instr) => FileLastWriteTimeVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("lastWriteTimeUtc", 1, 1, (recv, a, instr) => FileLastWriteTimeUtcVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("setWorkspace", 1, 1, (recv, a, instr) => SetWorkspaceVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("getWorkspace", 0, 0, (recv, a, instr) => GetWorkspaceVm(instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("getDirectory", 1, 1, (recv, a, instr) => GetDirectoryVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("directoryInfo", 1, 1, (recv, a, instr) => DirectoryInfoVm(a, instr)));
        }

        private static void EnsureFileIo(Instruction instr)
        {
            if (IsFileIoAllowed)
                return;

            throw new VMException("Runtime error: file I/O is disabled (AllowFileIO=false)", instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
        }

        private static string ResolveWorkspacePath(string path)
        {
            if (VM.CurrentVm is VM vm)
                return vm.ResolveWorkspacePath(path);

            string expanded = Environment.ExpandEnvironmentVariables((path ?? string.Empty).Trim());
            return Path.GetFullPath(expanded);
        }

        private static string ResolveRequiredWorkspacePath(object? value, Instruction instr, string opName)
        {
            string path = value?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path))
                throw new VMException(
                    $"Runtime error: {opName} requires a non-empty path",
                    instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);

            return ResolveWorkspacePath(path);
        }

        private static byte ConvertToByteValue(object value, Instruction instr, string name)
        {
            try
            {
                int intValue = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                if (intValue is < 0 or > 255)
                {
                    throw new VMException(
                        $"Runtime error: {name} must be between 0 and 255",
                        instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
                }

                return (byte)intValue;
            }
            catch (VMException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new VMException(
                    $"Runtime error: {name} is not a valid byte ({ex.GetType().Name}: {ex.Message})",
                    instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
            }
        }

        private static byte[] ConvertVmValueToBytes(object? value, Instruction instr, string name)
        {
            if (value is not List<object> list)
            {
                throw new VMException(
                    $"Runtime error: {name} must be an array of byte values",
                    instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
            }

            return ConvertVmArrayToBytes(list, instr, name);
        }

        private static byte[] ConvertVmArrayToBytes(List<object> list, Instruction instr, string name)
        {
            byte[] bytes = new byte[list.Count];
            for (int i = 0; i < list.Count; i++)
                bytes[i] = ConvertToByteValue(list[i], instr, $"{name}[{i}]");

            return bytes;
        }

        private static List<object> ToVmByteArray(byte[] bytes)
        {
            List<object> result = new(bytes.Length);
            foreach (byte b in bytes)
                result.Add((int)b);
            return result;
        }

        private static (string Path, int Mode) ParseFileOpenArgs(List<object> args, Instruction instr, string opName)
        {
            object a0 = args[0];
            object a1 = args[1];

            if (a0 is string or char)
                return (a0.ToString() ?? "", Convert.ToInt32(a1, CultureInfo.InvariantCulture));

            if (a1 is string or char)
                return (a1.ToString() ?? "", Convert.ToInt32(a0, CultureInfo.InvariantCulture));

            throw new VMException(
                $"Runtime error: {opName} needs a string path and a numeric mode",
                instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
        }

        private static (FileMode FileMode, FileAccess Access, bool CanRead, bool CanWrite) ResolveFileOpenMode(int mode, Instruction instr, string opName)
        {
            return mode switch
            {
                0 => (FileMode.Open, FileAccess.Read, true, false),
                1 => (FileMode.OpenOrCreate, FileAccess.ReadWrite, true, true),
                2 => (FileMode.Create, FileAccess.Write, false, true),
                3 => (FileMode.Append, FileAccess.Write, false, true),
                4 => (FileMode.OpenOrCreate, FileAccess.Write, false, true),
                _ => throw new VMException(
                    $"Runtime error: invalid {opName} mode {mode} (0..4 expected)",
                    instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!)
            };
        }

        private static (string Path, int Mode, FileStream Stream, bool CanRead, bool CanWrite) OpenFileStream(List<object> args, Instruction instr, string opName)
        {
            EnsureFileIo(instr);

            (string path, int mode) = ParseFileOpenArgs(args, instr, opName);
            path = ResolveRequiredWorkspacePath(path, instr, opName);
            (FileMode fileMode, FileAccess access, bool canRead, bool canWrite) = ResolveFileOpenMode(mode, instr, opName);

            try
            {
                FileStream fs = new(path, fileMode, access, FileShare.Read);
                if (mode == 3)
                    fs.Seek(0, SeekOrigin.End);

                return (path, mode, fs, canRead, canWrite);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new VMException(
                    $"Runtime error: {opName} unauthorized for '{path}' (mode={mode}): {ex.GetType().Name}: {ex.Message}",
                    instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
            }
            catch (DirectoryNotFoundException ex)
            {
                throw new VMException(
                    $"Runtime error: {opName} path not found '{path}' (mode={mode}): {ex.GetType().Name}: {ex.Message}",
                    instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
            }
            catch (FileNotFoundException ex)
            {
                throw new VMException(
                    $"Runtime error: {opName} file not found '{path}' (mode={mode}): {ex.GetType().Name}: {ex.Message}",
                    instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
            }
            catch (IOException ex)
            {
                throw new VMException(
                    $"Runtime error: {opName} I/O error for '{path}' (mode={mode}): {ex.GetType().Name}: {ex.Message}",
                    instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
            }
            catch (Exception ex)
            {
                throw new VMException(
                    $"Runtime error: {opName}('{path}', {mode}) failed: {ex.GetType().Name}: {ex.Message}",
                    instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
            }
        }

        private static SeekOrigin ToSeekOrigin(object value)
        {
            int origin = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            return origin switch
            {
                0 => SeekOrigin.Begin,
                1 => SeekOrigin.Current,
                2 => SeekOrigin.End,
                _ => SeekOrigin.Begin
            };
        }

        private static void RegisterDirectoryInfo(IIntrinsicRegistry intrinsics)
        {
            Type T = typeof(DirectoryInfo);

            static DateTime ParseDt(object? v)
            {
                if (v is DateTime dt) return dt;
                string s = v?.ToString() ?? "";
                if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime p)) return p;
                if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long secs))
                    return DateTimeOffset.FromUnixTimeSeconds(secs).LocalDateTime;
                return DateTime.Now;
            }

            intrinsics.Register(T, new IntrinsicDescriptor("exists", 0, 0, (r, a, instr) => ((DirectoryInfo)r).Exists));
            intrinsics.Register(T, new IntrinsicDescriptor("fullName", 0, 0, (r, a, instr) => ((DirectoryInfo)r).FullName));
            intrinsics.Register(T, new IntrinsicDescriptor("name", 0, 0, (r, a, instr) => ((DirectoryInfo)r).Name));
            intrinsics.Register(T, new IntrinsicDescriptor("parent", 0, 0, (r, a, instr) => ((DirectoryInfo)r).Parent!));
            intrinsics.Register(T, new IntrinsicDescriptor("root", 0, 0, (r, a, instr) => ((DirectoryInfo)r).Root));
            intrinsics.Register(T, new IntrinsicDescriptor("attributes", 0, 0, (r, a, instr) => (long)((DirectoryInfo)r).Attributes));

            intrinsics.Register(T, new IntrinsicDescriptor("creationTime", 0, 0, (r, a, instr) => ((DirectoryInfo)r).CreationTime));
            intrinsics.Register(T, new IntrinsicDescriptor("lastAccessTime", 0, 0, (r, a, instr) => ((DirectoryInfo)r).LastAccessTime));
            intrinsics.Register(T, new IntrinsicDescriptor("lastWriteTime", 0, 0, (r, a, instr) => ((DirectoryInfo)r).LastWriteTime));

            intrinsics.Register(T, new IntrinsicDescriptor("setCreationTime", 1, 1, (r, a, instr) =>
            {
                EnsureFileIo(instr);
                DirectoryInfo di = (DirectoryInfo)r; di.CreationTime = ParseDt(a[0]); return di;
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("setLastAccessTime", 1, 1, (r, a, instr) =>
            {
                EnsureFileIo(instr);
                DirectoryInfo di = (DirectoryInfo)r; di.LastAccessTime = ParseDt(a[0]); return di;
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("setLastWriteTime", 1, 1, (r, a, instr) =>
            {
                EnsureFileIo(instr);
                DirectoryInfo di = (DirectoryInfo)r; di.LastWriteTime = ParseDt(a[0]); return di;
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("setAttributes", 1, 1, (r, a, instr) =>
            {
                EnsureFileIo(instr);
                DirectoryInfo di = (DirectoryInfo)r;
                long val = Convert.ToInt64(a[0], CultureInfo.InvariantCulture);
                di.Attributes = (FileAttributes)val; return di;
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("create", 0, 0, (r, a, instr) =>
            {
                EnsureFileIo(instr);
                ((DirectoryInfo)r).Create(); return r;
            }));
            object DeleteDirectoryInfo(object r, List<object> a, Instruction instr)
            {
                EnsureFileIo(instr);
                bool recursive = a.Count > 0 && Convert.ToBoolean(a[0], CultureInfo.InvariantCulture);
                ((DirectoryInfo)r).Delete(recursive); return 1;
            }

            intrinsics.Register(T, new IntrinsicDescriptor("delete", 0, 1, DeleteDirectoryInfo));
            intrinsics.Register(T, new IntrinsicDescriptor("deleteDir", 0, 1, DeleteDirectoryInfo));
            intrinsics.Register(T, new IntrinsicDescriptor("refresh", 0, 0, (r, a, instr) =>
            {
                EnsureFileIo(instr);
                ((DirectoryInfo)r).Refresh(); return r;
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("moveTo", 1, 1, (r, a, instr) =>
            {
                EnsureFileIo(instr);
                string dest = a[0]?.ToString() ?? "";
                ((DirectoryInfo)r).MoveTo(dest); return r;
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("createSubdirectory", 1, 1, (r, a, instr) =>
            {
                EnsureFileIo(instr);
                string name = a[0]?.ToString() ?? "";
                return ((DirectoryInfo)r).CreateSubdirectory(name);
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("getFiles", 0, 2, (r, a, instr) =>
            {
                EnsureFileIo(instr);
                DirectoryInfo di = (DirectoryInfo)r;
                string pattern = a.Count >= 1 ? a[0]?.ToString() ?? "*" : "*";
                SearchOption opt = (a.Count >= 2 && Convert.ToBoolean(a[1], CultureInfo.InvariantCulture))
                    ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                return di.GetFiles(pattern, opt).Select(f => (object)f.FullName).ToList();
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("getDirectories", 0, 2, (r, a, instr) =>
            {
                EnsureFileIo(instr);
                DirectoryInfo di = (DirectoryInfo)r;
                string pattern = a.Count >= 1 ? a[0]?.ToString() ?? "*" : "*";
                SearchOption opt = (a.Count >= 2 && Convert.ToBoolean(a[1], CultureInfo.InvariantCulture))
                    ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                return di.GetDirectories(pattern, opt).Select(d => (object)d.FullName).ToList();
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("enumerateFileSystem", 0, 2, (r, a, instr) =>
            {
                EnsureFileIo(instr);
                DirectoryInfo di = (DirectoryInfo)r;
                string pattern = a.Count >= 1 ? a[0]?.ToString() ?? "*" : "*";
                SearchOption opt = (a.Count >= 2 && Convert.ToBoolean(a[1], CultureInfo.InvariantCulture))
                    ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                return di.EnumerateFileSystemInfos(pattern, opt).Select(fi => (object)fi.FullName).ToList();
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("existsOrCreate", 0, 0, (r, a, instr) =>
            {
                EnsureFileIo(instr);
                DirectoryInfo di = (DirectoryInfo)r;
                if (!di.Exists) di.Create();
                return di.Exists;
            }));

            intrinsics.Register(typeof(string), new IntrinsicDescriptor("dirinfo", 0, 0, (recv, a, instr) =>
            {
                EnsureFileIo(instr);
                return new DirectoryInfo(recv?.ToString() ?? "");
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("toString", 0, 0, (r, a, instr) => ((DirectoryInfo)r).FullName));
        }

        private static void RegisterFile(IIntrinsicRegistry intrinsics)
        {
            Type T = typeof(FileHandle);

            if (T == null)
                throw new Exception("error while loading FileHandle class");

            intrinsics.Register(T, new IntrinsicDescriptor("write", 1, 1, (recv, a, i) =>
            {
                dynamic fh = recv; fh.Write(a[0]?.ToString() ?? ""); return recv;
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("writeln", 1, 1, (recv, a, i) =>
            {
                dynamic fh = recv; fh.Writeln(a[0]?.ToString() ?? ""); return recv;
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("flush", 0, 0, (recv, a, i) =>
            {
                dynamic fh = recv; fh.Flush(); return recv;
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("read", 1, 1, (recv, a, i) =>
            {
                dynamic fh = recv; return (string)fh.Read(Convert.ToInt32(a[0]));
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("readline", 0, 0, (recv, a, i) =>
            {
                dynamic fh = recv; return (string)fh.ReadLine();
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("writeByte", 1, 1, (recv, a, instr) =>
            {
                FileHandle fh = (FileHandle)recv;
                fh.WriteByte(ConvertToByteValue(a[0], instr, "value"));
                return recv;
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("writeBytes", 1, 1, (recv, a, instr) =>
            {
                FileHandle fh = (FileHandle)recv;
                fh.WriteBytes(ConvertVmValueToBytes(a[0], instr, "value"));
                return recv;
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("readByte", 0, 0, (recv, a, i) =>
            {
                FileHandle fh = (FileHandle)recv;
                return fh.ReadByte();
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("readBytes", 1, 1, (recv, a, i) =>
            {
                FileHandle fh = (FileHandle)recv;
                return ToVmByteArray(fh.ReadBytes(Convert.ToInt32(a[0], CultureInfo.InvariantCulture)));
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("seek", 2, 2, (recv, a, i) =>
            {
                dynamic fh = recv;
                long off = Convert.ToInt64(a[0]);
                return (long)fh.Seek(off, ToSeekOrigin(a[1]));
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("tell", 0, 0, (recv, a, i) => { dynamic fh = recv; return (long)fh.Tell(); }));
            intrinsics.Register(T, new IntrinsicDescriptor("position", 0, 0, (recv, a, i) => { FileHandle fh = (FileHandle)recv; return fh.Tell(); }));
            intrinsics.Register(T, new IntrinsicDescriptor("setPosition", 1, 1, (recv, a, i) =>
            {
                FileHandle fh = (FileHandle)recv;
                return fh.SetPosition(Convert.ToInt64(a[0], CultureInfo.InvariantCulture));
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("length", 0, 0, (recv, a, i) => { FileHandle fh = (FileHandle)recv; return fh.Length(); }));
            intrinsics.Register(T, new IntrinsicDescriptor("eof", 0, 0, (recv, a, i) => { dynamic fh = recv; return (bool)fh.Eof(); }));
            intrinsics.Register(T, new IntrinsicDescriptor("close", 0, 0, (recv, a, i) => { dynamic fh = recv; fh.Close(); return 1; }));

            intrinsics.Register(T, new IntrinsicDescriptor("writeAsync", 1, 1, (recv, a, i) =>
            {
                return Task.Run<object?>(() => { dynamic fh = recv; fh.Write(a[0]?.ToString() ?? ""); return recv; });
            }, NativeCallOptions.Awaitable));

            intrinsics.Register(T, new IntrinsicDescriptor("writelnAsync", 1, 1, (recv, a, i) =>
            {
                return Task.Run<object?>(() => { dynamic fh = recv; fh.Writeln(a[0]?.ToString() ?? ""); return recv; });
            }, NativeCallOptions.Awaitable));

            intrinsics.Register(T, new IntrinsicDescriptor("flushAsync", 0, 0, (recv, a, i) =>
            {
                return Task.Run<object?>(() => { dynamic fh = recv; fh.Flush(); return recv; });
            }, NativeCallOptions.Awaitable));

            intrinsics.Register(T, new IntrinsicDescriptor("readAsync", 1, 1, (recv, a, i) =>
            {
                return Task.Run<object?>(() => { dynamic fh = recv; return (string)fh.Read(Convert.ToInt32(a[0])); });
            }, NativeCallOptions.Awaitable));

            intrinsics.Register(T, new IntrinsicDescriptor("readlineAsync", 0, 0, (recv, a, i) =>
            {
                return Task.Run<object?>(() => { dynamic fh = recv; return (string)fh.ReadLine(); });
            }, NativeCallOptions.Awaitable));

            intrinsics.Register(T, new IntrinsicDescriptor("writeByteAsync", 1, 1, (recv, a, instr) =>
            {
                return Task.Run<object?>(() =>
                {
                    FileHandle fh = (FileHandle)recv;
                    fh.WriteByte(ConvertToByteValue(a[0], instr, "value"));
                    return recv;
                });
            }, NativeCallOptions.Awaitable));

            intrinsics.Register(T, new IntrinsicDescriptor("writeBytesAsync", 1, 1, (recv, a, instr) =>
            {
                return Task.Run<object?>(() =>
                {
                    FileHandle fh = (FileHandle)recv;
                    fh.WriteBytes(ConvertVmValueToBytes(a[0], instr, "value"));
                    return recv;
                });
            }, NativeCallOptions.Awaitable));

            intrinsics.Register(T, new IntrinsicDescriptor("readByteAsync", 0, 0, (recv, a, instr) =>
            {
                return Task.Run<object?>(() =>
                {
                    FileHandle fh = (FileHandle)recv;
                    return fh.ReadByte();
                });
            }, NativeCallOptions.Awaitable));

            intrinsics.Register(T, new IntrinsicDescriptor("readBytesAsync", 1, 1, (recv, a, instr) =>
            {
                return Task.Run<object?>(() =>
                {
                    FileHandle fh = (FileHandle)recv;
                    return (object)ToVmByteArray(fh.ReadBytes(Convert.ToInt32(a[0], CultureInfo.InvariantCulture)));
                });
            }, NativeCallOptions.Awaitable));
        }

        private static void RegisterBinaryFile(IIntrinsicRegistry intrinsics)
        {
            Type T = typeof(BinaryFileHandle);

            intrinsics.Register(T, new IntrinsicDescriptor("writeByte", 1, 1, (recv, a, instr) =>
            {
                BinaryFileHandle fh = (BinaryFileHandle)recv;
                fh.WriteByte(ConvertToByteValue(a[0], instr, "value"));
                return recv;
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("writeBytes", 1, 1, (recv, a, instr) =>
            {
                BinaryFileHandle fh = (BinaryFileHandle)recv;
                fh.WriteBytes(ConvertVmValueToBytes(a[0], instr, "value"));
                return recv;
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("flush", 0, 0, (recv, a, instr) =>
            {
                BinaryFileHandle fh = (BinaryFileHandle)recv;
                fh.Flush();
                return recv;
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("readByte", 0, 0, (recv, a, instr) =>
            {
                BinaryFileHandle fh = (BinaryFileHandle)recv;
                return fh.ReadByte();
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("readBytes", 1, 1, (recv, a, instr) =>
            {
                BinaryFileHandle fh = (BinaryFileHandle)recv;
                return ToVmByteArray(fh.ReadBytes(Convert.ToInt32(a[0], CultureInfo.InvariantCulture)));
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("seek", 2, 2, (recv, a, instr) =>
            {
                BinaryFileHandle fh = (BinaryFileHandle)recv;
                long off = Convert.ToInt64(a[0], CultureInfo.InvariantCulture);
                return fh.Seek(off, ToSeekOrigin(a[1]));
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("tell", 0, 0, (recv, a, instr) =>
            {
                BinaryFileHandle fh = (BinaryFileHandle)recv;
                return fh.Tell();
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("position", 0, 0, (recv, a, instr) =>
            {
                BinaryFileHandle fh = (BinaryFileHandle)recv;
                return fh.Tell();
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("setPosition", 1, 1, (recv, a, instr) =>
            {
                BinaryFileHandle fh = (BinaryFileHandle)recv;
                return fh.SetPosition(Convert.ToInt64(a[0], CultureInfo.InvariantCulture));
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("length", 0, 0, (recv, a, instr) =>
            {
                BinaryFileHandle fh = (BinaryFileHandle)recv;
                return fh.Length();
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("eof", 0, 0, (recv, a, instr) =>
            {
                BinaryFileHandle fh = (BinaryFileHandle)recv;
                return fh.Eof();
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("close", 0, 0, (recv, a, instr) =>
            {
                BinaryFileHandle fh = (BinaryFileHandle)recv;
                fh.Close();
                return 1;
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("writeByteAsync", 1, 1, (recv, a, instr) =>
            {
                return Task.Run<object?>(() =>
                {
                    BinaryFileHandle fh = (BinaryFileHandle)recv;
                    fh.WriteByte(ConvertToByteValue(a[0], instr, "value"));
                    return recv;
                });
            }, NativeCallOptions.Awaitable));

            intrinsics.Register(T, new IntrinsicDescriptor("writeBytesAsync", 1, 1, (recv, a, instr) =>
            {
                return Task.Run<object?>(() =>
                {
                    BinaryFileHandle fh = (BinaryFileHandle)recv;
                    fh.WriteBytes(ConvertVmValueToBytes(a[0], instr, "value"));
                    return recv;
                });
            }, NativeCallOptions.Awaitable));

            intrinsics.Register(T, new IntrinsicDescriptor("flushAsync", 0, 0, (recv, a, instr) =>
            {
                return Task.Run<object?>(() =>
                {
                    BinaryFileHandle fh = (BinaryFileHandle)recv;
                    fh.Flush();
                    return recv;
                });
            }, NativeCallOptions.Awaitable));

            intrinsics.Register(T, new IntrinsicDescriptor("readByteAsync", 0, 0, (recv, a, instr) =>
            {
                return Task.Run<object?>(() =>
                {
                    BinaryFileHandle fh = (BinaryFileHandle)recv;
                    return fh.ReadByte();
                });
            }, NativeCallOptions.Awaitable));

            intrinsics.Register(T, new IntrinsicDescriptor("readBytesAsync", 1, 1, (recv, a, instr) =>
            {
                return Task.Run<object?>(() =>
                {
                    BinaryFileHandle fh = (BinaryFileHandle)recv;
                    return (object)ToVmByteArray(fh.ReadBytes(Convert.ToInt32(a[0], CultureInfo.InvariantCulture)));
                });
            }, NativeCallOptions.Awaitable));
        }
    }
}
