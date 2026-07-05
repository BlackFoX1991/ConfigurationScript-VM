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
using System.Numerics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using static CFGS_VM.VMCore.VM;

namespace CFGS_VM.StandardLibrary
{
    /// <summary>
    /// Built-in (non-IO) standard library. The VM auto-registers this at construction, so scripts
    /// no longer need to import it. File-system APIs live in the separate <c>CFGS.IO</c> plugin.
    /// </summary>
    internal static class CoreStandardLibrary
    {
        static CoreStandardLibrary()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
        };
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
            if (t == typeof(StringBuilderHandle)) return "StringBuilder";
            if (t == typeof(NamespaceBuiltinCallable)) return "BuiltinCallable";
            return t.Name;
        }

        private static string VmStringify(object? value)
        {
            using StringWriter writer = new();
            PrintValue(value, writer);
            return writer.ToString();
        }

        private static string VmStringArg(object? value) => VmStringify(value);

        private static object NormalizeFormatArg(object? value) => value switch
        {
            null => "null",
            string s => s,
            bool b => b ? "true" : "false",
            sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal or BigInteger or char or DateTime or DateTimeOffset or TimeSpan => value,
            _ => VmStringify(value)
        };

        private static bool IsImplicitReceiverParameter(string name)
            => string.Equals(name, "this", StringComparison.Ordinal) ||
               string.Equals(name, "type", StringComparison.Ordinal);

        private static int GetCallableExposedParameterCount(object callback, Instruction instr)
        {
            return callback switch
            {
                Closure closure => GetClosureExposedParameterCount(closure),
                BoundMethod boundMethod => GetClosureExposedParameterCount(boundMethod.Function),
                _ => throw new VMException(
                    $"Runtime error: callback must be a function or bound method (got {callback?.GetType().Name ?? "null"})",
                    instr.Line,
                    instr.Col,
                    instr.OriginFile,
                    VM.IsDebugging,
                    VM.DebugStream!)
            };
        }

        private static int GetClosureExposedParameterCount(Closure closure)
        {
            int receiverOffset = closure.Parameters.Count > 0 && IsImplicitReceiverParameter(closure.Parameters[0]) ? 1 : 0;
            return closure.Parameters.Count - receiverOffset;
        }

        private static object? InvokeCallbackSync(VM vm, object callback, List<object> args, Instruction instr)
        {
            return callback switch
            {
                Closure closure => vm.InvokeClosureSync(closure, args, instr),
                BoundMethod boundMethod => vm.InvokeClosureSync(boundMethod.Function, args, instr, boundMethod.Receiver, boundMethod.DeclaringType),
                _ => throw new VMException(
                    $"Runtime error: callback must be a function or bound method (got {callback?.GetType().Name ?? "null"})",
                    instr.Line,
                    instr.Col,
                    instr.OriginFile,
                    VM.IsDebugging,
                    VM.DebugStream!)
            };
        }

        public static void Register(IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics, INamespaceRegistry namespaces)
        {
            RegisterBuiltins(builtins);
            RegisterNamespaces(namespaces);
            RegisterString(intrinsics);
            RegisterStringBuilder(intrinsics);
            RegisterArray(intrinsics);
            RegisterDict(intrinsics);
            RegisterException(intrinsics);
            RegisterDateTime(intrinsics);
            RegisterTaskNamespace(intrinsics);
            RegisterEncodingNamespace(intrinsics);
            RegisterRegexNamespace(intrinsics);
            RegisterJsonNamespace(intrinsics);
            RegisterMathNamespace(intrinsics);
            RegisterConsoleNamespace(intrinsics);
            RegisterEventLogNamespace(intrinsics);
        }

        private static void RegisterNamespaces(INamespaceRegistry namespaces)
        {
            RegisterNamespaceAliases(namespaces, "console", ConsoleNamespace.Instance, [
                "print", "put", "clear", "getl", "getc", "readLine", "readChar",
                "getlAsync", "getcAsync", "readLineAsync", "readCharAsync"
            ]);

            RegisterNamespaceAliases(namespaces, "encoding", EncodingNamespace.Instance, [
                "encode", "decode", "convert", "has", "info", "names", "all"
            ]);

            RegisterNamespaceAliases(namespaces, "regex", RegexNamespace.Instance, [
                "match", "matches", "replace", "test", "split"
            ]);

            RegisterNamespaceAliases(namespaces, "json", JsonNamespace.Instance, [
                "parse", "from", "stringify", "to"
            ]);

            RegisterNamespaceAliases(namespaces, "math", MathNamespace.Instance, [
                "abs", "rand", "floor", "ceil", "round", "min", "max", "sqrt", "pow",
                "log", "log10", "sin", "cos", "tan", "sign", "trunc", "PI", "pi", "E", "e"
            ]);

            RegisterNamespaceAliases(namespaces, "eventlog", EventLogNamespace.Instance, [
                "write", "read", "exists", "create", "remove"
            ]);

        }


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

            intrinsics.Register(T, new IntrinsicDescriptor("encode", 1, 2, (recv, a, instr) => TextToBytesVm(a, instr, "encoding.encode")));
            intrinsics.Register(T, new IntrinsicDescriptor("decode", 1, 2, (recv, a, instr) => BytesToTextVm(a, instr, "encoding.decode")));
            intrinsics.Register(T, new IntrinsicDescriptor("convert", 3, 3, (recv, a, instr) => ConvertEncodingVm(a, instr, "encoding.convert")));
            intrinsics.Register(T, new IntrinsicDescriptor("has", 1, 1, (recv, a, instr) => HasEncodingVm(a, instr, "encoding.has")));
            intrinsics.Register(T, new IntrinsicDescriptor("info", 1, 1, (recv, a, instr) => EncodingInfoVm(a, instr, "encoding.info")));
            intrinsics.Register(T, new IntrinsicDescriptor("names", 0, 0, (recv, a, instr) => EncodingNamesVm()));
            intrinsics.Register(T, new IntrinsicDescriptor("all", 0, 0, (recv, a, instr) => EncodingsVm()));
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
            intrinsics.Register(T, new IntrinsicDescriptor("stringify", 1, 1, (recv, a, instr) => JsonStringifyVm(a)));
            intrinsics.Register(T, new IntrinsicDescriptor("to", 1, 1, (recv, a, instr) => JsonStringifyVm(a)));
        }

        private static object MathAbsVm(List<object> args) => args[0] switch
        {
            BigInteger bi => BigInteger.Abs(bi),
            _ => Math.Abs((dynamic)args[0])
        };

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
            => VM.CompareRuntimeValues(args[0], args[1]) <= 0 ? args[0] : args[1];

        private static object MathMaxVm(List<object> args)
            => VM.CompareRuntimeValues(args[0], args[1]) >= 0 ? args[0] : args[1];

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

        private static void RegisterEventLogNamespace(IIntrinsicRegistry intrinsics)
        {
            Type T = typeof(EventLogNamespace);

            intrinsics.Register(T, new IntrinsicDescriptor("write", 2, 3, (recv, a, instr) => EventLogWriteVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("read", 1, 2, (recv, a, instr) => EventLogReadVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("exists", 1, 1, (recv, a, instr) => EventLogExistsVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("create", 1, 2, (recv, a, instr) => EventLogCreateVm(a, instr)));
            intrinsics.Register(T, new IntrinsicDescriptor("remove", 1, 1, (recv, a, instr) => EventLogRemoveVm(a, instr)));
        }

        private static VMException EventLogError(string message, Instruction instr)
            => new VMException(message, instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);

        private static string RequireEventLogString(List<object> args, int index, string name, Instruction instr)
        {
            string? value = args.Count > index ? args[index]?.ToString() : null;
            if (string.IsNullOrEmpty(value))
                throw EventLogError($"Runtime error: eventlog argument '{name}' must be a non-empty string", instr);
            return value;
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static System.Diagnostics.EventLogEntryType MapEventLogLevel(string level, Instruction instr)
        {
            switch (level.Trim().ToLowerInvariant())
            {
                case "info":
                case "information":
                    return System.Diagnostics.EventLogEntryType.Information;
                case "warn":
                case "warning":
                    return System.Diagnostics.EventLogEntryType.Warning;
                case "error":
                    return System.Diagnostics.EventLogEntryType.Error;
                default:
                    throw EventLogError($"Runtime error: eventlog level '{level}' is invalid (expected 'info', 'warning' or 'error')", instr);
            }
        }

        private static object EventLogWriteVm(List<object> args, Instruction instr)
        {
            if (!OperatingSystem.IsWindows())
                throw EventLogError("Runtime error: eventlog is only available on Windows", instr);
            return EventLogWriteWindows(args, instr);
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static object EventLogWriteWindows(List<object> args, Instruction instr)
        {
            string source = RequireEventLogString(args, 0, "source", instr);
            string message = RequireEventLogString(args, 1, "message", instr);
            string level = args.Count >= 3 ? (args[2]?.ToString() ?? "info") : "info";
            System.Diagnostics.EventLogEntryType type = MapEventLogLevel(level, instr);

            try
            {
                if (!System.Diagnostics.EventLog.SourceExists(source))
                    System.Diagnostics.EventLog.CreateEventSource(source, "Application");

                using System.Diagnostics.EventLog log = new System.Diagnostics.EventLog("Application") { Source = source };
                log.WriteEntry(message, type);
                return 1;
            }
            catch (System.Security.SecurityException ex)
            {
                throw EventLogError($"Runtime error: eventlog.write requires administrator rights to register the source '{source}' ({ex.Message})", instr);
            }
            catch (Exception ex) when (ex is not VMException)
            {
                throw EventLogError($"Runtime error: eventlog.write failed ({ex.GetType().Name}: {ex.Message})", instr);
            }
        }

        private static object EventLogReadVm(List<object> args, Instruction instr)
        {
            if (!OperatingSystem.IsWindows())
                throw EventLogError("Runtime error: eventlog is only available on Windows", instr);
            return EventLogReadWindows(args, instr);
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static object EventLogReadWindows(List<object> args, Instruction instr)
        {
            string logName = RequireEventLogString(args, 0, "logName", instr);
            int count = 50;
            if (args.Count >= 2 && args[1] is not null)
            {
                try { count = Convert.ToInt32(args[1], CultureInfo.InvariantCulture); }
                catch (Exception ex) { throw EventLogError($"Runtime error: eventlog.read count is not a valid integer ({ex.Message})", instr); }
            }

            if (count <= 0)
                return new List<object>();

            try
            {
                using System.Diagnostics.EventLog log = new System.Diagnostics.EventLog(logName);
                System.Diagnostics.EventLogEntryCollection entries = log.Entries;
                int total = entries.Count;
                int take = Math.Min(count, total);

                List<object> result = new(take);
                // Newest first: walk from the end of the collection backwards.
                for (int i = total - 1; i >= total - take; i--)
                {
                    System.Diagnostics.EventLogEntry entry = entries[i];
                    result.Add(new Dictionary<string, object>
                    {
                        ["source"] = entry.Source ?? string.Empty,
                        ["message"] = entry.Message ?? string.Empty,
                        ["level"] = entry.EntryType.ToString(),
                        ["time"] = entry.TimeGenerated.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        ["id"] = entry.InstanceId
                    });
                }

                return result;
            }
            catch (System.Security.SecurityException ex)
            {
                throw EventLogError($"Runtime error: eventlog.read requires sufficient rights to read log '{logName}' ({ex.Message})", instr);
            }
            catch (Exception ex) when (ex is not VMException)
            {
                throw EventLogError($"Runtime error: eventlog.read failed ({ex.GetType().Name}: {ex.Message})", instr);
            }
        }

        private static object EventLogExistsVm(List<object> args, Instruction instr)
        {
            if (!OperatingSystem.IsWindows())
                throw EventLogError("Runtime error: eventlog is only available on Windows", instr);
            return EventLogExistsWindows(args, instr);
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static object EventLogExistsWindows(List<object> args, Instruction instr)
        {
            string source = RequireEventLogString(args, 0, "source", instr);
            try
            {
                return System.Diagnostics.EventLog.SourceExists(source);
            }
            catch (Exception ex) when (ex is not VMException)
            {
                throw EventLogError($"Runtime error: eventlog.exists failed ({ex.GetType().Name}: {ex.Message})", instr);
            }
        }

        private static object EventLogCreateVm(List<object> args, Instruction instr)
        {
            if (!OperatingSystem.IsWindows())
                throw EventLogError("Runtime error: eventlog is only available on Windows", instr);
            return EventLogCreateWindows(args, instr);
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static object EventLogCreateWindows(List<object> args, Instruction instr)
        {
            string source = RequireEventLogString(args, 0, "source", instr);
            string logName = args.Count >= 2 && args[1] is not null
                ? RequireEventLogString(args, 1, "logName", instr)
                : "Application";

            try
            {
                if (!System.Diagnostics.EventLog.SourceExists(source))
                    System.Diagnostics.EventLog.CreateEventSource(source, logName);
                return 1;
            }
            catch (System.Security.SecurityException ex)
            {
                throw EventLogError($"Runtime error: eventlog.create requires administrator rights to register the source '{source}' ({ex.Message})", instr);
            }
            catch (Exception ex) when (ex is not VMException)
            {
                throw EventLogError($"Runtime error: eventlog.create failed ({ex.GetType().Name}: {ex.Message})", instr);
            }
        }

        private static object EventLogRemoveVm(List<object> args, Instruction instr)
        {
            if (!OperatingSystem.IsWindows())
                throw EventLogError("Runtime error: eventlog is only available on Windows", instr);
            return EventLogRemoveWindows(args, instr);
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static object EventLogRemoveWindows(List<object> args, Instruction instr)
        {
            string source = RequireEventLogString(args, 0, "source", instr);
            try
            {
                if (System.Diagnostics.EventLog.SourceExists(source))
                    System.Diagnostics.EventLog.DeleteEventSource(source);
                return 1;
            }
            catch (System.Security.SecurityException ex)
            {
                throw EventLogError($"Runtime error: eventlog.remove requires administrator rights to delete the source '{source}' ({ex.Message})", instr);
            }
            catch (Exception ex) when (ex is not VMException)
            {
                throw EventLogError($"Runtime error: eventlog.remove failed ({ex.GetType().Name}: {ex.Message})", instr);
            }
        }

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
                if (val is StringBuilderHandle) return "StringBuilder";
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
                if (a0 is StringBuilderHandle sbh) return sbh.Length;
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
                return VmStringify(args[0]);
            }));

            builtins.Register(new BuiltinDescriptor("strbuilder", 0, 1, (args, instr) =>
            {
                return args.Count == 0 ? new StringBuilderHandle() : new StringBuilderHandle(VmStringify(args[0]));
            }));
            builtins.Register(new BuiltinDescriptor("format", 1, int.MaxValue, (args, instr) =>
            {
                if (args.Count == 0 || args[0] is not string format)
                    return null!;
                if (args.Count == 1)
                    return format;
                var fmtArgs = new object[args.Count - 1];
                for (int i = 1; i < args.Count; i++)
                    fmtArgs[i - 1] = NormalizeFormatArg(args[i]);

                return string.Format(CultureInfo.InvariantCulture, format, fmtArgs);
            }));



            builtins.Register(new BuiltinDescriptor("string", 1, 1, (args, instr) =>
            {
                return VmStringify(args[0]);
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

        }

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
            intrinsics.Register(T, new IntrinsicDescriptor("contains", 1, 1, (recv, a, i) =>
            {
                string s = recv?.ToString() ?? "";
                return s.Contains(VmStringArg(a[0]), StringComparison.Ordinal);
            }));
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
                string repl = VmStringArg(a[2]);
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
                return s.Replace(VmStringArg(a[0]), VmStringArg(a[1]), StringComparison.Ordinal);
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("insert_at", 2, 2, (recv, a, instr) =>
            {
                string s = recv?.ToString() ?? "";
                int len = s.Length;
                int idx = ClampIndex(Convert.ToInt32(a[0]), len);
                string repl = VmStringArg(a[1]);
                return s.Substring(0, idx) + repl + s.Substring(idx);
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("split", 1, 1, (recv, a, i) =>
            {
                string s = recv?.ToString() ?? "";
                string sep = VmStringArg(a[0]);
                if (sep.Length == 0)
                    return s.Select(c => (object)c.ToString()).ToList();
                return s.Split(sep).Select(p => (object)p).ToList();
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("startsWith", 1, 1, (recv, a, i) =>
            {
                string s = recv?.ToString() ?? "";
                string prefix = VmStringArg(a[0]);
                return s.StartsWith(prefix, StringComparison.Ordinal);
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("endsWith", 1, 1, (recv, a, i) =>
            {
                string s = recv?.ToString() ?? "";
                string suffix = VmStringArg(a[0]);
                return s.EndsWith(suffix, StringComparison.Ordinal);
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("indexOf", 1, 1, (recv, a, i) =>
            {
                string s = recv?.ToString() ?? "";
                string needle = VmStringArg(a[0]);
                return s.IndexOf(needle, StringComparison.Ordinal);
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("lastIndexOf", 1, 1, (recv, a, i) =>
            {
                string s = recv?.ToString() ?? "";
                string needle = VmStringArg(a[0]);
                return s.LastIndexOf(needle, StringComparison.Ordinal);
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
                char padChar = VmStringArg(a[1]).FirstOrDefault(' ');
                return s.PadLeft(totalWidth, padChar);
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("padEnd", 2, 2, (recv, a, i) =>
            {
                string s = recv?.ToString() ?? "";
                int totalWidth = Convert.ToInt32(a[0]);
                char padChar = VmStringArg(a[1]).FirstOrDefault(' ');
                return s.PadRight(totalWidth, padChar);
            }));
        }

        private static void RegisterStringBuilder(IIntrinsicRegistry intrinsics)
        {
            Type T = typeof(StringBuilderHandle);

            // Appends the stringified value (same rendering as str()) and returns the builder for chaining.
            intrinsics.Register(T, new IntrinsicDescriptor("append", 1, 1, (recv, a, i) =>
                ((StringBuilderHandle)recv!).Append(VmStringify(a[0]))));

            intrinsics.Register(T, new IntrinsicDescriptor("str", 0, 0, (recv, a, i) => ((StringBuilderHandle)recv!).Build()));
            intrinsics.Register(T, new IntrinsicDescriptor("toString", 0, 0, (recv, a, i) => ((StringBuilderHandle)recv!).Build()));
            intrinsics.Register(T, new IntrinsicDescriptor("len", 0, 0, (recv, a, i) => ((StringBuilderHandle)recv!).Length));
            intrinsics.Register(T, new IntrinsicDescriptor("clear", 0, 0, (recv, a, i) => ((StringBuilderHandle)recv!).Clear()));
        }

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
                // Numeric-aware total order: compares by numeric value across int/long/BigInteger/
                // double, so a naturally-mixed array (CFGS auto-promotes) sorts instead of throwing
                // from int.CompareTo(long).
                LockMutableRuntime(arr, () => arr.Sort((x, y) => VM.CompareRuntimeValues(x, y)));
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
                    if (VM.RuntimeValuesEqual(arr[idx], target)) return idx;
                }
                return -1;
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("includes", 1, 1, (recv, a, i) =>
            {
                List<object> arr = SnapshotMutableList((List<object>)recv);
                object target = a[0];
                foreach (object item in arr)
                {
                    if (VM.RuntimeValuesEqual(item, target)) return true;
                }
                return false;
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("join", 1, 1, (recv, a, i) =>
            {
                List<object> arr = SnapshotMutableList((List<object>)recv);
                string sep = VmStringify(a[0]);
                return string.Join(sep, arr.Select(VmStringify));
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
                object callback = a[0];
                VM vm = VM.CurrentVm!;
                List<object> result = new(arr.Count);
                bool passIndex = GetCallableExposedParameterCount(callback, instr) >= 2;
                for (int idx = 0; idx < arr.Count; idx++)
                {
                    List<object> cbArgs = passIndex
                        ? new List<object> { arr[idx], idx }
                        : new List<object> { arr[idx] };
                    result.Add(InvokeCallbackSync(vm, callback, cbArgs, instr)!);
                }
                return result;
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("filter", 1, 1, (recv, a, instr) =>
            {
                List<object> arr = SnapshotMutableList((List<object>)recv);
                object callback = a[0];
                VM vm = VM.CurrentVm!;
                List<object> result = new();
                bool passIndex = GetCallableExposedParameterCount(callback, instr) >= 2;
                for (int idx = 0; idx < arr.Count; idx++)
                {
                    List<object> cbArgs = passIndex
                        ? new List<object> { arr[idx], idx }
                        : new List<object> { arr[idx] };
                    object? val = InvokeCallbackSync(vm, callback, cbArgs, instr);
                    if (val is true) result.Add(arr[idx]);
                }
                return result;
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("reduce", 2, 2, (recv, a, instr) =>
            {
                List<object> arr = SnapshotMutableList((List<object>)recv);
                object callback = a[0];
                object accumulator = a[1];
                VM vm = VM.CurrentVm!;
                foreach (object item in arr)
                {
                    accumulator = InvokeCallbackSync(vm, callback, new List<object> { accumulator, item }, instr)!;
                }
                return accumulator;
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("find", 1, 1, (recv, a, instr) =>
            {
                List<object> arr = SnapshotMutableList((List<object>)recv);
                object callback = a[0];
                VM vm = VM.CurrentVm!;
                bool passIndex = GetCallableExposedParameterCount(callback, instr) >= 2;
                for (int idx = 0; idx < arr.Count; idx++)
                {
                    List<object> cbArgs = passIndex
                        ? new List<object> { arr[idx], idx }
                        : new List<object> { arr[idx] };
                    object? val = InvokeCallbackSync(vm, callback, cbArgs, instr);
                    if (val is true) return arr[idx];
                }
                return null!;
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("findIndex", 1, 1, (recv, a, instr) =>
            {
                List<object> arr = SnapshotMutableList((List<object>)recv);
                object callback = a[0];
                VM vm = VM.CurrentVm!;
                bool passIndex = GetCallableExposedParameterCount(callback, instr) >= 2;
                for (int idx = 0; idx < arr.Count; idx++)
                {
                    List<object> cbArgs = passIndex
                        ? new List<object> { arr[idx], idx }
                        : new List<object> { arr[idx] };
                    object? val = InvokeCallbackSync(vm, callback, cbArgs, instr);
                    if (val is true) return idx;
                }
                return -1;
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("every", 1, 1, (recv, a, instr) =>
            {
                List<object> arr = SnapshotMutableList((List<object>)recv);
                object callback = a[0];
                VM vm = VM.CurrentVm!;
                foreach (object item in arr)
                {
                    object? val = InvokeCallbackSync(vm, callback, new List<object> { item }, instr);
                    if (val is not true) return false;
                }
                return true;
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("some", 1, 1, (recv, a, instr) =>
            {
                List<object> arr = SnapshotMutableList((List<object>)recv);
                object callback = a[0];
                VM vm = VM.CurrentVm!;
                foreach (object item in arr)
                {
                    object? val = InvokeCallbackSync(vm, callback, new List<object> { item }, instr);
                    if (val is true) return true;
                }
                return false;
            }));
        }

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
                if (LockMutableRuntime(inst, () => inst.Fields.TryGetValue("__type", out Value innerType) ? innerType.ToObject() : null) is StaticInstance st
                    && LockMutableRuntime(st, () => st.Fields.TryGetValue("__vis_inst", out Value innerVis) ? innerVis.ToObject() : null) is Dictionary<string, object> vm)
                {
                    visMap = vm;
                }

                var result = new Dictionary<string, object>();
                foreach (var kvp in LockMutableRuntime(inst, () => inst.EnumerateFields()
                    .Select(static kv => new KeyValuePair<string, object?>(kv.Key, kv.Value.ToObject()))
                    .ToList()))
                {
                    object? fieldValue = kvp.Value;
                    if (kvp.Key.StartsWith("__")) continue;
                    if (fieldValue is Closure) continue;
                    if (fieldValue is StaticInstance) continue;

                    if (visMap != null && LockMutableRuntime(visMap, () => visMap.TryGetValue(kvp.Key, out object? rawVis) ? rawVis : null) is object rawVis)
                    {
                        int vis = rawVis is int i ? i : 0;
                        if (vis != 0) continue;
                    }

                    result[kvp.Key] = NormalizeForJson(fieldValue)!;
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
