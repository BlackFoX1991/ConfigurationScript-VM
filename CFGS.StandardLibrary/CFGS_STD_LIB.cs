using CFGS_VM.VMCore.Extensions.Instance;
using CFGS_VM.VMCore.Extensions.Intrinsics.Handles;
using CFGS_VM.VMCore.Extensions.Core;
using CFGS_VM.VMCore.Plugin;
using CFGS_VM.VMCore;
using CFGS_VM.VMCore.Extensions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using static CFGS_VM.VMCore.VM;

namespace CFGS.StandardLibrary
{
    /// <summary>
    /// Defines the <see cref="CFGS_STDLIB" />
    /// </summary>
    public sealed class CFGS_STDLIB : IVmPlugin
    {
        static CFGS_STDLIB()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        /// <summary>
        /// Gets or sets a value indicating whether AllowFileIO
        /// </summary>
        public static bool AllowFileIO { get; set; } = true;
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
        };
        /// <summary>
        /// The FriendlyTypeName
        /// </summary>
        /// <param name="t">The t<see cref="Type"/></param>
        /// <returns>The <see cref="string"/></returns>
        internal static string FriendlyTypeName(Type t)
        {
            if (t == typeof(void)) return "Void";
            if (t == typeof(bool)) return "Boolean";
            if (t == typeof(byte)) return "Byte";
            if (t == typeof(int)) return "Int";
            if (t == typeof(long)) return "Long";
            if (t == typeof(double)) return "Double";
            if (t == typeof(float)) return "Float";
            if (t == typeof(decimal)) return "Decimal";
            if (t == typeof(string)) return "String";
            if (t == typeof(char)) return "Char";
            if (t == typeof(List<object>)) return "Array";
            if (t == typeof(Dictionary<string, object>)) return "Dictionary";
            if (t == typeof(NamespaceBuiltinCallable)) return "BuiltinCallable";
            return t.Name;
        }

        /// <summary>
        /// The Register
        /// </summary>
        /// <param name="builtins">The builtins<see cref="IBuiltinRegistry"/></param>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
        /// <param name="namespaces">The namespaces<see cref="INamespaceRegistry"/></param>
        public void Register(IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics, INamespaceRegistry namespaces)
        {
            RegisterBuiltins(builtins);
            RegisterNamespaces(namespaces);
            RegisterString(intrinsics);
            RegisterArray(intrinsics);
            RegisterDict(intrinsics);
            RegisterException(intrinsics);
            RegisterFile(intrinsics);
            RegisterBinaryFile(intrinsics);
            RegisterDateTime(intrinsics);
            RegisterDirectoryInfo(intrinsics);
            RegisterTaskNamespace(intrinsics);
            RegisterEncodingNamespace(intrinsics);
            RegisterRegexNamespace(intrinsics);
            RegisterJsonNamespace(intrinsics);
            RegisterMathNamespace(intrinsics);
            RegisterConsoleNamespace(intrinsics);
            RegisterFileSystemNamespace(intrinsics);
        }

        private static void RegisterNamespaces(INamespaceRegistry namespaces)
        {
            RegisterNamespaceAliases(namespaces, "console", ConsoleNamespace.Instance, [
                "print", "put", "clear", "getl", "getc", "readLine", "readChar",
                "getlAsync", "getcAsync", "readLineAsync", "readCharAsync"
            ]);

            RegisterNamespaceAliases(namespaces, "encoding", EncodingNamespace.Instance, [
                "textToBytes", "bytesToText", "encode", "encodeText", "decode", "decodeText",
                "convert", "convertEncoding", "has", "hasEncoding", "info", "encodingInfo",
                "names", "encodingNames", "all", "encodings"
            ]);

            RegisterNamespaceAliases(namespaces, "regex", RegexNamespace.Instance, [
                "match", "matches", "replace", "test", "split",
                "regex_match", "regex_matches", "regex_replace", "regex_test", "regex_split"
            ]);

            RegisterNamespaceAliases(namespaces, "json", JsonNamespace.Instance, [
                "parse", "from", "fromjson", "stringify", "to", "tojson"
            ]);

            RegisterNamespaceAliases(namespaces, "math", MathNamespace.Instance, [
                "abs", "rand", "floor", "ceil", "round", "min", "max", "sqrt", "pow",
                "log", "log10", "sin", "cos", "tan", "sign", "trunc", "PI", "pi", "E", "e"
            ]);

            RegisterNamespaceAliases(namespaces, "fs", FileSystemNamespace.Instance, FileSystemNamespaceMembers);
            RegisterNamespaceAliases(namespaces, "file", FileSystemNamespace.Instance, FileSystemNamespaceMembers);
        }

        private static readonly string[] FileSystemNamespaceMembers =
        [
            "open", "fopen", "openBinary", "fbopen", "delete", "fdelete", "move", "fmove",
            "copy", "fcopy", "exists", "fexist", "readAllBytes", "readAllBytesAsync",
            "writeAllBytes", "readTextAsync", "writeTextAsync", "appendTextAsync", "size",
            "fileSize", "lastWriteTime", "fileLastWriteTime", "lastWriteTimeUtc",
            "fileLastWriteTimeUtc", "setWorkspace", "set_workspace", "getWorkspace",
            "get_workspace", "getDirectory", "directoryInfo", "DirectoryInfo"
        ];

        private static void RegisterNamespaceAliases(INamespaceRegistry namespaces, string name, object instance, IEnumerable<string> members)
        {
            string[] materializedMembers = members.ToArray();
            namespaces.Register(new NamespaceDescriptor(name, instance, materializedMembers));
            namespaces.Register(new NamespaceDescriptor($"std.{name}", instance, materializedMembers));
        }

        private static object TextToBytesVm(List<object> args, Instruction instr, string opName)
        {
            string text = args[0]?.ToString() ?? "";
            Encoding encoding = ResolveTextEncoding(args.Count >= 2 ? args[1] : null, instr, opName);
            return ToVmByteArray(encoding.GetBytes(text));
        }

        private static object BytesToTextVm(List<object> args, Instruction instr, string opName)
        {
            byte[] bytes = ConvertVmValueToBytes(args[0], instr, "bytes");
            Encoding encoding = ResolveTextEncoding(args.Count >= 2 ? args[1] : null, instr, opName);
            return encoding.GetString(bytes);
        }

        private static object ConvertEncodingVm(List<object> args, Instruction instr, string opName)
        {
            byte[] sourceBytes = ConvertVmValueToBytes(args[0], instr, "bytes");
            Encoding sourceEncoding = ResolveTextEncoding(args[1], instr, opName);
            Encoding targetEncoding = ResolveTextEncoding(args[2], instr, opName);
            string text = sourceEncoding.GetString(sourceBytes);
            return ToVmByteArray(targetEncoding.GetBytes(text));
        }

        private static object HasEncodingVm(List<object> args, Instruction instr, string opName)
        {
            try
            {
                _ = ResolveTextEncoding(args[0], instr, opName);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static object EncodingInfoVm(List<object> args, Instruction instr, string opName)
        {
            Encoding encoding = ResolveTextEncoding(args[0], instr, opName);
            return EncodingToVmInfo(encoding);
        }

        private static object EncodingNamesVm()
        {
            return Encoding.GetEncodings()
                .OrderBy(static info => info.Name, StringComparer.OrdinalIgnoreCase)
                .Select(static info => (object)info.Name)
                .ToList();
        }

        private static object EncodingsVm()
        {
            return Encoding.GetEncodings()
                .OrderBy(static info => info.Name, StringComparer.OrdinalIgnoreCase)
                .Select(static info => (object)EncodingInfoToVmInfo(info))
                .ToList();
        }

        private static void RegisterEncodingNamespace(IIntrinsicRegistry intrinsics)
        {
            Type T = typeof(EncodingNamespace);

            intrinsics.Register(T, new IntrinsicDescriptor("textToBytes", 1, 2, (recv, a, instr) => TextToBytesVm(a, instr, "encoding.textToBytes")));
            intrinsics.Register(T, new IntrinsicDescriptor("bytesToText", 1, 2, (recv, a, instr) => BytesToTextVm(a, instr, "encoding.bytesToText")));
            intrinsics.Register(T, new IntrinsicDescriptor("encode", 1, 2, (recv, a, instr) => TextToBytesVm(a, instr, "encoding.encode")));
            intrinsics.Register(T, new IntrinsicDescriptor("encodeText", 1, 2, (recv, a, instr) => TextToBytesVm(a, instr, "encoding.encodeText")));
            intrinsics.Register(T, new IntrinsicDescriptor("decode", 1, 2, (recv, a, instr) => BytesToTextVm(a, instr, "encoding.decode")));
            intrinsics.Register(T, new IntrinsicDescriptor("decodeText", 1, 2, (recv, a, instr) => BytesToTextVm(a, instr, "encoding.decodeText")));
            intrinsics.Register(T, new IntrinsicDescriptor("convert", 3, 3, (recv, a, instr) => ConvertEncodingVm(a, instr, "encoding.convert")));
            intrinsics.Register(T, new IntrinsicDescriptor("convertEncoding", 3, 3, (recv, a, instr) => ConvertEncodingVm(a, instr, "encoding.convertEncoding")));
            intrinsics.Register(T, new IntrinsicDescriptor("has", 1, 1, (recv, a, instr) => HasEncodingVm(a, instr, "encoding.has")));
            intrinsics.Register(T, new IntrinsicDescriptor("hasEncoding", 1, 1, (recv, a, instr) => HasEncodingVm(a, instr, "encoding.hasEncoding")));
            intrinsics.Register(T, new IntrinsicDescriptor("info", 1, 1, (recv, a, instr) => EncodingInfoVm(a, instr, "encoding.info")));
            intrinsics.Register(T, new IntrinsicDescriptor("encodingInfo", 1, 1, (recv, a, instr) => EncodingInfoVm(a, instr, "encoding.encodingInfo")));
            intrinsics.Register(T, new IntrinsicDescriptor("names", 0, 0, (recv, a, instr) => EncodingNamesVm()));
            intrinsics.Register(T, new IntrinsicDescriptor("encodingNames", 0, 0, (recv, a, instr) => EncodingNamesVm()));
            intrinsics.Register(T, new IntrinsicDescriptor("all", 0, 0, (recv, a, instr) => EncodingsVm()));
            intrinsics.Register(T, new IntrinsicDescriptor("encodings", 0, 0, (recv, a, instr) => EncodingsVm()));
        }

        private static Dictionary<string, object> RegexMatchToVm(Match match)
        {
            return new Dictionary<string, object>
            {
                ["matched"] = match.Value,
                ["index"] = match.Index,
                ["groups"] = match.Groups.Cast<Group>().Select(g => (object)g.Value).ToList()
            };
        }

        private static object RegexMatchVm(List<object> args)
        {
            string input = args[0]?.ToString() ?? "";
            string pattern = args[1]?.ToString() ?? "";
            Match match = Regex.Match(input, pattern);
            return match.Success ? RegexMatchToVm(match) : null!;
        }

        private static object RegexMatchesVm(List<object> args)
        {
            string input = args[0]?.ToString() ?? "";
            string pattern = args[1]?.ToString() ?? "";
            return Regex.Matches(input, pattern)
                .Select(match => (object)RegexMatchToVm(match))
                .ToList();
        }

        private static object RegexReplaceVm(List<object> args)
        {
            string input = args[0]?.ToString() ?? "";
            string pattern = args[1]?.ToString() ?? "";
            string replacement = args[2]?.ToString() ?? "";
            return Regex.Replace(input, pattern, replacement);
        }

        private static object RegexTestVm(List<object> args)
        {
            string input = args[0]?.ToString() ?? "";
            string pattern = args[1]?.ToString() ?? "";
            return Regex.IsMatch(input, pattern);
        }

        private static object RegexSplitVm(List<object> args)
        {
            string input = args[0]?.ToString() ?? "";
            string pattern = args[1]?.ToString() ?? "";
            return Regex.Split(input, pattern).Select(s => (object)s).ToList();
        }

        private static void RegisterRegexNamespace(IIntrinsicRegistry intrinsics)
        {
            Type T = typeof(RegexNamespace);

            intrinsics.Register(T, new IntrinsicDescriptor("match", 2, 2, (recv, a, instr) => RegexMatchVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("matches", 2, 2, (recv, a, instr) => RegexMatchesVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("replace", 3, 3, (recv, a, instr) => RegexReplaceVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("test", 2, 2, (recv, a, instr) => RegexTestVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("split", 2, 2, (recv, a, instr) => RegexSplitVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("regex_match", 2, 2, (recv, a, instr) => RegexMatchVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("regex_matches", 2, 2, (recv, a, instr) => RegexMatchesVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("regex_replace", 3, 3, (recv, a, instr) => RegexReplaceVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("regex_test", 2, 2, (recv, a, instr) => RegexTestVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("regex_split", 2, 2, (recv, a, instr) => RegexSplitVm(a)));
        }

        private static object JsonParseVm(List<object> args)
        {
            string? json = args[0]?.ToString();
            if (string.IsNullOrWhiteSpace(json))
                return null!;

            using JsonDocument doc = JsonDocument.Parse(json);
            return JsonToVmValue(doc.RootElement)!;
        }

        private static object JsonStringifyVm(List<object> args)
        {
            return JsonSerializer.Serialize(NormalizeForJson(args[0]), Options);
        }

        private static void RegisterJsonNamespace(IIntrinsicRegistry intrinsics)
        {
            Type T = typeof(JsonNamespace);

            intrinsics.Register(T, new IntrinsicDescriptor("parse", 1, 1, (recv, a, instr) => JsonParseVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("from", 1, 1, (recv, a, instr) => JsonParseVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("fromjson", 1, 1, (recv, a, instr) => JsonParseVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("stringify", 1, 1, (recv, a, instr) => JsonStringifyVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("to", 1, 1, (recv, a, instr) => JsonStringifyVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("tojson", 1, 1, (recv, a, instr) => JsonStringifyVm(a)));
        }

        private static object MathAbsVm(List<object> args) => Math.Abs((dynamic)args[0]);

        private static object MathRandVm(List<object> args)
        {
            return new Random(Convert.ToInt32(args[0], CultureInfo.InvariantCulture))
                .Next(Convert.ToInt32(args[1], CultureInfo.InvariantCulture), Convert.ToInt32(args[2], CultureInfo.InvariantCulture));
        }

        private static object MathFloorVm(List<object> args) => Math.Floor(Convert.ToDouble(args[0], CultureInfo.InvariantCulture));
        private static object MathCeilVm(List<object> args) => Math.Ceiling(Convert.ToDouble(args[0], CultureInfo.InvariantCulture));
        private static object MathRoundVm(List<object> args) => args.Count == 1
            ? Math.Round(Convert.ToDouble(args[0], CultureInfo.InvariantCulture))
            : Math.Round(Convert.ToDouble(args[0], CultureInfo.InvariantCulture), Convert.ToInt32(args[1], CultureInfo.InvariantCulture));

        private static object MathMinVm(List<object> args)
        {
            dynamic a = args[0], b = args[1];
            return a < b ? a : b;
        }

        private static object MathMaxVm(List<object> args)
        {
            dynamic a = args[0], b = args[1];
            return a > b ? a : b;
        }

        private static object MathSqrtVm(List<object> args) => Math.Sqrt(Convert.ToDouble(args[0], CultureInfo.InvariantCulture));
        private static object MathPowVm(List<object> args) => Math.Pow(Convert.ToDouble(args[0], CultureInfo.InvariantCulture), Convert.ToDouble(args[1], CultureInfo.InvariantCulture));
        private static object MathLogVm(List<object> args) => args.Count == 1
            ? Math.Log(Convert.ToDouble(args[0], CultureInfo.InvariantCulture))
            : Math.Log(Convert.ToDouble(args[0], CultureInfo.InvariantCulture), Convert.ToDouble(args[1], CultureInfo.InvariantCulture));
        private static object MathLog10Vm(List<object> args) => Math.Log10(Convert.ToDouble(args[0], CultureInfo.InvariantCulture));
        private static object MathSinVm(List<object> args) => Math.Sin(Convert.ToDouble(args[0], CultureInfo.InvariantCulture));
        private static object MathCosVm(List<object> args) => Math.Cos(Convert.ToDouble(args[0], CultureInfo.InvariantCulture));
        private static object MathTanVm(List<object> args) => Math.Tan(Convert.ToDouble(args[0], CultureInfo.InvariantCulture));
        private static object MathSignVm(List<object> args) => Math.Sign(Convert.ToDouble(args[0], CultureInfo.InvariantCulture));
        private static object MathTruncVm(List<object> args) => Math.Truncate(Convert.ToDouble(args[0], CultureInfo.InvariantCulture));

        private static void RegisterMathNamespace(IIntrinsicRegistry intrinsics)
        {
            Type T = typeof(MathNamespace);

            intrinsics.Register(T, new IntrinsicDescriptor("abs", 1, 1, (recv, a, instr) => MathAbsVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("rand", 3, 3, (recv, a, instr) => MathRandVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("floor", 1, 1, (recv, a, instr) => MathFloorVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("ceil", 1, 1, (recv, a, instr) => MathCeilVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("round", 1, 2, (recv, a, instr) => MathRoundVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("min", 2, 2, (recv, a, instr) => MathMinVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("max", 2, 2, (recv, a, instr) => MathMaxVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("sqrt", 1, 1, (recv, a, instr) => MathSqrtVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("pow", 2, 2, (recv, a, instr) => MathPowVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("log", 1, 2, (recv, a, instr) => MathLogVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("log10", 1, 1, (recv, a, instr) => MathLog10Vm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("sin", 1, 1, (recv, a, instr) => MathSinVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("cos", 1, 1, (recv, a, instr) => MathCosVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("tan", 1, 1, (recv, a, instr) => MathTanVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("sign", 1, 1, (recv, a, instr) => MathSignVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("trunc", 1, 1, (recv, a, instr) => MathTruncVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("PI", 0, 0, (recv, a, instr) => Math.PI));
            intrinsics.Register(T, new IntrinsicDescriptor("pi", 0, 0, (recv, a, instr) => Math.PI));
            intrinsics.Register(T, new IntrinsicDescriptor("E", 0, 0, (recv, a, instr) => Math.E));
            intrinsics.Register(T, new IntrinsicDescriptor("e", 0, 0, (recv, a, instr) => Math.E));
        }

        private static object ConsolePrintVm(List<object> args)
        {
            PrintValue(args[0], Console.Out, escapeNewlines: false);
            Console.Out.WriteLine();
            Console.Out.Flush();
            return 1;
        }

        private static object ConsolePutVm(List<object> args)
        {
            PrintValue(args[0], Console.Out, escapeNewlines: false);
            Console.Out.Flush();
            return 1;
        }

        private static object ConsoleClearVm()
        {
            Console.Clear();
            return 1;
        }

        private static object ConsoleGetLineVm() => Console.ReadLine() ?? "";
        private static object ConsoleGetCharVm() => Console.Read();
        private static object ConsoleGetLineAsyncVm() => Task.Run<object?>(async () => await Console.In.ReadLineAsync().ConfigureAwait(false));
        private static object ConsoleGetCharAsyncVm() => Task.Run<object?>(async () =>
        {
            char[] buf = new char[1];
            int n = await Console.In.ReadAsync(buf, 0, 1).ConfigureAwait(false);
            return n <= 0 ? -1 : (int)buf[0];
        });

        private static void RegisterConsoleNamespace(IIntrinsicRegistry intrinsics)
        {
            Type T = typeof(ConsoleNamespace);

            intrinsics.Register(T, new IntrinsicDescriptor("print", 1, 1, (recv, a, instr) => ConsolePrintVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("put", 1, 1, (recv, a, instr) => ConsolePutVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("clear", 0, 0, (recv, a, instr) => ConsoleClearVm()));
            intrinsics.Register(T, new IntrinsicDescriptor("getl", 0, 0, (recv, a, instr) => ConsoleGetLineVm()));
            intrinsics.Register(T, new IntrinsicDescriptor("getc", 0, 0, (recv, a, instr) => ConsoleGetCharVm()));
            intrinsics.Register(T, new IntrinsicDescriptor("readLine", 0, 0, (recv, a, instr) => ConsoleGetLineVm()));
            intrinsics.Register(T, new IntrinsicDescriptor("readChar", 0, 0, (recv, a, instr) => ConsoleGetCharVm()));
            intrinsics.Register(T, new IntrinsicDescriptor("getlAsync", 0, 0, (recv, a, instr) => ConsoleGetLineAsyncVm()));
            intrinsics.Register(T, new IntrinsicDescriptor("getcAsync", 0, 0, (recv, a, instr) => ConsoleGetCharAsyncVm()));
            intrinsics.Register(T, new IntrinsicDescriptor("readLineAsync", 0, 0, (recv, a, instr) => ConsoleGetLineAsyncVm()));
            intrinsics.Register(T, new IntrinsicDescriptor("readCharAsync", 0, 0, (recv, a, instr) => ConsoleGetCharAsyncVm()));
        }

        private static object FileDeleteVm(List<object> args)
        {
            var path = args[0]?.ToString() ?? string.Empty;
            if (path.Length == 0)
                throw new ArgumentException("fdelete requires a file path.");

            if (!File.Exists(path))
                return 0;

            File.Delete(path);
            return 1;
        }

        private static object FileMoveVm(List<object> args)
        {
            var source = args[0]?.ToString() ?? string.Empty;
            var destination = args[1]?.ToString() ?? string.Empty;
            var overwrite = args.Count >= 3 && Convert.ToBoolean(args[2], CultureInfo.InvariantCulture);

            if (source.Length == 0 || destination.Length == 0)
                throw new ArgumentException("fmove requires source and destination paths.");

            var parent = Path.GetDirectoryName(Path.GetFullPath(destination));
            if (!string.IsNullOrWhiteSpace(parent))
                Directory.CreateDirectory(parent);

            File.Move(source, destination, overwrite);
            return destination;
        }

        private static object FileCopyVm(List<object> args)
        {
            var source = args[0]?.ToString() ?? string.Empty;
            var destination = args[1]?.ToString() ?? string.Empty;
            var overwrite = args.Count >= 3 && Convert.ToBoolean(args[2], CultureInfo.InvariantCulture);

            if (source.Length == 0 || destination.Length == 0)
                throw new ArgumentException("fcopy requires source and destination paths.");

            var parent = Path.GetDirectoryName(Path.GetFullPath(destination));
            if (!string.IsNullOrWhiteSpace(parent))
                Directory.CreateDirectory(parent);

            File.Copy(source, destination, overwrite);
            return destination;
        }

        private static object FileOpenVm(List<object> args, Instruction instr)
        {
            (string path, int mode, FileStream fs, bool canRead, bool canWrite) = OpenFileStream(args, instr, "fopen");
            return new FileHandle(path, mode, fs, canRead, canWrite);
        }

        private static object BinaryFileOpenVm(List<object> args, Instruction instr)
        {
            (string path, int mode, FileStream fs, bool canRead, bool canWrite) = OpenFileStream(args, instr, "fbopen");
            return new BinaryFileHandle(path, mode, fs, canRead, canWrite);
        }

        private static object ReadAllBytesVm(List<object> args, Instruction instr)
        {
            EnsureFileIo(instr);
            string path = args[0]?.ToString() ?? "";
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
            string path = args[0]?.ToString() ?? "";
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
            string path = args[0]?.ToString() ?? "";
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
            if (!AllowFileIO)
                throw new VMException("Runtime error: file I/O is disabled (AllowFileIO=false)", instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
            string path = args[0]?.ToString() ?? "";
            return Task.Run<object?>(async () => await File.ReadAllTextAsync(path).ConfigureAwait(false));
        }

        private static object WriteTextAsyncVm(List<object> args, Instruction instr)
        {
            if (!AllowFileIO)
                throw new VMException("Runtime error: file I/O is disabled (AllowFileIO=false)", instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
            string path = args[0]?.ToString() ?? "";
            string text = args[1]?.ToString() ?? "";
            return Task.Run<object?>(async () => { await File.WriteAllTextAsync(path, text).ConfigureAwait(false); return 1; });
        }

        private static object AppendTextAsyncVm(List<object> args, Instruction instr)
        {
            if (!AllowFileIO)
                throw new VMException("Runtime error: file I/O is disabled (AllowFileIO=false)", instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
            string path = args[0]?.ToString() ?? "";
            string text = args[1]?.ToString() ?? "";
            return Task.Run<object?>(async () => { await File.AppendAllTextAsync(path, text).ConfigureAwait(false); return 1; });
        }

        private static object FileSizeVm(List<object> args, Instruction instr)
        {
            EnsureFileIo(instr);
            string path = args[0]?.ToString() ?? "";
            return File.Exists(path) ? new FileInfo(path).Length : -1L;
        }

        private static object FileLastWriteTimeVm(List<object> args, Instruction instr)
        {
            EnsureFileIo(instr);
            string path = args[0]?.ToString() ?? "";
            return File.GetLastWriteTime(path);
        }

        private static object FileLastWriteTimeUtcVm(List<object> args, Instruction instr)
        {
            EnsureFileIo(instr);
            string path = args[0]?.ToString() ?? "";
            return File.GetLastWriteTimeUtc(path);
        }

        private static object FileExistsVm(List<object> args)
        {
            if (args[0] is not string)
                return false;
            return File.Exists(args[0].ToString());
        }

        private static object SetWorkspaceVm(List<object> args, Instruction instr)
        {
            if (!AllowFileIO)
                throw new VMException("Runtime error: file I/O is disabled (AllowFileIO=false)", instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
            if (args[0] is not string path)
                throw new VMException("Runtime error: set_workspace requires a string path",
                    instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
            try
            {
                Directory.SetCurrentDirectory(path);
                return 1;
            }
            catch (Exception ex)
            {
                throw new VMException($"Runtime error: set_workspace('{path}') failed: {ex.GetType().Name}: {ex.Message}",
                    instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
            }
        }

        private static object GetWorkspaceVm() => Directory.GetCurrentDirectory();

        private static object GetDirectoryVm(List<object> args, Instruction instr)
        {
            if (!AllowFileIO)
                throw new VMException("Runtime error: file I/O is disabled (AllowFileIO=false)", instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
            return Path.GetDirectoryName(args[0]?.ToString() ?? "") ?? "";
        }

        private static object DirectoryInfoVm(List<object> args) => new DirectoryInfo(args[0]?.ToString() ?? "");

        private static void RegisterFileSystemNamespace(IIntrinsicRegistry intrinsics)
        {
            Type T = typeof(FileSystemNamespace);

            intrinsics.Register(T, new IntrinsicDescriptor("open", 2, 2, (recv, a, instr) => FileOpenVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("fopen", 2, 2, (recv, a, instr) => FileOpenVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("openBinary", 2, 2, (recv, a, instr) => BinaryFileOpenVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("fbopen", 2, 2, (recv, a, instr) => BinaryFileOpenVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("delete", 1, 1, (recv, a, instr) => FileDeleteVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("fdelete", 1, 1, (recv, a, instr) => FileDeleteVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("move", 2, 3, (recv, a, instr) => FileMoveVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("fmove", 2, 3, (recv, a, instr) => FileMoveVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("copy", 2, 3, (recv, a, instr) => FileCopyVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("fcopy", 2, 3, (recv, a, instr) => FileCopyVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("exists", 1, 1, (recv, a, instr) => FileExistsVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("fexist", 1, 1, (recv, a, instr) => FileExistsVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("readAllBytes", 1, 1, (recv, a, instr) => ReadAllBytesVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("readAllBytesAsync", 1, 1, (recv, a, instr) => ReadAllBytesAsyncVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("writeAllBytes", 2, 2, (recv, a, instr) => WriteAllBytesVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("readTextAsync", 1, 1, (recv, a, instr) => ReadTextAsyncVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("writeTextAsync", 2, 2, (recv, a, instr) => WriteTextAsyncVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("appendTextAsync", 2, 2, (recv, a, instr) => AppendTextAsyncVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("size", 1, 1, (recv, a, instr) => FileSizeVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("fileSize", 1, 1, (recv, a, instr) => FileSizeVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("lastWriteTime", 1, 1, (recv, a, instr) => FileLastWriteTimeVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("fileLastWriteTime", 1, 1, (recv, a, instr) => FileLastWriteTimeVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("lastWriteTimeUtc", 1, 1, (recv, a, instr) => FileLastWriteTimeUtcVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("fileLastWriteTimeUtc", 1, 1, (recv, a, instr) => FileLastWriteTimeUtcVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("setWorkspace", 1, 1, (recv, a, instr) => SetWorkspaceVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("set_workspace", 1, 1, (recv, a, instr) => SetWorkspaceVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("getWorkspace", 0, 0, (recv, a, instr) => GetWorkspaceVm()));
            intrinsics.Register(T, new IntrinsicDescriptor("get_workspace", 0, 0, (recv, a, instr) => GetWorkspaceVm()));
            intrinsics.Register(T, new IntrinsicDescriptor("getDirectory", 1, 1, (recv, a, instr) => GetDirectoryVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("directoryInfo", 1, 1, (recv, a, instr) => DirectoryInfoVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("DirectoryInfo", 1, 1, (recv, a, instr) => DirectoryInfoVm(a)));
        }

        /// <summary>
        /// The RegisterTaskNamespace
        /// </summary>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
        private static void RegisterTaskNamespace(IIntrinsicRegistry intrinsics)
        {
            Type T = typeof(TaskNamespace);

            intrinsics.Register(T, new IntrinsicDescriptor("get", 1, 1, (recv, a, i) =>
            {
                object? value = a[0];
                return System.Threading.Tasks.Task.FromResult<object?>(value);
            }, NativeCallOptions.Awaitable));

            intrinsics.Register(T, new IntrinsicDescriptor("completed", 0, 0, (recv, a, i) =>
            {
                return System.Threading.Tasks.Task.FromResult<object?>(null);
            }, NativeCallOptions.Awaitable));

            intrinsics.Register(T, new IntrinsicDescriptor("delay", 1, 2, (recv, a, instr) =>
            {
                int ms = Convert.ToInt32(a[0], System.Globalization.CultureInfo.InvariantCulture);
                object? value = (a.Count >= 2) ? a[1] : null;
                return System.Threading.Tasks.Task.Run<object?>(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(ms).ConfigureAwait(false);
                    return value;
                });
            }, NativeCallOptions.Awaitable));
        }

        private static void EnsureFileIo(Instruction instr)
        {
            if (!AllowFileIO)
                throw new VMException("Runtime error: file I/O is disabled (AllowFileIO=false)", instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
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

        private static byte ConvertAnyToByteValue(object? value, Instruction instr, string name)
        {
            if (value is null)
            {
                throw new VMException(
                    $"Runtime error: {name} cannot be null",
                    instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
            }

            if (value is List<object> bits)
                return ConvertBitArrayToByte(bits, instr, name);

            return ConvertToByteValue(value, instr, name);
        }

        private static byte ConvertBitArrayToByte(List<object> bits, Instruction instr, string name)
        {
            if (bits.Count is < 1 or > 8)
            {
                throw new VMException(
                    $"Runtime error: {name} bit array must contain 1..8 values",
                    instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
            }

            int result = 0;
            for (int i = 0; i < bits.Count; i++)
            {
                int bit;
                try
                {
                    bit = Convert.ToInt32(bits[i], CultureInfo.InvariantCulture);
                }
                catch (Exception ex)
                {
                    throw new VMException(
                        $"Runtime error: {name}[{i}] is not a valid bit ({ex.GetType().Name}: {ex.Message})",
                        instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
                }

                if (bit is not 0 and not 1)
                {
                    throw new VMException(
                        $"Runtime error: {name}[{i}] must be 0 or 1",
                        instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
                }

                result = (result << 1) | bit;
            }

            return (byte)result;
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

        private static byte[] ConvertAnyToBytes(object? value, Instruction instr, string name, string? encodingName = null)
        {
            if (value is null)
                return Array.Empty<byte>();

            if (value is List<object> list)
                return ConvertVmArrayToBytes(list, instr, name);

            if (value is string text)
                return ResolveTextEncoding(encodingName, instr, name).GetBytes(text);

            if (value is char ch)
                return ResolveTextEncoding(encodingName, instr, name).GetBytes(ch.ToString());

            return [ConvertAnyToByteValue(value, instr, name)];
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

        private static Encoding ResolveTextEncoding(object? encodingName, Instruction instr, string opName)
        {
            try
            {
                if (encodingName is null)
                    return Encoding.UTF8;

                if (encodingName is not string and not char)
                {
                    int codePage = Convert.ToInt32(encodingName, CultureInfo.InvariantCulture);
                    return Encoding.GetEncoding(codePage);
                }

                string actualName = encodingName.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(actualName))
                    return Encoding.UTF8;

                return Encoding.GetEncoding(actualName.Trim());
            }
            catch (Exception ex)
            {
                string actualName = encodingName?.ToString() ?? Encoding.UTF8.WebName;
                throw new VMException(
                    $"Runtime error: {opName} encoding '{actualName}' is not supported ({ex.GetType().Name}: {ex.Message})",
                    instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
            }
        }

        private static Dictionary<string, object> EncodingToVmInfo(Encoding encoding)
            => new(StringComparer.Ordinal)
            {
                ["name"] = encoding.WebName,
                ["webName"] = encoding.WebName,
                ["encodingName"] = encoding.EncodingName,
                ["bodyName"] = encoding.BodyName,
                ["headerName"] = encoding.HeaderName,
                ["codePage"] = encoding.CodePage,
                ["windowsCodePage"] = encoding.WindowsCodePage,
                ["isSingleByte"] = encoding.IsSingleByte
            };

        private static Dictionary<string, object> EncodingInfoToVmInfo(EncodingInfo info)
            => new(StringComparer.Ordinal)
            {
                ["name"] = info.Name,
                ["displayName"] = info.DisplayName,
                ["codePage"] = info.CodePage
            };

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

        /// <summary>
        /// The RegisterBuiltins
        /// </summary>
        /// <param name="builtins">The builtins<see cref="IBuiltinRegistry"/></param>
        private static void RegisterBuiltins(IBuiltinRegistry builtins)
        {

            builtins.Register(new BuiltinDescriptor("typeof", 1, 1, (args, instr) =>
            {
                object val = args[0];
                if (val == null) return "Null";

                Type vt = val.GetType();

                if (val is System.Threading.Tasks.Task)
                {
                    if (vt.IsGenericType)
                        return $"Task<{FriendlyTypeName(vt.GetGenericArguments()[0])}>";
                    return "Task";
                }
                if (vt == typeof(System.Threading.Tasks.ValueTask))
                    return "ValueTask";
                if (vt.IsGenericType && vt.GetGenericTypeDefinition() == typeof(System.Threading.Tasks.ValueTask<>))
                    return $"ValueTask<{FriendlyTypeName(vt.GetGenericArguments()[0])}>";

                if (val is bool) return "Boolean";
                if (val is int) return "Int";
                if (val is long) return "Long";
                if (val is BigInteger) return "BigInt";
                if (val is double) return "Double";
                if (val is float) return "Float";
                if (val is decimal) return "Decimal";
                if (val is string) return "String";
                if (val is char) return "Char";
                if (val is List<object>) return "Array";
                if (val is Dictionary<string, object>) return "Dictionary";
                if (val is NamespaceBuiltinCallable) return "BuiltinCallable";
                if (val is ClassInstance ci) return ci.ClassName;
                if (val is StaticInstance si) return si.ClassName;
                if (val is EnumInstance) return "Enum";
                if (val is ExceptionObject) return "Exception";

                return vt.Name;
            }));

            builtins.Register(new BuiltinDescriptor("len", 1, 1, (args, instr) =>
            {
                object a0 = args[0];
                if (a0 is string s) return s.Length;
                if (a0 is List<object> list) return LockMutableRuntime(list, () => list.Count);
                if (a0 is Dictionary<string, object> dct) return LockMutableRuntime(dct, () => dct.Count);
                return -1;
            }));

            builtins.Register(new BuiltinDescriptor("destroy", 1, 2, (args, instr) =>
            {
                bool recursive = false;
                if (args.Count >= 2)
                {
                    try
                    {
                        recursive = Convert.ToBoolean(args[1], CultureInfo.InvariantCulture);
                    }
                    catch (Exception ex)
                    {
                        throw new VMException(
                            $"Runtime error: destroy() optional recursive flag must be boolean-convertible ({ex.GetType().Name}: {ex.Message})",
                            instr.Line,
                            instr.Col,
                            instr.OriginFile,
                            VM.IsDebugging,
                            VM.DebugStream!);
                    }
                }

                VM vm = VM.CurrentVm
                    ?? throw new VMException("Runtime error: destroy() requires an active VM.", instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);

                return vm.DestroyValue(args[0], instr, recursive);
            }));

            builtins.Register(new BuiltinDescriptor("print", 1, 1, (args, instr) =>
            {
                return ConsolePrintVm(args);
            }));

            builtins.Register(new BuiltinDescriptor("put", 1, 1, (args, instr) =>
            {
                return ConsolePutVm(args);
            }));

            builtins.Register(new BuiltinDescriptor("fdelete", 1, 1, (args, instr) =>
            {
                return FileDeleteVm(args);
            }));

            builtins.Register(new BuiltinDescriptor("fmove", 2, 3, (args, instr) =>
            {
                return FileMoveVm(args);
            }));

            builtins.Register(new BuiltinDescriptor("fcopy", 2, 3, (args, instr) =>
            {
                return FileCopyVm(args);
            }));

            builtins.Register(new BuiltinDescriptor("clear", 0, 0, (args, instr) =>
            {
                return ConsoleClearVm();
            }));

            builtins.Register(new BuiltinDescriptor("getl", 0, 0, (args, instr) =>
            {
                return ConsoleGetLineVm();
            }));

            builtins.Register(new BuiltinDescriptor("getc", 0, 0, (args, instr) =>
            {
                return ConsoleGetCharVm();
            }));

            builtins.Register(new BuiltinDescriptor("str", 1, 1, (args, instr) =>
            {
                if (args[0] is null) return null!;
                return args[0].ToString() ?? "";
            }));
            builtins.Register(new BuiltinDescriptor("format", 1, int.MaxValue, (args, instr) =>
            {
                if (args.Count == 0 || args[0] is not string format)
                    return null!;
                if (args.Count == 1)
                    return format;
                var fmtArgs = new object[args.Count - 1];
                for (int i = 1; i < args.Count; i++)
                    fmtArgs[i - 1] = args[i];

                return string.Format(format, fmtArgs);
            }));



            builtins.Register(new BuiltinDescriptor("string", 1, 1, (args, instr) =>
            {
                if (args[0] is null) return null!;
                return args[0].ToString() ?? "";
            }));

            builtins.Register(new BuiltinDescriptor("int", 1, 1, (args, instr) =>
            {
                if (args[0] is null) return null!;
                return Convert.ToInt32(args[0], CultureInfo.InvariantCulture);
            }));

            builtins.Register(new BuiltinDescriptor("byte", 1, 1, (args, instr) =>
            {
                if (args[0] is null) return null!;
                return ConvertToByteValue(args[0], instr, "value");
            }));

            builtins.Register(new BuiltinDescriptor("tobyte", 1, 1, (args, instr) =>
            {
                if (args[0] is null) return null!;
                return ConvertAnyToByteValue(args[0], instr, "value");
            }));

            builtins.Register(new BuiltinDescriptor("tobytes", 1, 2, (args, instr) =>
            {
                string? encodingName = args.Count >= 2 ? args[1]?.ToString() : null;
                return ToVmByteArray(ConvertAnyToBytes(args[0], instr, "value", encodingName));
            }));

            builtins.Register(new BuiltinDescriptor("long", 1, 1, (args, instr) =>
            {
                if (args[0] is null) return null!;
                return Convert.ToInt64(args[0], CultureInfo.InvariantCulture);
            }));

            builtins.Register(new BuiltinDescriptor("double", 1, 1, (args, instr) =>
            {
                if (args[0] is null) return null!;
                return Convert.ToDouble(args[0], CultureInfo.InvariantCulture);
            }));

            builtins.Register(new BuiltinDescriptor("float", 1, 1, (args, instr) =>
            {
                if (args[0] is null) return null!;
                return Convert.ToSingle(args[0], CultureInfo.InvariantCulture);
            }));

            builtins.Register(new BuiltinDescriptor("decimal", 1, 1, (args, instr) =>
            {
                if (args[0] is null) return null!;
                return Convert.ToDecimal(args[0], CultureInfo.InvariantCulture);
            }));

            builtins.Register(new BuiltinDescriptor("char", 1, 1, (args, instr) =>
            {
                if (args[0] is null) return null!;
                return Convert.ToChar(args[0], CultureInfo.InvariantCulture);
            }));

            builtins.Register(new BuiltinDescriptor("bool", 1, 1, (args, instr) =>
            {
                if (args[0] is null) return null!;
                return Convert.ToBoolean(args[0], CultureInfo.InvariantCulture);
            }));

            builtins.Register(new BuiltinDescriptor("bigint", 1, 1, (args, instr) =>
            {
                if (args[0] is null) return null!;
                object v = args[0];

                if (v is sbyte or byte or short or ushort or int or uint or long or ulong)
                    return new System.Numerics.BigInteger(Convert.ToInt64(v, CultureInfo.InvariantCulture));
                if (v is decimal)
                    return new System.Numerics.BigInteger((decimal)v);
                if (v is float or double)
                    return new System.Numerics.BigInteger(Convert.ToDecimal(v, CultureInfo.InvariantCulture));
                if (v is string s)
                {
                    if (System.Numerics.BigInteger.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger bi))
                        return bi;
                    return new System.Numerics.BigInteger(0);
                }
                return new System.Numerics.BigInteger(0);
            }));

            builtins.Register(new BuiltinDescriptor("array", 1, 1, (args, instr) =>
            {
                if (args[0] is null) return null!;
                if (args[0] is List<object> list)
                    return SnapshotMutableList(list);
                return new List<object> { args[0]! };
            }));

            builtins.Register(new BuiltinDescriptor("dictionary", 1, 1, (args, instr) =>
            {
                if (args[0] is null) return null!;
                if (args[0] is Dictionary<string, object> d)
                    return SnapshotMutableDictionaryEntries(d).ToDictionary(kv => kv.Key, kv => kv.Value);
                return new Dictionary<string, object>();
            }));

            builtins.Register(new BuiltinDescriptor("toi", 1, 1, (args, instr) => ToNumber(args[0])));
            builtins.Register(new BuiltinDescriptor("toi16", 1, 1, (args, instr) => Convert.ToInt16(args[0])));
            builtins.Register(new BuiltinDescriptor("toi32", 1, 1, (args, instr) => Convert.ToInt32(args[0])));
            builtins.Register(new BuiltinDescriptor("toi64", 1, 1, (args, instr) => Convert.ToInt64(args[0])));
            builtins.Register(new BuiltinDescriptor("chr", 1, 1, (args, instr) => Convert.ToChar(args[0])));

            builtins.Register(new BuiltinDescriptor("set_workspace", 1, 1, (args, instr) =>
            {
                return SetWorkspaceVm(args, instr);
            }));
            builtins.Register(new BuiltinDescriptor("get_workspace", 0, 0, (args, instr) =>
            {
                return GetWorkspaceVm();
            }));

            builtins.Register(new BuiltinDescriptor("ScriptPath", 0, 0, (args, instr) =>
            {
                return VM.CurrentVm?.EntryScriptPath ?? string.Empty;
            }));

            builtins.Register(new BuiltinDescriptor("cmdArgs", 0, 0, (args, instr) =>
            {
                if (!Environment.GetCommandLineArgs().Contains("-p") && !Environment.GetCommandLineArgs().Contains("-params"))
                    throw new Exception("Set the argument marker first ( -p(arams ).");

                return Environment.GetCommandLineArgs().
                                                    Skip(Environment.GetCommandLineArgs().ToList().IndexOf("-p") != -1 ?
                                                         Environment.GetCommandLineArgs().ToList().IndexOf("-p") + 1 :
                                                         Environment.GetCommandLineArgs().ToList().IndexOf("-params") + 1).
                                                    ToList<object>();
            }));

            builtins.Register(new BuiltinDescriptor("getDirectory", 1, 1, (args, instr) =>
            {
                return GetDirectoryVm(args, instr);
            }));

            builtins.Register(new BuiltinDescriptor("DirectoryInfo", 1, 1, (args, instr) =>
            {
                return DirectoryInfoVm(args);
            }));

            builtins.Register(new BuiltinDescriptor("DateTime", 0, 0, (args, instr) =>
            {
                return new DateTime();
            }));
            builtins.Register(new BuiltinDescriptor("Now", 0, 1, (args, instr) =>
            {
                return DateTime.Now.ToString(args.Count > 0 ? args[0].ToString() : null);
            }));
            builtins.Register(new BuiltinDescriptor("UtcNow", 0, 1, (args, instr) =>
            {
                return DateTime.UtcNow.ToString(args.Count > 0 ? args[0].ToString() : null);
            }));

            builtins.Register(new BuiltinDescriptor("abs", 1, 1, (args, instr) => MathAbsVm(args)));

            builtins.Register(new BuiltinDescriptor("rand", 3, 3, (args, instr) =>
            {
                return MathRandVm(args);
            }));

            builtins.Register(new BuiltinDescriptor("floor", 1, 1, (args, instr) =>
                MathFloorVm(args)));

            builtins.Register(new BuiltinDescriptor("ceil", 1, 1, (args, instr) =>
                MathCeilVm(args)));

            builtins.Register(new BuiltinDescriptor("round", 1, 2, (args, instr) =>
                MathRoundVm(args)));

            builtins.Register(new BuiltinDescriptor("min", 2, 2, (args, instr) =>
            {
                return MathMinVm(args);
            }));

            builtins.Register(new BuiltinDescriptor("max", 2, 2, (args, instr) =>
            {
                return MathMaxVm(args);
            }));

            builtins.Register(new BuiltinDescriptor("sqrt", 1, 1, (args, instr) =>
                MathSqrtVm(args)));

            builtins.Register(new BuiltinDescriptor("pow", 2, 2, (args, instr) =>
                MathPowVm(args)));

            builtins.Register(new BuiltinDescriptor("log", 1, 2, (args, instr) =>
                MathLogVm(args)));

            builtins.Register(new BuiltinDescriptor("log10", 1, 1, (args, instr) =>
                MathLog10Vm(args)));

            builtins.Register(new BuiltinDescriptor("sin", 1, 1, (args, instr) =>
                MathSinVm(args)));

            builtins.Register(new BuiltinDescriptor("cos", 1, 1, (args, instr) =>
                MathCosVm(args)));

            builtins.Register(new BuiltinDescriptor("tan", 1, 1, (args, instr) =>
                MathTanVm(args)));

            builtins.Register(new BuiltinDescriptor("sign", 1, 1, (args, instr) =>
                MathSignVm(args)));

            builtins.Register(new BuiltinDescriptor("trunc", 1, 1, (args, instr) =>
                MathTruncVm(args)));

            builtins.Register(new BuiltinDescriptor("PI", 0, 0, (args, instr) => Math.PI));

            builtins.Register(new BuiltinDescriptor("E", 0, 0, (args, instr) => Math.E));

            // Regex builtins
            builtins.Register(new BuiltinDescriptor("regex_match", 2, 2, (args, instr) =>
            {
                return RegexMatchVm(args);
            }));

            builtins.Register(new BuiltinDescriptor("regex_matches", 2, 2, (args, instr) =>
            {
                return RegexMatchesVm(args);
            }));

            builtins.Register(new BuiltinDescriptor("regex_replace", 3, 3, (args, instr) =>
            {
                return RegexReplaceVm(args);
            }));

            builtins.Register(new BuiltinDescriptor("regex_test", 2, 2, (args, instr) =>
            {
                return RegexTestVm(args);
            }));

            builtins.Register(new BuiltinDescriptor("regex_split", 2, 2, (args, instr) =>
            {
                return RegexSplitVm(args);
            }));

            builtins.Register(new BuiltinDescriptor("isdigit", 1, 1, (args, instr) =>
            {
                if (args[0] is null) return false;
                return char.IsDigit(Convert.ToChar(args[0], CultureInfo.InvariantCulture));
            }));
            builtins.Register(new BuiltinDescriptor("isletter", 1, 1, (args, instr) =>
            {
                if (args[0] is null) return false;
                return char.IsLetter(Convert.ToChar(args[0], CultureInfo.InvariantCulture));
            }));
            builtins.Register(new BuiltinDescriptor("isalnum", 1, 1, (args, instr) =>
            {
                if (args[0] is null) return false;
                return char.IsLetterOrDigit(Convert.ToChar(args[0], CultureInfo.InvariantCulture));
            }));
            builtins.Register(new BuiltinDescriptor("isspace", 1, 1, (args, instr) =>
            {
                if (args[0] is null) return false;
                return char.IsWhiteSpace(Convert.ToChar(args[0], CultureInfo.InvariantCulture));
            }));

            builtins.Register(new BuiltinDescriptor("task", 0, 1, (args, instr) =>
            {
                if (args.Count == 0)
                    return TaskNamespace.Instance;

                return System.Threading.Tasks.Task.FromResult<object?>(args[0]);
            }, NativeCallOptions.Awaitable));

            builtins.Register(new BuiltinDescriptor("isarray", 1, 1, (args, instr) => args[0] is List<object>));
            builtins.Register(new BuiltinDescriptor("isdict", 1, 1, (args, instr) => args[0] is Dictionary<string, object>));

            builtins.Register(new BuiltinDescriptor("getfields", 1, 1, (args, instr) =>
            {
                if (args[0] is not Dictionary<string, object> d) return new List<object>();
                return SnapshotMutableDictionaryKeys(d).Cast<object>().ToList();
            }));

            builtins.Register(new BuiltinDescriptor("fromjson", 1, 1, (args, instr) =>
            {
                return JsonParseVm(args);
            }));


            builtins.Register(new BuiltinDescriptor("tojson", 1, 1, (args, instr) =>
            {
                return JsonStringifyVm(args);
            }));




            builtins.Register(new BuiltinDescriptor("fopen", 2, 2, (args, instr) =>
            {
                return FileOpenVm(args, instr);
            }));

            builtins.Register(new BuiltinDescriptor("fbopen", 2, 2, (args, instr) =>
            {
                return BinaryFileOpenVm(args, instr);
            }));

            builtins.Register(new BuiltinDescriptor("readAllBytes", 1, 1, (args, instr) =>
            {
                return ReadAllBytesVm(args, instr);
            }));

            builtins.Register(new BuiltinDescriptor("readAllBytesAsync", 1, 1, (args, instr) =>
            {
                return ReadAllBytesAsyncVm(args, instr);
            }, NativeCallOptions.Awaitable));

            builtins.Register(new BuiltinDescriptor("writeAllBytes", 2, 2, (args, instr) =>
            {
                return WriteAllBytesVm(args, instr);
            }));

            builtins.Register(new BuiltinDescriptor("textToBytes", 1, 2, (args, instr) =>
            {
                return TextToBytesVm(args, instr, "textToBytes");
            }));

            builtins.Register(new BuiltinDescriptor("bytesToText", 1, 2, (args, instr) =>
            {
                return BytesToTextVm(args, instr, "bytesToText");
            }));

            builtins.Register(new BuiltinDescriptor("encodeText", 1, 2, (args, instr) =>
            {
                return TextToBytesVm(args, instr, "encodeText");
            }));

            builtins.Register(new BuiltinDescriptor("decodeText", 1, 2, (args, instr) =>
            {
                return BytesToTextVm(args, instr, "decodeText");
            }));

            builtins.Register(new BuiltinDescriptor("convertEncoding", 3, 3, (args, instr) =>
            {
                return ConvertEncodingVm(args, instr, "convertEncoding");
            }));

            builtins.Register(new BuiltinDescriptor("hasEncoding", 1, 1, (args, instr) =>
            {
                return HasEncodingVm(args, instr, "hasEncoding");
            }));

            builtins.Register(new BuiltinDescriptor("encodingInfo", 1, 1, (args, instr) =>
            {
                return EncodingInfoVm(args, instr, "encodingInfo");
            }));

            builtins.Register(new BuiltinDescriptor("encodingNames", 0, 0, (args, instr) =>
            {
                return EncodingNamesVm();
            }));

            builtins.Register(new BuiltinDescriptor("encodings", 0, 0, (args, instr) =>
            {
                return EncodingsVm();
            }));

            builtins.Register(new BuiltinDescriptor("sleep", 1, 1, (args, instr) =>
            {
                int ms = Convert.ToInt32(args[0], CultureInfo.InvariantCulture);
                return Task.Run<object?>(async () => { await Task.Delay(ms).ConfigureAwait(false); return null; });
            }, NativeCallOptions.Awaitable));

            builtins.Register(new BuiltinDescriptor("nextTick", 0, 0, (args, instr) =>
            {
                return Task.Run<object?>(async () => { await Task.Yield(); return null; });
            }, NativeCallOptions.Awaitable));

            builtins.Register(new BuiltinDescriptor("getlAsync", 0, 0, (args, instr) =>
            {
                return ConsoleGetLineAsyncVm();
            }, NativeCallOptions.Awaitable));

            builtins.Register(new BuiltinDescriptor("getcAsync", 0, 0, (args, instr) =>
            {
                return ConsoleGetCharAsyncVm();
            }, NativeCallOptions.Awaitable));

            builtins.Register(new BuiltinDescriptor("readTextAsync", 1, 1, (args, instr) =>
            {
                return ReadTextAsyncVm(args, instr);
            }, NativeCallOptions.Awaitable));

            builtins.Register(new BuiltinDescriptor("writeTextAsync", 2, 2, (args, instr) =>
            {
                return WriteTextAsyncVm(args, instr);
            }, NativeCallOptions.Awaitable));

            builtins.Register(new BuiltinDescriptor("appendTextAsync", 2, 2, (args, instr) =>
            {
                return AppendTextAsyncVm(args, instr);
            }, NativeCallOptions.Awaitable));

            builtins.Register(new BuiltinDescriptor("fileSize", 1, 1, (args, instr) =>
            {
                return FileSizeVm(args, instr);
            }));

            builtins.Register(new BuiltinDescriptor("fileLastWriteTime", 1, 1, (args, instr) =>
            {
                return FileLastWriteTimeVm(args, instr);
            }));

            builtins.Register(new BuiltinDescriptor("fileLastWriteTimeUtc", 1, 1, (args, instr) =>
            {
                return FileLastWriteTimeUtcVm(args, instr);
            }));

            builtins.Register(new BuiltinDescriptor("fexist", 1, 1, (args, instr) =>
            {
                return FileExistsVm(args);
            }));
        }

        /// <summary>
        /// The RegisterString
        /// </summary>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
        private static void RegisterString(IIntrinsicRegistry intrinsics)
        {
            Type T = typeof(string);

            intrinsics.Register(T, new IntrinsicDescriptor("lower", 0, 0, (recv, a, i) =>
            {
                string s = recv?.ToString() ?? "";
                return s.ToLower();
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("upper", 0, 0, (recv, a, i) =>
            {
                string s = recv?.ToString() ?? "";
                return s.ToUpper();
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("trim", 0, 0, (recv, a, i) =>
            {
                string s = recv?.ToString() ?? "";
                return s.Trim();
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("rtrim", 0, 0, (recv, a, i) =>
            {
                string s = recv?.ToString() ?? "";
                return s.TrimEnd();
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("ltrim", 0, 0, (recv, a, i) =>
            {
                string s = recv?.ToString() ?? "";
                return s.TrimStart();
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("len", 0, 0, (recv, a, i) => ((string)(recv ?? ""))!.Length));
            intrinsics.Register(T, new IntrinsicDescriptor("contains", 1, 1, (recv, a, i) => ((string)(recv ?? ""))!.Contains(a[0].ToString()! ?? "")));
            intrinsics.Register(T, new IntrinsicDescriptor("substr", 2, 2, (recv, a, i) =>
            {
                string s = recv?.ToString() ?? "";
                int len = s.Length;
                int start = ClampIndex(Convert.ToInt32(a[0]), len);
                int length = Math.Max(0, Convert.ToInt32(a[1]));
                if (start > len) start = len;
                if (start + length > len) length = len - start;
                return s.Substring(start, length);
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("slice", 0, 2, (recv, a, instr) =>
            {
                string s = recv?.ToString() ?? "";
                int len = s.Length;
                object? startObj = a.Count > 0 ? a[0] : null;
                object? endObj = a.Count > 1 ? a[1] : null;
                (int st, int ex) = NormalizeSliceBounds(startObj, endObj, len);
                return s.Substring(st, ex - st);
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("replace_range", 3, 3, (recv, a, instr) =>
            {
                string s = recv?.ToString() ?? "";
                int len = s.Length;
                (int st, int ex) = NormalizeSliceBounds(a[0], a[1], len);
                string repl = a[2]?.ToString() ?? "";
                return s.Substring(0, st) + repl + s.Substring(ex);
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("remove_range", 2, 2, (recv, a, instr) =>
            {
                string s = recv?.ToString() ?? "";
                int len = s.Length;
                (int st, int ex) = NormalizeSliceBounds(a[0], a[1], len);
                return s.Substring(0, st) + s.Substring(ex);
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("replace", 2, 2, (recv, a, i) =>
            {
                string s = recv?.ToString() ?? "";
                if (a[0] is not string || a[1] is not string) return s;
                return s.Replace((string)a[0], (string)a[1]);
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("insert_at", 2, 2, (recv, a, instr) =>
            {
                string s = recv?.ToString() ?? "";
                int len = s.Length;
                int idx = ClampIndex(Convert.ToInt32(a[0]), len);
                string repl = a[1]?.ToString() ?? "";
                return s.Substring(0, idx) + repl + s.Substring(idx);
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("split", 1, 1, (recv, a, i) =>
            {
                string s = recv?.ToString() ?? "";
                string sep = a[0]?.ToString() ?? "";
                if (sep.Length == 0)
                    return s.Select(c => (object)c.ToString()).ToList();
                return s.Split(sep).Select(p => (object)p).ToList();
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("startsWith", 1, 1, (recv, a, i) =>
            {
                string s = recv?.ToString() ?? "";
                string prefix = a[0]?.ToString() ?? "";
                return s.StartsWith(prefix);
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("endsWith", 1, 1, (recv, a, i) =>
            {
                string s = recv?.ToString() ?? "";
                string suffix = a[0]?.ToString() ?? "";
                return s.EndsWith(suffix);
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("indexOf", 1, 1, (recv, a, i) =>
            {
                string s = recv?.ToString() ?? "";
                string needle = a[0]?.ToString() ?? "";
                return s.IndexOf(needle);
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("lastIndexOf", 1, 1, (recv, a, i) =>
            {
                string s = recv?.ToString() ?? "";
                string needle = a[0]?.ToString() ?? "";
                return s.LastIndexOf(needle);
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("repeat", 1, 1, (recv, a, i) =>
            {
                string s = recv?.ToString() ?? "";
                int count = Math.Max(0, Convert.ToInt32(a[0]));
                return string.Concat(Enumerable.Repeat(s, count));
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("padStart", 2, 2, (recv, a, i) =>
            {
                string s = recv?.ToString() ?? "";
                int totalWidth = Convert.ToInt32(a[0]);
                char padChar = (a[1]?.ToString() ?? " ").FirstOrDefault(' ');
                return s.PadLeft(totalWidth, padChar);
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("padEnd", 2, 2, (recv, a, i) =>
            {
                string s = recv?.ToString() ?? "";
                int totalWidth = Convert.ToInt32(a[0]);
                char padChar = (a[1]?.ToString() ?? " ").FirstOrDefault(' ');
                return s.PadRight(totalWidth, padChar);
            }));
        }

        /// <summary>
        /// The RegisterArray
        /// </summary>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
        private static void RegisterArray(IIntrinsicRegistry intrinsics)
        {
            Type T = typeof(List<object>);

            intrinsics.Register(T, new IntrinsicDescriptor("len", 0, 0, (recv, a, i) =>
                LockMutableRuntime((List<object>)recv, () => ((List<object>)recv).Count)));
            intrinsics.Register(T, new IntrinsicDescriptor("push", 1, 1, (recv, a, i) =>
            {
                List<object> arr = (List<object>)recv;
                return LockMutableRuntime(arr, () =>
                {
                    arr.Add(a[0]);
                    return arr.Count;
                });
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("pop", 0, 0, (recv, a, i) =>
            {
                List<object> arr = (List<object>)recv;
                return LockMutableRuntime(arr, () =>
                {
                    if (arr.Count == 0) return null!;
                    object last = arr[^1];
                    arr.RemoveAt(arr.Count - 1);
                    return last;
                });
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("insert_at", 2, 2, (recv, a, i) =>
            {
                List<object> arr = (List<object>)recv;
                LockMutableRuntime(arr, () =>
                {
                    int idx = ClampIndex(Convert.ToInt32(a[0]), arr.Count);
                    arr.Insert(idx, a[1]);
                });
                return arr;
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("remove_range", 2, 2, (recv, a, i) =>
            {
                List<object> arr = (List<object>)recv;
                LockMutableRuntime(arr, () =>
                {
                    (int st, int ex) = NormalizeSliceBounds(a[0], a[1], arr.Count);
                    arr.RemoveRange(st, ex - st);
                });
                return arr;
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("replace_range", 3, 3, (recv, a, i) =>
            {
                List<object> arr = (List<object>)recv;
                List<object>? replacement = a[2] is List<object> replList ? SnapshotMutableList(replList) : null;
                LockMutableRuntime(arr, () =>
                {
                    (int st, int ex) = NormalizeSliceBounds(a[0], a[1], arr.Count);
                    if (replacement != null)
                    {
                        arr.RemoveRange(st, ex - st);
                        arr.InsertRange(st, replacement);
                    }
                    else
                    {
                        arr.RemoveRange(st, ex - st);
                        arr.Insert(st, a[2]);
                    }
                });
                return arr;
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("slice", 0, 2, (recv, a, instr) =>
            {
                List<object> arr = (List<object>)recv;
                object? startObj = a.Count > 0 ? a[0] : null;
                object? endObj = a.Count > 1 ? a[1] : null;
                return LockMutableRuntime(arr, () =>
                {
                    (int st, int ex) = NormalizeSliceBounds(startObj, endObj, arr.Count);
                    return (object)arr.GetRange(st, ex - st);
                });
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("sort", 0, 0, (recv, a, i) =>
            {
                List<object> arr = (List<object>)recv;
                LockMutableRuntime(arr, () => arr.Sort((x, y) =>
                {
                    if (x is null && y is null) return 0;
                    if (x is null) return -1;
                    if (y is null) return 1;
                    if (x is IComparable cx) return cx.CompareTo(y);
                    return string.Compare(x.ToString(), y.ToString(), StringComparison.Ordinal);
                }));
                return arr;
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("reverse", 0, 0, (recv, a, i) =>
            {
                List<object> arr = (List<object>)recv;
                LockMutableRuntime(arr, arr.Reverse);
                return arr;
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("indexOf", 1, 1, (recv, a, i) =>
            {
                List<object> arr = SnapshotMutableList((List<object>)recv);
                object target = a[0];
                for (int idx = 0; idx < arr.Count; idx++)
                {
                    if (Equals(arr[idx], target)) return idx;
                }
                return -1;
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("includes", 1, 1, (recv, a, i) =>
            {
                List<object> arr = SnapshotMutableList((List<object>)recv);
                object target = a[0];
                foreach (object item in arr)
                {
                    if (Equals(item, target)) return true;
                }
                return false;
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("join", 1, 1, (recv, a, i) =>
            {
                List<object> arr = SnapshotMutableList((List<object>)recv);
                string sep = a[0]?.ToString() ?? "";
                return string.Join(sep, arr.Select(x => x?.ToString() ?? ""));
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("flat", 0, 1, (recv, a, instr) =>
            {
                List<object> arr = SnapshotMutableList((List<object>)recv);
                int depth = a.Count > 0 ? Convert.ToInt32(a[0]) : 1;
                return FlattenList(arr, depth);
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("map", 1, 1, (recv, a, instr) =>
            {
                List<object> arr = SnapshotMutableList((List<object>)recv);
                Closure fn = (Closure)a[0];
                VM vm = VM.CurrentVm!;
                List<object> result = new(arr.Count);
                for (int idx = 0; idx < arr.Count; idx++)
                {
                    List<object> cbArgs = fn.Parameters.Count >= 2
                        ? new List<object> { arr[idx], idx }
                        : new List<object> { arr[idx] };
                    result.Add(vm.InvokeClosureSync(fn, cbArgs, instr)!);
                }
                return result;
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("filter", 1, 1, (recv, a, instr) =>
            {
                List<object> arr = SnapshotMutableList((List<object>)recv);
                Closure fn = (Closure)a[0];
                VM vm = VM.CurrentVm!;
                List<object> result = new();
                for (int idx = 0; idx < arr.Count; idx++)
                {
                    List<object> cbArgs = fn.Parameters.Count >= 2
                        ? new List<object> { arr[idx], idx }
                        : new List<object> { arr[idx] };
                    object? val = vm.InvokeClosureSync(fn, cbArgs, instr);
                    if (val is true) result.Add(arr[idx]);
                }
                return result;
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("reduce", 2, 2, (recv, a, instr) =>
            {
                List<object> arr = SnapshotMutableList((List<object>)recv);
                Closure fn = (Closure)a[0];
                object accumulator = a[1];
                VM vm = VM.CurrentVm!;
                foreach (object item in arr)
                {
                    accumulator = vm.InvokeClosureSync(fn, new List<object> { accumulator, item }, instr)!;
                }
                return accumulator;
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("find", 1, 1, (recv, a, instr) =>
            {
                List<object> arr = SnapshotMutableList((List<object>)recv);
                Closure fn = (Closure)a[0];
                VM vm = VM.CurrentVm!;
                for (int idx = 0; idx < arr.Count; idx++)
                {
                    List<object> cbArgs = fn.Parameters.Count >= 2
                        ? new List<object> { arr[idx], idx }
                        : new List<object> { arr[idx] };
                    object? val = vm.InvokeClosureSync(fn, cbArgs, instr);
                    if (val is true) return arr[idx];
                }
                return null!;
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("findIndex", 1, 1, (recv, a, instr) =>
            {
                List<object> arr = SnapshotMutableList((List<object>)recv);
                Closure fn = (Closure)a[0];
                VM vm = VM.CurrentVm!;
                for (int idx = 0; idx < arr.Count; idx++)
                {
                    List<object> cbArgs = fn.Parameters.Count >= 2
                        ? new List<object> { arr[idx], idx }
                        : new List<object> { arr[idx] };
                    object? val = vm.InvokeClosureSync(fn, cbArgs, instr);
                    if (val is true) return idx;
                }
                return -1;
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("every", 1, 1, (recv, a, instr) =>
            {
                List<object> arr = SnapshotMutableList((List<object>)recv);
                Closure fn = (Closure)a[0];
                VM vm = VM.CurrentVm!;
                foreach (object item in arr)
                {
                    object? val = vm.InvokeClosureSync(fn, new List<object> { item }, instr);
                    if (val is not true) return false;
                }
                return true;
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("some", 1, 1, (recv, a, instr) =>
            {
                List<object> arr = SnapshotMutableList((List<object>)recv);
                Closure fn = (Closure)a[0];
                VM vm = VM.CurrentVm!;
                foreach (object item in arr)
                {
                    object? val = vm.InvokeClosureSync(fn, new List<object> { item }, instr);
                    if (val is true) return true;
                }
                return false;
            }));
        }

        /// <summary>
        /// The RegisterDict
        /// </summary>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
        private static void RegisterDict(IIntrinsicRegistry intrinsics)
        {
            Type T = typeof(Dictionary<string, object>);
            intrinsics.Register(T, new IntrinsicDescriptor("len", 0, 0, (r, a, i) =>
                LockMutableRuntime((Dictionary<string, object>)r, () => ((Dictionary<string, object>)r).Count)));
            intrinsics.Register(T, new IntrinsicDescriptor("contains", 1, 1, (r, a, i) =>
            {
                Dictionary<string, object> d = (Dictionary<string, object>)r;
                string key = a[0]?.ToString() ?? "";
                return LockMutableRuntime(d, () => d.ContainsKey(key));
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("remove", 1, 1, (r, a, i) =>
            {
                Dictionary<string, object> d = (Dictionary<string, object>)r;
                string key = a[0]?.ToString() ?? "";
                return LockMutableRuntime(d, () => d.Remove(key));
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("keys", 0, 0, (r, a, i) =>
                SnapshotMutableDictionaryKeys((Dictionary<string, object>)r).Cast<object>().ToList()));
            intrinsics.Register(T, new IntrinsicDescriptor("values", 0, 0, (r, a, i) =>
                SnapshotMutableDictionaryEntries((Dictionary<string, object>)r).Select(kv => kv.Value).ToList()));
            intrinsics.Register(T, new IntrinsicDescriptor("set", 2, 2, (r, a, i) =>
            {
                Dictionary<string, object> d = (Dictionary<string, object>)r;
                string key = a[0]?.ToString() ?? "";
                LockMutableRuntime(d, () => d[key] = a[1]);
                return d;
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("get_or", 2, 2, (r, a, i) =>
            {
                Dictionary<string, object> d = (Dictionary<string, object>)r;
                string key = a[0]?.ToString() ?? "";
                return LockMutableRuntime(d, () => d.TryGetValue(key, out object? v) ? v : a[1]);
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("entries", 0, 0, (r, a, i) =>
            {
                return SnapshotMutableDictionaryEntries((Dictionary<string, object>)r).Select(kv =>
                {
                    List<object> pair = new() { kv.Key, kv.Value };
                    return (object)pair;
                }).ToList();
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("merge", 1, 1, (r, a, i) =>
            {
                Dictionary<string, object> d = (Dictionary<string, object>)r;
                if (a[0] is Dictionary<string, object> other)
                {
                    List<KeyValuePair<string, object>> otherEntries = SnapshotMutableDictionaryEntries(other);
                    LockMutableRuntime(d, () =>
                    {
                        foreach (KeyValuePair<string, object> kv in otherEntries)
                            d[kv.Key] = kv.Value;
                    });
                }
                return d;
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("clone", 0, 0, (r, a, i) =>
            {
                Dictionary<string, object> d = (Dictionary<string, object>)r;
                return SnapshotMutableDictionaryEntries(d).ToDictionary(kv => kv.Key, kv => kv.Value);
            }));
        }

        /// <summary>
        /// The RegisterDateTime
        /// </summary>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
        private static void RegisterDateTime(IIntrinsicRegistry intrinsics)
        {
            Type T = typeof(DateTime);

            static DateTime ParseDt(object? v)
            {
                if (v is DateTime dt) return dt;

                string s = v?.ToString() ?? "";
                if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime p))
                    return p;

                if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n))
                {
                    if (n >= 100_000_000_000L) return DateTimeOffset.FromUnixTimeMilliseconds(n).LocalDateTime;
                    return DateTimeOffset.FromUnixTimeSeconds(n).LocalDateTime;
                }

                return DateTime.MinValue;
            }

            static DateTimeKind ParseKind(object? v)
            {
                if (v is DateTimeKind k) return k;
                string s = (v?.ToString() ?? "").Trim().ToLowerInvariant();
                return s switch
                {
                    "utc" => DateTimeKind.Utc,
                    "local" => DateTimeKind.Local,
                    "unspecified" => DateTimeKind.Unspecified,
                    "0" => DateTimeKind.Unspecified,
                    "1" => DateTimeKind.Utc,
                    "2" => DateTimeKind.Local,
                    _ => DateTimeKind.Unspecified
                };
            }

            intrinsics.Register(T, new IntrinsicDescriptor("year", 0, 0, (r, a, i) => ((DateTime)r).Year));
            intrinsics.Register(T, new IntrinsicDescriptor("month", 0, 0, (r, a, i) => ((DateTime)r).Month));
            intrinsics.Register(T, new IntrinsicDescriptor("day", 0, 0, (r, a, i) => ((DateTime)r).Day));
            intrinsics.Register(T, new IntrinsicDescriptor("hour", 0, 0, (r, a, i) => ((DateTime)r).Hour));
            intrinsics.Register(T, new IntrinsicDescriptor("minute", 0, 0, (r, a, i) => ((DateTime)r).Minute));
            intrinsics.Register(T, new IntrinsicDescriptor("second", 0, 0, (r, a, i) => ((DateTime)r).Second));
            intrinsics.Register(T, new IntrinsicDescriptor("millisecond", 0, 0, (r, a, i) => ((DateTime)r).Millisecond));
            intrinsics.Register(T, new IntrinsicDescriptor("dayOfWeek", 0, 0, (r, a, i) => (int)((DateTime)r).DayOfWeek));
            intrinsics.Register(T, new IntrinsicDescriptor("dayOfYear", 0, 0, (r, a, i) => ((DateTime)r).DayOfYear));
            intrinsics.Register(T, new IntrinsicDescriptor("ticks", 0, 0, (r, a, i) => ((DateTime)r).Ticks));
            intrinsics.Register(T, new IntrinsicDescriptor("kind", 0, 0, (r, a, i) => ((DateTime)r).Kind.ToString()));
            intrinsics.Register(T, new IntrinsicDescriptor("dateOnly", 0, 0, (r, a, i) => ((DateTime)r).Date));
            intrinsics.Register(T, new IntrinsicDescriptor("timeOfDayTicks", 0, 0, (r, a, i) => ((DateTime)r).TimeOfDay.Ticks));

            intrinsics.Register(T, new IntrinsicDescriptor("toUnixSeconds", 0, 0, (r, a, i) => new DateTimeOffset((DateTime)r).ToUnixTimeSeconds()));
            intrinsics.Register(T, new IntrinsicDescriptor("toUnixMilliseconds", 0, 0, (r, a, i) => new DateTimeOffset((DateTime)r).ToUnixTimeMilliseconds()));

            intrinsics.Register(T, new IntrinsicDescriptor("toString", 0, 1, (r, a, i) =>
            {
                DateTime dt = (DateTime)r;
                string fmt = a.Count > 0 ? (a[0]?.ToString() ?? "yyyy-MM-dd HH:mm:ss") : "yyyy-MM-dd HH:mm:ss";
                return dt.ToString(fmt, CultureInfo.InvariantCulture);
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("toLocalTime", 0, 0, (r, a, i) => ((DateTime)r).ToLocalTime()));
            intrinsics.Register(T, new IntrinsicDescriptor("toUniversalTime", 0, 0, (r, a, i) => ((DateTime)r).ToUniversalTime()));
            intrinsics.Register(T, new IntrinsicDescriptor("withKind", 1, 1, (r, a, i) =>
            {
                DateTime dt = (DateTime)r;
                DateTimeKind kind = ParseKind(a[0]);
                return DateTime.SpecifyKind(dt, kind);
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("addYears", 1, 1, (r, a, i) => ((DateTime)r).AddYears(Convert.ToInt32(a[0], CultureInfo.InvariantCulture))));
            intrinsics.Register(T, new IntrinsicDescriptor("addMonths", 1, 1, (r, a, i) => ((DateTime)r).AddMonths(Convert.ToInt32(a[0], CultureInfo.InvariantCulture))));
            intrinsics.Register(T, new IntrinsicDescriptor("addDays", 1, 1, (r, a, i) => ((DateTime)r).AddDays(Convert.ToDouble(a[0], CultureInfo.InvariantCulture))));
            intrinsics.Register(T, new IntrinsicDescriptor("addHours", 1, 1, (r, a, i) => ((DateTime)r).AddHours(Convert.ToDouble(a[0], CultureInfo.InvariantCulture))));
            intrinsics.Register(T, new IntrinsicDescriptor("addMinutes", 1, 1, (r, a, i) => ((DateTime)r).AddMinutes(Convert.ToDouble(a[0], CultureInfo.InvariantCulture))));
            intrinsics.Register(T, new IntrinsicDescriptor("addSeconds", 1, 1, (r, a, i) => ((DateTime)r).AddSeconds(Convert.ToDouble(a[0], CultureInfo.InvariantCulture))));
            intrinsics.Register(T, new IntrinsicDescriptor("addMilliseconds", 1, 1, (r, a, i) => ((DateTime)r).AddMilliseconds(Convert.ToDouble(a[0], CultureInfo.InvariantCulture))));
            intrinsics.Register(T, new IntrinsicDescriptor("addTicks", 1, 1, (r, a, i) => ((DateTime)r).AddTicks(Convert.ToInt64(a[0], CultureInfo.InvariantCulture))));

            intrinsics.Register(T, new IntrinsicDescriptor("compareTo", 1, 1, (r, a, i) => ((DateTime)r).CompareTo(ParseDt(a[0]))));
            intrinsics.Register(T, new IntrinsicDescriptor("diffMs", 1, 1, (r, a, i) =>
            {
                DateTime dt = (DateTime)r;
                DateTime other = ParseDt(a[0]);
                return (dt - other).TotalMilliseconds;
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("diffTicks", 1, 1, (r, a, i) =>
            {
                DateTime dt = (DateTime)r;
                DateTime other = ParseDt(a[0]);
                return (dt - other).Ticks;
            }));

            intrinsics.Register(typeof(string), new IntrinsicDescriptor("toDateTime", 0, 1, (recv, a, i) =>
            {
                string s = recv?.ToString() ?? "";
                if (a.Count == 0)
                {
                    return ParseDt(s);
                }
                else
                {
                    string fmt = a[0]?.ToString() ?? "yyyy-MM-dd HH:mm:ss";
                    return DateTime.ParseExact(s, fmt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal);
                }
            }));

            intrinsics.Register(typeof(string), new IntrinsicDescriptor("toUnixSeconds", 0, 0, (recv, a, i) =>
            {
                string s = recv?.ToString() ?? "0";
                long n = Convert.ToInt64(s, CultureInfo.InvariantCulture);
                return DateTimeOffset.FromUnixTimeSeconds(n).LocalDateTime;
            }));
            intrinsics.Register(typeof(string), new IntrinsicDescriptor("toUnixMilliseconds", 0, 0, (recv, a, i) =>
            {
                string s = recv?.ToString() ?? "0";
                long n = Convert.ToInt64(s, CultureInfo.InvariantCulture);
                return DateTimeOffset.FromUnixTimeMilliseconds(n).LocalDateTime;
            }));
        }

        /// <summary>
        /// The RegisterDirectoryInfo
        /// </summary>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
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
                if (!AllowFileIO)
                    throw new VMException("Runtime error: file I/O is disabled (AllowFileIO=false)", instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
                DirectoryInfo di = (DirectoryInfo)r; di.CreationTime = ParseDt(a[0]); return di;
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("setLastAccessTime", 1, 1, (r, a, instr) =>
            {
                if (!AllowFileIO)
                    throw new VMException("Runtime error: file I/O is disabled (AllowFileIO=false)", instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
                DirectoryInfo di = (DirectoryInfo)r; di.LastAccessTime = ParseDt(a[0]); return di;
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("setLastWriteTime", 1, 1, (r, a, instr) =>
            {
                if (!AllowFileIO)
                    throw new VMException("Runtime error: file I/O is disabled (AllowFileIO=false)", instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
                DirectoryInfo di = (DirectoryInfo)r; di.LastWriteTime = ParseDt(a[0]); return di;
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("setAttributes", 1, 1, (r, a, instr) =>
            {
                if (!AllowFileIO)
                    throw new VMException("Runtime error: file I/O is disabled (AllowFileIO=false)", instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
                DirectoryInfo di = (DirectoryInfo)r;
                long val = Convert.ToInt64(a[0], CultureInfo.InvariantCulture);
                di.Attributes = (FileAttributes)val; return di;
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("create", 0, 0, (r, a, instr) =>
            {
                if (!AllowFileIO)
                    throw new VMException("Runtime error: file I/O is disabled (AllowFileIO=false)", instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
                ((DirectoryInfo)r).Create(); return r;
            }));
            object DeleteDirectoryInfo(object r, List<object> a, Instruction instr)
            {
                if (!AllowFileIO)
                    throw new VMException("Runtime error: file I/O is disabled (AllowFileIO=false)", instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
                bool recursive = a.Count > 0 && Convert.ToBoolean(a[0], CultureInfo.InvariantCulture);
                ((DirectoryInfo)r).Delete(recursive); return 1;
            }

            intrinsics.Register(T, new IntrinsicDescriptor("delete", 0, 1, DeleteDirectoryInfo));
            intrinsics.Register(T, new IntrinsicDescriptor("deleteDir", 0, 1, DeleteDirectoryInfo));
            intrinsics.Register(T, new IntrinsicDescriptor("refresh", 0, 0, (r, a, instr) =>
            {
                if (!AllowFileIO)
                    throw new VMException("Runtime error: file I/O is disabled (AllowFileIO=false)", instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
                ((DirectoryInfo)r).Refresh(); return r;
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("moveTo", 1, 1, (r, a, instr) =>
            {
                if (!AllowFileIO)
                    throw new VMException("Runtime error: file I/O is disabled (AllowFileIO=false)", instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
                string dest = a[0]?.ToString() ?? "";
                ((DirectoryInfo)r).MoveTo(dest); return r;
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("createSubdirectory", 1, 1, (r, a, instr) =>
            {
                if (!AllowFileIO)
                    throw new VMException("Runtime error: file I/O is disabled (AllowFileIO=false)", instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
                string name = a[0]?.ToString() ?? "";
                return ((DirectoryInfo)r).CreateSubdirectory(name);
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("getFiles", 0, 2, (r, a, instr) =>
            {
                if (!AllowFileIO)
                    throw new VMException("Runtime error: file I/O is disabled (AllowFileIO=false)", instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
                DirectoryInfo di = (DirectoryInfo)r;
                string pattern = a.Count >= 1 ? a[0]?.ToString() ?? "*" : "*";
                SearchOption opt = (a.Count >= 2 && Convert.ToBoolean(a[1], CultureInfo.InvariantCulture))
                    ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                return di.GetFiles(pattern, opt).Select(f => (object)f.FullName).ToList();
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("getDirectories", 0, 2, (r, a, instr) =>
            {
                if (!AllowFileIO)
                    throw new VMException("Runtime error: file I/O is disabled (AllowFileIO=false)", instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
                DirectoryInfo di = (DirectoryInfo)r;
                string pattern = a.Count >= 1 ? a[0]?.ToString() ?? "*" : "*";
                SearchOption opt = (a.Count >= 2 && Convert.ToBoolean(a[1], CultureInfo.InvariantCulture))
                    ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                return di.GetDirectories(pattern, opt).Select(d => (object)d.FullName).ToList();
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("enumerateFileSystem", 0, 2, (r, a, instr) =>
            {
                if (!AllowFileIO)
                    throw new VMException("Runtime error: file I/O is disabled (AllowFileIO=false)", instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
                DirectoryInfo di = (DirectoryInfo)r;
                string pattern = a.Count >= 1 ? a[0]?.ToString() ?? "*" : "*";
                SearchOption opt = (a.Count >= 2 && Convert.ToBoolean(a[1], CultureInfo.InvariantCulture))
                    ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                return di.EnumerateFileSystemInfos(pattern, opt).Select(fi => (object)fi.FullName).ToList();
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("existsOrCreate", 0, 0, (r, a, instr) =>
            {
                if (!AllowFileIO)
                    throw new VMException("Runtime error: file I/O is disabled (AllowFileIO=false)", instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
                DirectoryInfo di = (DirectoryInfo)r;
                if (!di.Exists) di.Create();
                return di.Exists;
            }));

            intrinsics.Register(typeof(string), new IntrinsicDescriptor("dirinfo", 0, 0, (recv, a, instr) =>
            {
                if (!AllowFileIO)
                    throw new VMException("Runtime error: file I/O is disabled (AllowFileIO=false)", instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
                return new DirectoryInfo(recv?.ToString() ?? "");
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("toString", 0, 0, (r, a, instr) => ((DirectoryInfo)r).FullName));
        }

        /// <summary>
        /// The RegisterException
        /// </summary>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
        private static void RegisterException(IIntrinsicRegistry intrinsics)
        {
            Type T = typeof(ExceptionObject);
            intrinsics.Register(T, new IntrinsicDescriptor("message", 0, 0, (r, a, i) => ((ExceptionObject)r).eMessage));
            intrinsics.Register(T, new IntrinsicDescriptor("type", 0, 0, (r, a, i) => ((ExceptionObject)r).Type));
            intrinsics.Register(T, new IntrinsicDescriptor("file", 0, 0, (r, a, i) => ((ExceptionObject)r).File));
            intrinsics.Register(T, new IntrinsicDescriptor("line", 0, 0, (r, a, i) => ((ExceptionObject)r).Line));
            intrinsics.Register(T, new IntrinsicDescriptor("col", 0, 0, (r, a, i) => ((ExceptionObject)r).Col));
            intrinsics.Register(T, new IntrinsicDescriptor("stack", 0, 0, (r, a, i) => ((ExceptionObject)r).Stack));
            intrinsics.Register(T, new IntrinsicDescriptor("toString", 0, 0, (r, a, i) => ((ExceptionObject)r).ToString()));
        }

        /// <summary>
        /// The RegisterFile
        /// </summary>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
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

        /// <summary>
        /// The JsonToVmValue
        /// </summary>
        /// <param name="elem">The elem<see cref="JsonElement"/></param>
        /// <returns>The <see cref="object?"/></returns>
        private static object? JsonToVmValue(JsonElement elem)
        {
            switch (elem.ValueKind)
            {
                case JsonValueKind.Object:
                    {
                        Dictionary<string, object> dict = new(StringComparer.Ordinal);
                        foreach (JsonProperty prop in elem.EnumerateObject())
                            dict[prop.Name] = JsonToVmValue(prop.Value)!;
                        return dict;
                    }

                case JsonValueKind.Array:
                    {
                        List<object> list = new();
                        foreach (JsonElement item in elem.EnumerateArray())
                            list.Add(JsonToVmValue(item)!);
                        return list;
                    }

                case JsonValueKind.String:
                    return elem.GetString() ?? string.Empty;

                case JsonValueKind.Number:
                    {
                        if (elem.TryGetInt32(out int i32)) return i32;
                        if (elem.TryGetInt64(out long i64)) return i64;
                        if (elem.TryGetDecimal(out decimal dec)) return dec;
                        if (elem.TryGetDouble(out double dbl)) return dbl;
                        return elem.GetRawText();
                    }

                case JsonValueKind.True:
                    return true;

                case JsonValueKind.False:
                    return false;

                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    return null;

                default:
                    return elem.GetRawText();
            }
        }

        /// <summary>
        /// The ToNumber
        /// </summary>
        /// <param name="v">The v<see cref="object?"/></param>
        /// <returns>The <see cref="object"/></returns>
        private static object ToNumber(object? v)
        {
            if (v is null) return 0;
            if (v is int or long or short or sbyte or byte or ushort or uint or ulong) return Convert.ToInt64(v, CultureInfo.InvariantCulture);
            if (v is float or double or decimal) return Convert.ToDouble(v, CultureInfo.InvariantCulture);
            if (v is bool b) return b ? 1 : 0;
            if (v is char c) return (int)c;
            if (v is string s)
            {
                if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long li)) return li;
                if (double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double dd)) return dd;
                return 0;
            }
            return 0;
        }
        private static object? NormalizeForJson(object? value)
        {
            if (value is null) return null;
            if (value is string or bool or int or long or double or float or decimal or char) return value;
            if (value is BigInteger bi) return bi.ToString();

            if (value is ClassInstance inst)
            {
                Dictionary<string, object>? visMap = null;
                if (LockMutableRuntime(inst, () => inst.Fields.TryGetValue("__type", out object? innerType) ? innerType : null) is StaticInstance st
                    && LockMutableRuntime(st, () => st.Fields.TryGetValue("__vis_inst", out object? innerVis) ? innerVis : null) is Dictionary<string, object> vm)
                {
                    visMap = vm;
                }

                var result = new Dictionary<string, object>();
                foreach (var kvp in LockMutableRuntime(inst, () => inst.Fields.ToList()))
                {
                    if (kvp.Key.StartsWith("__")) continue;
                    if (kvp.Value is Closure) continue;
                    if (kvp.Value is StaticInstance) continue;

                    if (visMap != null && LockMutableRuntime(visMap, () => visMap.TryGetValue(kvp.Key, out object? rawVis) ? rawVis : null) is object rawVis)
                    {
                        int vis = rawVis is int i ? i : 0;
                        if (vis != 0) continue;
                    }

                    result[kvp.Key] = NormalizeForJson(kvp.Value)!;
                }
                return result;
            }

            if (value is List<object> list)
            {
                List<object> snapshot = SnapshotMutableList(list);
                var result = new List<object>(snapshot.Count);
                foreach (var item in snapshot)
                    result.Add(NormalizeForJson(item)!);
                return result;
            }

            if (value is Dictionary<string, object> dict)
            {
                List<KeyValuePair<string, object>> snapshot = SnapshotMutableDictionaryEntries(dict);
                var result = new Dictionary<string, object>(snapshot.Count);
                foreach (var kvp in snapshot)
                    result[kvp.Key] = NormalizeForJson(kvp.Value)!;
                return result;
            }

            return value.ToString();
        }

        /// <summary>
        /// The JsonStringify
        /// </summary>
        /// <param name="v">The v<see cref="object?"/></param>
        /// <returns>The <see cref="string"/></returns>
        private static string JsonStringify(object? v)
        {
            JsonSerializerOptions options = new()
            {
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                WriteIndented = false
            };

            object? Normalize(object? x)
            {
                if (x is null) return null;
                if (x is string or bool or int or long or double or float or decimal or char) return x;
                if (x is List<object> list) return SnapshotMutableList(list).Select(Normalize).ToList();
                if (x is Dictionary<string, object> dict)
                    return SnapshotMutableDictionaryEntries(dict).ToDictionary(k => k.Key, k => Normalize(k.Value)!);
                return x.ToString();
            }

            return JsonSerializer.Serialize(Normalize(v), options);
        }

        /// <summary>
        /// The ClampIndex
        /// </summary>
        /// <param name="idx">The idx<see cref="int"/></param>
        /// <param name="len">The len<see cref="int"/></param>
        /// <returns>The <see cref="int"/></returns>
        private static List<object> FlattenList(List<object> list, int depth)
        {
            List<object> result = new();
            foreach (object item in SnapshotMutableList(list))
            {
                if (depth > 0 && item is List<object> subList)
                    result.AddRange(FlattenList(subList, depth - 1));
                else
                    result.Add(item);
            }
            return result;
        }

        private static int ClampIndex(int idx, int len)
        {
            if (idx < 0) idx += len;
            if (idx < 0) idx = 0;
            if (idx > len) idx = len;
            return idx;
        }

        /// <summary>
        /// The NormalizeSliceBounds
        /// </summary>
        /// <param name="startObj">The startObj<see cref="object?"/></param>
        /// <param name="endObj">The endObj<see cref="object?"/></param>
        /// <param name="len">The len<see cref="int"/></param>
        /// <returns>The <see cref="(int start, int endEx)"/></returns>
        private static (int start, int endEx) NormalizeSliceBounds(object? startObj, object? endObj, int len)
        {
            bool startIsNull = startObj == null;
            int start = startIsNull ? 0 : Convert.ToInt32(startObj);
            if (start < 0) start += len;

            int endEx;
            if (endObj == null)
            {
                endEx = len;
            }
            else
            {
                int rawEnd = Convert.ToInt32(endObj);
                if (rawEnd == 0 && startIsNull) endEx = len;
                else
                {
                    endEx = rawEnd;
                    if (endEx < 0) endEx += len;
                }
            }

            start = Math.Clamp(start, 0, len);
            endEx = Math.Clamp(endEx, 0, len);
            if (endEx < start) endEx = start;
            return (start, endEx);
        }
    }
}
