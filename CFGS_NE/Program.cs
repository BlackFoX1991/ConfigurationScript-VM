using CFGS_VM.Analytic.Core;
using CFGS_VM.Analytic;
using CFGS_VM.Analytic.Ex;
using CFGS_VM.Analytic.Tree;
using CFGS_VM.Analytic.Lowering;
using CFGS_VM.Analytic.Modules;
using CFGS_VM.VMCore;
using CFGS_VM.VMCore.Extensions;
using CFGS_VM.VMCore.Extensions.Core;
using CFGS_VM.Packaging;
using CFGS_VM.Tooling;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;

internal static class AnsiConsole
{
    internal const int STD_OUTPUT_HANDLE = -11;

    internal const int ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetConsoleMode(IntPtr hConsoleHandle, out int lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool SetConsoleMode(IntPtr hConsoleHandle, int dwMode);

    public static void EnableAnsi()
    {
        nint hOut = GetStdHandle(STD_OUTPUT_HANDLE);
        if (hOut == IntPtr.Zero) return;
        if (!GetConsoleMode(hOut, out int mode)) return;
        SetConsoleMode(hOut, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
    }

    public static void DisableAnsi()
    {
        nint hOut = GetStdHandle(STD_OUTPUT_HANDLE);
        if (hOut == IntPtr.Zero) return;
        if (!GetConsoleMode(hOut, out int mode)) return;
        mode &= ~ENABLE_VIRTUAL_TERMINAL_PROCESSING;
        SetConsoleMode(hOut, mode);
    }
}

public class Program
{
    /// <summary>
    /// Gets a value indicating whether IsDebug
    /// </summary>
    public static bool IsDebug { get; private set; } = false;

    /// <summary>
    /// Gets a value indicating whether the interactive VM debugger is enabled.
    /// </summary>
    public static bool IsInteractiveDebug { get; private set; } = false;

    public const string Version = "v9.1.1";

    public static bool AnsiMode = true;

    public static string CLIPath = string.Empty;

    private static readonly string header_text =
        $"[ Configuration-Language ] [ Version {Version} ]\n Enter your code, '$help' or '$exit/$quit'\n Visit : https://github.com/BlackFoX1991/ConfigurationScript-VM";

    private static readonly string help_diag = @"
================================================================================
[=============================== HELP =========================================]
[ Enter your code or use $ commands                                            ]
[ $buffer:length to set logfile max. output                                    ]
[ $cls / $clear to clear the console                                           ]
[ $debug to enable/disable the debug mode                                      ]
[ $ansi to enable or disable Ansi-Console-Mode                                 ]
================================================================================
[ Commands : -d(ebug) -di(debugger) -p(arams) -c(ompile)                       ]
[            -s(et) buffer  maxlen                                             ]
[                   ansi    1 or 0 to en/dis-able                              ]
[ Compile  : -c file.cfs [-o file.cfb]                                         ]
[ Check    : -check file.cfs [--json]                                          ]
[ Lint     : -lint file-or-folder [--json] [--warnings-as-errors]              ]
[ Format   : -format file-or-folder [--write|--check] [--json]                 ]
[ Packages : init | install plugin.dll | restore                               ]
[------------------------------ REPL INFO -------------------------------------]
[ $help shows help, $exit/$quit leaves REPL                                    ]
[ Enter runs complete code, Ctrl+Enter forces run                              ]
[ Use Ctrl+Backspace to clear the code                                         ]
[ Enter $L <Line> <Content> to edit a specific Line                            ]
[ Use Arrow Up/Down to trigger $L command                                      ]
[------------------------------------------------------------------------------]
================================================================================";

    public static async Task Main(string[] args)
    {
        if (TryGetLspArguments(args, out string[] lspArgs))
        {
            Environment.ExitCode = await RunLspModeAsync(lspArgs);
            return;
        }

        if (OperatingSystem.IsWindows())
            AnsiConsole.EnableAnsi();

        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        PackageCliCommand packageCommand;
        try
        {
            if (TryGetPackageArguments(args, out packageCommand))
            {
                Environment.ExitCode = RunPackageCommand(packageCommand);
                return;
            }
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"[CFGS] invalid package command: {ex.Message}");
            Environment.ExitCode = 1;
            return;
        }

        try
        {
            if (TryGetLintArguments(args, out LintCliCommand lintCommand))
            {
                Environment.ExitCode = RunLintCommand(lintCommand);
                return;
            }

            if (TryGetFormatArguments(args, out FormatCliCommand formatCommand))
            {
                Environment.ExitCode = RunFormatCommand(formatCommand);
                return;
            }
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"[CFGS] invalid tooling command: {ex.Message}");
            Environment.ExitCode = 1;
            return;
        }

        if (TryGetCheckArguments(args, out string? checkInput, out bool checkJson))
        {
            try
            {
                if (string.IsNullOrWhiteSpace(checkInput))
                {
                    Console.Error.WriteLine("missing input script for -check/--check");
                    Environment.ExitCode = 1;
                    return;
                }

                CheckFile(checkInput, checkJson);
            }
            catch (Exception ex)
            {
                if (checkJson)
                    PrintDiagnosticJson(ex, checkInput ?? "<check>");
                else
                    PrintTrackedException(ex, "check", checkInput ?? "<check>");

                Environment.ExitCode = 1;
            }

            return;
        }

        if (TryGetCompileArguments(args, out string? compileInput, out string? compileOutput, out bool compileDebug))
        {
            IsDebug = compileDebug;
            try
            {
                if (string.IsNullOrWhiteSpace(compileInput))
                {
                    Console.Error.WriteLine("missing input script for -c/-compile");
                    Environment.ExitCode = 1;
                    return;
                }

                CompileFileToCfb(compileInput, compileOutput);
            }
            catch (Exception ex)
            {
                PrintTrackedException(ex, "compile", compileInput ?? "<compile>");
                Environment.ExitCode = 1;
            }

            return;
        }

        List<string> files = new();
        string? currentSourceForError = null;

        bool setMode = false;
        string setCommand = string.Empty;
        bool ignoreMode = false;

        foreach (string arg in args)
        {
            if (!ignoreMode)
            {
                if (setMode)
                {
                    if (setCommand == string.Empty) setCommand = arg;
                    else
                    {
                        switch (setCommand)
                        {
                            case "buffer":
                                if (int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int buf))
                                {
                                    VM.DEBUG_BUFFER = buf;
                                    Console.WriteLine($"[SETTINGS] Debug-Buffer set to {VM.DEBUG_BUFFER}");
                                }
                                else
                                {
                                    Console.Error.WriteLine($"invalid value for -s(et) buffer '{arg}'");
                                }
                                setCommand = string.Empty;
                                setMode = false;
                                break;

                            case "ansi":
                                if (int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int aMode) &&
                                    (aMode == 0 || aMode == 1))
                                {
                                    AnsiMode = Convert.ToBoolean(aMode);
                                    Console.WriteLine("Ansi-Mode " + (AnsiMode ? "enabled" : "disabled"));
                                }
                                else
                                {
                                    Console.Error.WriteLine($"invalid value for -s(et) ansi '{arg}' (expected 0 or 1)");
                                }
                                setCommand = string.Empty;
                                setMode = false;
                                break;

                            default:
                                Console.Error.WriteLine($"invalid command for -s(et) '{setCommand}'");
                                setCommand = string.Empty;
                                setMode = false;
                                break;
                        }
                    }
                    continue;
                }

                if (arg.Trim().StartsWith("-"))
                {
                    switch (arg.Trim())
                    {
                        case "-d":
                        case "-debug":
                            IsDebug = true;
                            break;

                        case "-di":
                        case "-debugger":
                        case "--debugger":
                            IsInteractiveDebug = true;
                            break;

                        case "-p":
                        case "-params":
                            ignoreMode = true;
                            break;

                        case "-s":
                        case "-set":
                            setMode = true;
                            break;

                        default:
                            Console.Error.WriteLine($"Invalid command {arg.Trim()}.");
                            break;
                    }
                }
                else
                {
                    if (File.Exists(arg))
                        files.Add(arg);
                    else
                        Console.WriteLine($"Could not load the script-file : '{arg}'");
                }
            }
        }

        try
        {
            if (files.Count > 0)
            {
                foreach (string file in files)
                {
                    currentSourceForError = file;

                    string workingDirectory =
                        FrontendPipeline.TryGetWorkingDirectory(file)
                        ?? Path.GetDirectoryName(Environment.ProcessPath)
                        ?? AppContext.BaseDirectory;

                    if (file.EndsWith(".cfb", StringComparison.OrdinalIgnoreCase))
                    {
                        await RunCompiledFileAsync(file, IsDebug, IsInteractiveDebug, workingDirectory: workingDirectory);
                    }
                    else
                    {
                        string input = File.ReadAllText(file);
                        await RunSourceAsync(input, file, IsDebug, IsInteractiveDebug, workingDirectory: workingDirectory);
                    }

                    currentSourceForError = null;
                }
            }
            else
            {
                Console.WriteLine(header_text);

                VM replVm = new();
                LoadReplPluginsFromApplicationPath(replVm);

                while (true)
                {
                    string? code = ReadMultilineInput();
                    if (code == null) break;
                    if (string.IsNullOrWhiteSpace(code)) continue;

                    try
                    {
                        await RunSourceAsync(code, "<repl>", IsDebug, IsInteractiveDebug, sharedVm: replVm);
                    }
                    catch (Exception ex)
                    {
                        PrintTrackedException(ex, "repl", "<repl>");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            string phase = currentSourceForError is null && files.Count == 0 ? "startup" : "script-run";
            PrintTrackedException(ex, phase, currentSourceForError);

            // A failed startup, compile or script run must surface a non-zero exit code so that
            // shells and CI pipelines can detect the failure. (The REPL loop catches its own
            // per-command errors above and intentionally keeps running.)
            Environment.ExitCode = 1;
        }
    }

    private static bool TryGetCompileArguments(string[] args, out string? input, out string? output, out bool debug)
    {
        input = null;
        output = null;
        debug = false;
        bool compileMode = false;
        bool outputMode = false;

        foreach (string rawArg in args)
        {
            string arg = rawArg.Trim();
            if (outputMode)
            {
                output = rawArg;
                outputMode = false;
                continue;
            }

            switch (arg)
            {
                case "-c":
                case "-compile":
                    compileMode = true;
                    continue;

                case "-o":
                case "-out":
                    outputMode = true;
                    continue;

                case "-d":
                case "-debug":
                    debug = true;
                    continue;
            }

            if (compileMode && !arg.StartsWith("-", StringComparison.Ordinal) && input is null)
                input = rawArg;
        }

        return compileMode;
    }

    private static bool TryGetCheckArguments(string[] args, out string? input, out bool json)
    {
        input = null;
        json = false;
        bool checkMode = false;

        foreach (string rawArg in args)
        {
            string arg = rawArg.Trim();
            switch (arg)
            {
                case "-check":
                case "--check":
                case "check":
                    checkMode = true;
                    continue;

                case "--json":
                case "-json":
                    json = true;
                    continue;
            }

            if (checkMode && !arg.StartsWith("-", StringComparison.Ordinal) && input is null)
                input = rawArg;
        }

        return checkMode;
    }

    private sealed class LintCliCommand
    {
        public string? Input { get; set; }
        public bool Json { get; set; }
        public bool WarningsAsErrors { get; set; }
        public bool Fix { get; set; }
        public bool AllowTabs { get; set; }
        public int MaxLineLength { get; set; } = 140;
        public int IndentSize { get; set; } = 4;
        public bool WarningsAsErrorsSet { get; set; }
        public bool AllowTabsSet { get; set; }
        public bool MaxLineLengthSet { get; set; }
        public bool IndentSizeSet { get; set; }
    }

    private static bool TryGetLintArguments(string[] args, out LintCliCommand command)
    {
        command = new LintCliCommand();
        if (args.Length == 0)
            return false;

        string first = args[0].Trim();
        if (!string.Equals(first, "-lint", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(first, "--lint", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(first, "lint", StringComparison.OrdinalIgnoreCase))
            return false;

        for (int i = 1; i < args.Length; i++)
        {
            string arg = args[i].Trim();
            switch (arg)
            {
                case "--json":
                case "-json":
                    command.Json = true;
                    break;

                case "--warnings-as-errors":
                    command.WarningsAsErrors = true;
                    command.WarningsAsErrorsSet = true;
                    break;

                case "--fix":
                    command.Fix = true;
                    break;

                case "--allow-tabs":
                    command.AllowTabs = true;
                    command.AllowTabsSet = true;
                    break;

                case "--max-line-length":
                    string max = ReadOptionValue(args, ref i, arg);
                    if (!int.TryParse(max, NumberStyles.Integer, CultureInfo.InvariantCulture, out int maxLineLength) || maxLineLength <= 0)
                        throw new ArgumentException($"invalid value for {arg}: {max}");
                    command.MaxLineLength = maxLineLength;
                    command.MaxLineLengthSet = true;
                    break;

                case "--indent":
                case "--indent-size":
                    string indent = ReadOptionValue(args, ref i, arg);
                    if (!int.TryParse(indent, NumberStyles.Integer, CultureInfo.InvariantCulture, out int indentSize) || indentSize <= 0)
                        throw new ArgumentException($"invalid value for {arg}: {indent}");
                    command.IndentSize = indentSize;
                    command.IndentSizeSet = true;
                    break;

                default:
                    if (command.Input is null && !arg.StartsWith("-", StringComparison.Ordinal))
                        command.Input = args[i];
                    break;
            }
        }

        return true;
    }

    private sealed class FormatCliCommand
    {
        public string? Input { get; set; }
        public bool Json { get; set; }
        public bool Write { get; set; }
        public bool Check { get; set; }
        public bool UseTabs { get; set; }
        public int IndentSize { get; set; } = 4;
        public bool UseTabsSet { get; set; }
        public bool IndentSizeSet { get; set; }
    }

    private static bool TryGetFormatArguments(string[] args, out FormatCliCommand command)
    {
        command = new FormatCliCommand();
        if (args.Length == 0)
            return false;

        string first = args[0].Trim();
        if (!string.Equals(first, "-format", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(first, "--format", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(first, "format", StringComparison.OrdinalIgnoreCase))
            return false;

        for (int i = 1; i < args.Length; i++)
        {
            string arg = args[i].Trim();
            switch (arg)
            {
                case "--json":
                case "-json":
                    command.Json = true;
                    break;

                case "--write":
                case "-w":
                    command.Write = true;
                    break;

                case "--check":
                    command.Check = true;
                    break;

                case "--tabs":
                    command.UseTabs = true;
                    command.UseTabsSet = true;
                    break;

                case "--spaces":
                    command.UseTabs = false;
                    command.UseTabsSet = true;
                    break;

                case "--indent":
                case "--indent-size":
                    string indent = ReadOptionValue(args, ref i, arg);
                    if (!int.TryParse(indent, NumberStyles.Integer, CultureInfo.InvariantCulture, out int indentSize) || indentSize <= 0)
                        throw new ArgumentException($"invalid value for {arg}: {indent}");
                    command.IndentSize = indentSize;
                    command.IndentSizeSet = true;
                    break;

                default:
                    if (command.Input is null && !arg.StartsWith("-", StringComparison.Ordinal))
                        command.Input = args[i];
                    break;
            }
        }

        return true;
    }

    private sealed class PackageCliCommand
    {
        public string Command { get; init; } = string.Empty;
        public string? PluginPath { get; set; }
        public string? ProjectDirectory { get; set; }
        public string? Name { get; set; }
        public string? Version { get; set; }
        public bool Restore { get; set; } = true;
        public bool IncludeDependencies { get; set; } = true;
        public bool List { get; set; }
    }

    private static bool TryGetPackageArguments(string[] args, out PackageCliCommand command)
    {
        command = new PackageCliCommand();
        if (args.Length == 0)
            return false;

        string first = args[0].Trim();
        if (!IsPackageCommand(first))
            return false;

        command = new PackageCliCommand { Command = first };
        for (int i = 1; i < args.Length; i++)
        {
            string arg = args[i].Trim();
            switch (arg)
            {
                case "--project":
                case "-project":
                    command.ProjectDirectory = ReadOptionValue(args, ref i, arg);
                    break;

                case "--name":
                case "-name":
                    command.Name = ReadOptionValue(args, ref i, arg);
                    break;

                case "--version":
                case "-version":
                    command.Version = ReadOptionValue(args, ref i, arg);
                    break;

                case "--no-restore":
                    command.Restore = false;
                    break;

                case "--no-dependencies":
                    command.IncludeDependencies = false;
                    break;

                case "list":
                    command.List = true;
                    break;

                default:
                    if (command.PluginPath is null && !arg.StartsWith("-", StringComparison.Ordinal))
                        command.PluginPath = args[i];
                    break;
            }
        }

        return true;
    }

    private static bool IsPackageCommand(string command)
        => string.Equals(command, "init", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(command, "restore", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(command, "install", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(command, "deps", StringComparison.OrdinalIgnoreCase);

    private static string ReadOptionValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException($"missing value for {option}");

        index++;
        return args[index];
    }

    private static int RunPackageCommand(PackageCliCommand command)
    {
        try
        {
            switch (command.Command.ToLowerInvariant())
            {
                case "init":
                    CfgsManifest manifest = CfgsPackageManager.Init(command.ProjectDirectory);
                    string initRoot = CfgsPackageManager.ResolveProjectRoot(command.ProjectDirectory);
                    Console.WriteLine($"[CFGS] initialized {CfgsPackageManager.ManifestFileName}: {Path.Combine(initRoot, CfgsPackageManager.ManifestFileName)}");
                    Console.WriteLine($"[CFGS] project: {manifest.Name ?? new DirectoryInfo(initRoot).Name}");
                    return 0;

                case "install":
                    if (string.IsNullOrWhiteSpace(command.PluginPath))
                    {
                        Console.Error.WriteLine("missing plugin path for install");
                        return 1;
                    }

                    CfgsPluginDependency installed = CfgsPackageManager.InstallPlugin(
                        command.PluginPath,
                        command.ProjectDirectory,
                        command.Name,
                        command.Version,
                        command.IncludeDependencies,
                        command.Restore);

                    Console.WriteLine($"[CFGS] installed plugin dependency: {installed.Name ?? Path.GetFileNameWithoutExtension(installed.Path)}");
                    if (command.Restore)
                        Console.WriteLine($"[CFGS] restored packages and updated {CfgsPackageManager.LockFileName}");
                    return 0;

                case "restore":
                    CfgsLockFile restored = CfgsPackageManager.Restore(command.ProjectDirectory);
                    Console.WriteLine($"[CFGS] restored {restored.Plugins.Count} plugin dependency/dependencies");
                    Console.WriteLine($"[CFGS] lockfile: {CfgsPackageManager.LockFileName}");
                    return 0;

                case "deps":
                    return RunDepsCommand(command);

                default:
                    Console.Error.WriteLine($"unknown package command '{command.Command}'");
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CFGS] {command.Command} failed: {ex.Message}");
            return 1;
        }
    }

    private static int RunDepsCommand(PackageCliCommand command)
    {
        string root = CfgsPackageManager.ResolveProjectRoot(command.ProjectDirectory);
        CfgsManifest manifest = CfgsPackageManager.ReadManifest(root);
        CfgsLockFile? lockFile = CfgsPackageManager.TryReadLockFile(root);

        Console.WriteLine($"[CFGS] project: {manifest.Name ?? new DirectoryInfo(root).Name}");
        if (manifest.Plugins is null || manifest.Plugins.Count == 0)
        {
            Console.WriteLine("[CFGS] plugins: 0");
            return 0;
        }

        Console.WriteLine($"[CFGS] plugins: {manifest.Plugins.Count}");
        foreach (CfgsPluginDependency plugin in manifest.Plugins)
        {
            CfgsLockedPlugin? locked = lockFile?.Plugins.FirstOrDefault(p =>
                string.Equals(p.Name, plugin.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileName(p.Source), Path.GetFileName(plugin.Path), StringComparison.OrdinalIgnoreCase));

            string version = string.IsNullOrWhiteSpace(plugin.Version) ? "0.0.0" : plugin.Version;
            string lockInfo = locked is null ? "not restored" : $"locked {locked.Sha256[..Math.Min(12, locked.Sha256.Length)]}";
            Console.WriteLine($"  - {plugin.Name ?? Path.GetFileNameWithoutExtension(plugin.Path)} {version} ({lockInfo})");
        }

        return 0;
    }

    private static int RunLintCommand(LintCliCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Input))
        {
            Console.Error.WriteLine("missing input file or directory for lint");
            return 1;
        }

        ApplyToolingConfig(command.Input, command);

        IReadOnlyList<string> files = CollectCfgsFiles(command.Input);
        if (files.Count == 0)
        {
            Console.Error.WriteLine($"no .cfs files found for lint input '{command.Input}'");
            return 1;
        }

        CfgsLintOptions options = new(command.MaxLineLength, command.WarningsAsErrors, command.Fix, command.AllowTabs, command.IndentSize);
        List<CfgsToolDiagnostic> diagnostics = [];
        foreach (string file in files)
        {
            CfgsLintResult result = CfgsLinter.LintFile(file, options, Version);
            if (command.Fix && result.Fixed && result.FixedText is not null)
            {
                File.WriteAllText(file, result.FixedText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                if (!command.Json)
                    Console.WriteLine($"[LINT] fixed: {file}");

                CfgsLintOptions postFixOptions = options with { Fix = false };
                result = CfgsLinter.LintFile(file, postFixOptions, Version);
            }

            diagnostics.AddRange(result.Diagnostics);
        }

        if (command.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(diagnostics.Select(ToDiagnosticJsonPayload), new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            foreach (CfgsToolDiagnostic diagnostic in diagnostics.OrderBy(static d => d.SourceFile, StringComparer.OrdinalIgnoreCase).ThenBy(static d => d.Line).ThenBy(static d => d.Column))
                Console.WriteLine(FormatToolDiagnostic(diagnostic));

            int errors = diagnostics.Count(static d => d.Severity == CfgsToolDiagnosticSeverity.Error);
            int warnings = diagnostics.Count(static d => d.Severity == CfgsToolDiagnosticSeverity.Warning);
            int infos = diagnostics.Count(static d => d.Severity == CfgsToolDiagnosticSeverity.Info);
            Console.WriteLine($"[LINT] files={files.Count}, errors={errors}, warnings={warnings}, info={infos}");
        }

        return diagnostics.Any(static d => d.Severity is CfgsToolDiagnosticSeverity.Error or CfgsToolDiagnosticSeverity.Warning)
            ? 1
            : 0;
    }

    private static int RunFormatCommand(FormatCliCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Input))
        {
            Console.Error.WriteLine("missing input file or directory for format");
            return 1;
        }

        ApplyToolingConfig(command.Input, command);

        IReadOnlyList<string> files = CollectCfgsFiles(command.Input);
        if (files.Count == 0)
        {
            Console.Error.WriteLine($"no .cfs files found for format input '{command.Input}'");
            return 1;
        }

        if (!command.Write && !command.Check && files.Count > 1)
        {
            Console.Error.WriteLine("formatting multiple files requires --write or --check");
            return 1;
        }

        CfgsFormatOptions options = new(command.IndentSize, command.UseTabs);
        List<object> jsonResults = [];
        bool anyChanged = false;

        foreach (string file in files)
        {
            try
            {
                ValidateCfgsFile(file);
                string source = File.ReadAllText(file);
                CfgsFormatResult result = CfgsFormatter.Format(source, options);
                anyChanged |= result.Changed;

                if (command.Write && result.Changed)
                    File.WriteAllText(file, result.Text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                else if (!command.Check && !command.Write && !command.Json)
                    Console.Write(result.Text);

                jsonResults.Add(new
                {
                    file,
                    changed = result.Changed,
                    written = command.Write && result.Changed
                });

                if (!command.Json && (command.Write || command.Check))
                {
                    string status = result.Changed
                        ? command.Write ? "formatted" : "needs format"
                        : "ok";
                    Console.WriteLine($"[FORMAT] {status}: {file}");
                }
            }
            catch (Exception ex)
            {
                ErrorDiagnostic diag = ErrorDiagnostics.FromException(ex, file);
                CfgsToolDiagnostic diagnostic = new(
                    diag.Code,
                    CfgsToolDiagnosticSeverity.Error,
                    diag.Message,
                    diag.SourceFile ?? file,
                    Math.Max(1, diag.Line ?? 1),
                    Math.Max(1, diag.Column ?? 1),
                    Math.Max(1, diag.SpanLength ?? 1));

                if (command.Json)
                    jsonResults.Add(new { file, error = ToDiagnosticJsonPayload(diagnostic) });
                else
                    Console.Error.WriteLine(FormatToolDiagnostic(diagnostic));

                return 1;
            }
        }

        if (command.Json)
            Console.WriteLine(JsonSerializer.Serialize(jsonResults, new JsonSerializerOptions { WriteIndented = true }));

        if (command.Check && anyChanged)
            return 1;

        return 0;
    }

    private static void ValidateCfgsFile(string file)
    {
        VM vm = new();
        _ = CfgsCompiler.CompileFile(
            file,
            loadPluginDll: vm.LoadPlugin,
            namespaces: vm.Namespaces,
            compilerVersion: Version);
    }

    private static IReadOnlyList<string> CollectCfgsFiles(string input)
    {
        string fullPath = Path.GetFullPath(input);
        if (File.Exists(fullPath))
            return string.Equals(Path.GetExtension(fullPath), ".cfs", StringComparison.OrdinalIgnoreCase)
                ? [fullPath]
                : [];

        if (!Directory.Exists(fullPath))
            return [];

        return Directory
            .EnumerateFiles(fullPath, "*.cfs", SearchOption.AllDirectories)
            .Where(static p => !IsGeneratedOrBuildPath(p))
            .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsGeneratedOrBuildPath(string path)
    {
        string normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        string separator = Path.DirectorySeparatorChar.ToString();
        return normalized.Contains($"{separator}bin{separator}", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains($"{separator}obj{separator}", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains($"{separator}dist{separator}", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains($"{separator}dist_release{separator}", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains($"{separator}.git{separator}", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains($"{separator}.vs{separator}", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatToolDiagnostic(CfgsToolDiagnostic diagnostic)
        => $"{diagnostic.SourceFile}:{diagnostic.Line}:{diagnostic.Column}: {diagnostic.SeverityName} {diagnostic.Code}: {diagnostic.Message}";

    private static object ToDiagnosticJsonPayload(CfgsToolDiagnostic diagnostic)
        => new
        {
            code = diagnostic.Code,
            severity = diagnostic.SeverityName,
            message = diagnostic.Message,
            source = diagnostic.SourceFile,
            line = diagnostic.Line,
            column = diagnostic.Column,
            spanLength = diagnostic.SpanLength
        };

    private static void ApplyToolingConfig(string input, LintCliCommand command)
    {
        using JsonDocument? config = TryReadToolingConfig(input);
        if (config is null)
            return;

        JsonElement tooling = config.RootElement.GetProperty("tooling");
        if (!command.MaxLineLengthSet && TryGetToolingProperty(tooling, "maxLineLength", out JsonElement maxLineLength) && maxLineLength.TryGetInt32(out int max) && max > 0)
            command.MaxLineLength = max;
        if (!command.WarningsAsErrorsSet && TryGetToolingProperty(tooling, "warningsAsErrors", out JsonElement warningsAsErrors) && warningsAsErrors.ValueKind is JsonValueKind.True or JsonValueKind.False)
            command.WarningsAsErrors = warningsAsErrors.GetBoolean();
        if (!command.AllowTabsSet && TryGetToolingProperty(tooling, "allowTabs", out JsonElement allowTabs) && allowTabs.ValueKind is JsonValueKind.True or JsonValueKind.False)
            command.AllowTabs = allowTabs.GetBoolean();
        if (!command.IndentSizeSet && TryGetToolingProperty(tooling, "indentSize", out JsonElement indentSize) && indentSize.TryGetInt32(out int indent) && indent > 0)
            command.IndentSize = indent;
    }

    private static void ApplyToolingConfig(string input, FormatCliCommand command)
    {
        using JsonDocument? config = TryReadToolingConfig(input);
        if (config is null)
            return;

        JsonElement tooling = config.RootElement.GetProperty("tooling");
        if (!command.IndentSizeSet && TryGetToolingProperty(tooling, "indentSize", out JsonElement indentSize) && indentSize.TryGetInt32(out int indent) && indent > 0)
            command.IndentSize = indent;
        if (!command.UseTabsSet && TryGetToolingProperty(tooling, "useTabs", out JsonElement useTabs) && useTabs.ValueKind is JsonValueKind.True or JsonValueKind.False)
            command.UseTabs = useTabs.GetBoolean();
    }

    private static JsonDocument? TryReadToolingConfig(string input)
    {
        string fullInput = Path.GetFullPath(input);
        string? dir = Directory.Exists(fullInput) ? fullInput : Path.GetDirectoryName(fullInput);

        while (!string.IsNullOrWhiteSpace(dir))
        {
            string manifestPath = Path.Combine(dir, CfgsPackageManager.ManifestFileName);
            if (File.Exists(manifestPath))
            {
                try
                {
                    JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath), new JsonDocumentOptions
                    {
                        AllowTrailingCommas = true,
                        CommentHandling = JsonCommentHandling.Skip
                    });

                    if (document.RootElement.ValueKind == JsonValueKind.Object &&
                        document.RootElement.TryGetProperty("tooling", out JsonElement tooling) &&
                        tooling.ValueKind == JsonValueKind.Object)
                        return document;

                    document.Dispose();
                    return null;
                }
                catch (JsonException)
                {
                    return null;
                }
                catch (IOException)
                {
                    return null;
                }
            }

            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }

    private static bool TryGetToolingProperty(JsonElement tooling, string name, out JsonElement value)
        => tooling.TryGetProperty(name, out value);

    private static void LoadReplPluginsFromApplicationPath(VM replVm)
    {
        string applicationDirectory = AppContext.BaseDirectory;
        string configPath = Path.Combine(applicationDirectory, "repl_plugins.json");
        if (!File.Exists(configPath))
            return;

        IReadOnlyList<string> pluginEntries;
        try
        {
            pluginEntries = ReadReplPluginEntries(configPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[REPL] could not read repl_plugins.json: {ex.Message}");
            return;
        }

        int loaded = 0;
        int skipped = 0;

        foreach (string pluginEntry in pluginEntries)
        {
            if (string.IsNullOrWhiteSpace(pluginEntry))
                continue;

            string pluginFile = ResolveReplPluginPath(applicationDirectory, pluginEntry);

            if (!string.Equals(Path.GetExtension(pluginFile), ".dll", StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                Console.Error.WriteLine($"[REPL] skipped plugin '{pluginEntry}': entry is not a DLL");
                continue;
            }

            if (!File.Exists(pluginFile))
            {
                skipped++;
                Console.Error.WriteLine($"[REPL] skipped plugin '{pluginEntry}': file not found");
                continue;
            }

            try
            {
                replVm.LoadPlugin(pluginFile);
                loaded++;
            }
            catch (Exception ex)
            {
                skipped++;
                Console.Error.WriteLine($"[REPL] skipped plugin '{pluginEntry}': {ex.Message}");
            }
        }

        if (loaded > 0 || skipped > 0)
            Console.WriteLine($"[REPL] plugins loaded: {loaded}, skipped: {skipped}");
    }

    private static IReadOnlyList<string> ReadReplPluginEntries(string configPath)
    {
        using FileStream stream = File.OpenRead(configPath);
        using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });

        JsonElement pluginsElement = document.RootElement;
        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            if (!document.RootElement.TryGetProperty("plugins", out pluginsElement))
                throw new InvalidDataException("expected a 'plugins' array");
        }

        if (pluginsElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("expected a JSON array of plugin paths");

        List<string> entries = new();
        foreach (JsonElement entryElement in pluginsElement.EnumerateArray())
        {
            if (entryElement.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("plugin entries must be strings");

            entries.Add(entryElement.GetString() ?? string.Empty);
        }

        return entries;
    }

    private static string ResolveReplPluginPath(string applicationDirectory, string pluginEntry)
    {
        string normalizedEntry = Environment.ExpandEnvironmentVariables(pluginEntry.Trim());
        if (Path.IsPathFullyQualified(normalizedEntry))
            return normalizedEntry;

        bool hasDirectoryPart =
            normalizedEntry.Contains(Path.DirectorySeparatorChar) ||
            normalizedEntry.Contains(Path.AltDirectorySeparatorChar);

        string relativePath = hasDirectoryPart
            ? normalizedEntry
            : Path.Combine("plugins", normalizedEntry);

        return Path.GetFullPath(Path.Combine(applicationDirectory, relativePath));
    }

    /// <summary>
    /// Detects whether the current invocation should start the embedded LSP mode.
    /// </summary>
    /// <param name="args">The args<see cref="string[]"/></param>
    /// <param name="forwardedArgs">The forwardedArgs<see cref="string[]"/></param>
    /// <returns>The <see cref="bool"/></returns>
    private static bool TryGetLspArguments(string[] args, out string[] forwardedArgs)
    {
        List<string> remaining = new(args.Length);
        bool lspMode = false;

        foreach (string arg in args)
        {
            if (string.Equals(arg, "-lsp", StringComparison.OrdinalIgnoreCase))
            {
                lspMode = true;
                continue;
            }

            remaining.Add(arg);
        }

        forwardedArgs = remaining.ToArray();
        return lspMode;
    }

    /// <summary>
    /// Starts the CFGS language server by loading the external LSP host assembly on demand.
    /// </summary>
    /// <param name="args">The args<see cref="string[]"/></param>
    /// <returns>The <see cref="Task{int}"/></returns>
    private static async Task<int> RunLspModeAsync(string[] args)
    {
        string[] searchPaths =
        {
            Path.Combine(AppContext.BaseDirectory, "CFGS.Lsp.dll"),
            Path.Combine(AppContext.BaseDirectory, "plugins", "CFGS.Lsp.dll")
        };

        string? lspAssemblyPath = searchPaths.FirstOrDefault(File.Exists);
        if (lspAssemblyPath is null)
        {
            Console.Error.WriteLine("Could not locate CFGS.Lsp.dll for -lsp mode.");
            foreach (string path in searchPaths)
                Console.Error.WriteLine($"  searched: {path}");

            return 1;
        }

        try
        {
            Assembly lspAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(lspAssemblyPath);
            MethodInfo? entryPoint = lspAssembly.EntryPoint;
            if (entryPoint is null)
            {
                Console.Error.WriteLine($"CFGS.Lsp entry point not found in '{lspAssemblyPath}'.");
                return 1;
            }

            ParameterInfo[] parameters = entryPoint.GetParameters();
            object? invocationResult = parameters.Length switch
            {
                0 => entryPoint.Invoke(null, null),
                1 => entryPoint.Invoke(null, new object?[] { args }),
                _ => throw new InvalidOperationException($"Unsupported CFGS.Lsp entry point signature with {parameters.Length} parameters.")
            };

            if (invocationResult is Task<int> exitCodeTask)
                return await exitCodeTask;

            if (invocationResult is Task task)
            {
                await task;
                return Environment.ExitCode;
            }

            return invocationResult is int exitCode ? exitCode : Environment.ExitCode;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            Console.Error.WriteLine($"Failed to start CFGS LSP mode: {ex.InnerException.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to start CFGS LSP mode: {ex.Message}");
            return 1;
        }
    }

    private static void PrintTrackedException(Exception ex, string phase, string? sourceName = null)
    {
        ErrorDiagnostic diag = ErrorDiagnostics.FromException(ex, sourceName);
        string? errorId = ErrorTracker.Track(ex, phase, sourceName);
        Console.WriteLine(ErrorDiagnostics.FormatHeadline(diag));

        string? location = ErrorDiagnostics.FormatLocation(diag);
        if (!string.IsNullOrWhiteSpace(location))
            Console.WriteLine($"at {location}");

        string? sourceSpan = ErrorDiagnostics.FormatSourceSpan(diag);
        if (!string.IsNullOrWhiteSpace(sourceSpan))
            Console.WriteLine(sourceSpan);

        string? help = ErrorDiagnostics.FormatHelp(diag);
        if (!string.IsNullOrWhiteSpace(help))
            Console.WriteLine(help);

        string? languageStack = ErrorDiagnostics.FormatLanguageStack(diag);
        if (!string.IsNullOrWhiteSpace(languageStack))
        {
            Console.WriteLine("stacktrace:");
            Console.WriteLine(languageStack);
        }
        else if (IsDebug && !string.IsNullOrWhiteSpace(diag.ManagedStack))
        {
            Console.WriteLine("managed stack:");
            Console.WriteLine(diag.ManagedStack);
        }

        if (!string.IsNullOrWhiteSpace(errorId))
            Console.WriteLine($"[Error-Id: {errorId}]");
    }

    private static void PrintDiagnosticJson(Exception ex, string? sourceName = null)
    {
        ErrorDiagnostic diag = ErrorDiagnostics.FromException(ex, sourceName);
        var payload = new[]
        {
            new
            {
                code = diag.Code,
                category = diag.Category,
                message = diag.Message,
                sourceFile = diag.SourceFile,
                line = diag.Line,
                column = diag.Column,
                spanLength = diag.SpanLength,
                help = diag.Help,
                sourceSpan = ErrorDiagnostics.FormatSourceSpan(diag)
            }
        };

        Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task RunSourceAsync(
        string source,
        string name,
        bool debug = false,
        bool interactiveDebug = false,
        CancellationToken ct = default,
        VM? sharedVm = null,
        string? workingDirectory = null)
    {
        VM vm = sharedVm ?? new VM();
        vm.Debugger = interactiveDebug ? new ConsoleVmDebugger() : null;
        vm.EntryScriptPath = string.Equals(name, "<repl>", StringComparison.Ordinal)
            ? "<repl>"
            : Path.GetFullPath(name);
        vm.WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory;
        vm.NativeAssemblyBaseDirectory = workingDirectory ?? Environment.CurrentDirectory;

        CompiledScript compiled = CfgsCompiler.CompileSource(
            name,
            source,
            loadPluginDll: vm.LoadPlugin,
            namespaces: vm.Namespaces,
            workingDirectory: workingDirectory,
            compilerVersion: Version);
        bool isRepl = string.Equals(name, "<repl>", StringComparison.Ordinal);
        if (sharedVm is not null && isRepl)
            vm.LoadReplCompiledScript(compiled);
        else
            vm.LoadCompiledScript(compiled);

        if (debug)
            PrintInstructions(name, compiled.Instructions, compiled.Functions);

        await vm.RunAsync(debug, 0, ct);

        if ((sharedVm is null || isRepl) && compiled.ShouldAutoInvokeMain)
            _ = await vm.InvokeGlobalFunctionAsync("main", originFile: name);

        if (debug)
            WriteDebugLog(vm, workingDirectory);
    }

    private static void CompileFileToCfb(string inputPath, string? outputPath)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException($"Could not load the script-file : '{inputPath}'", inputPath);

        string output = string.IsNullOrWhiteSpace(outputPath)
            ? Path.ChangeExtension(inputPath, ".cfb")
            : outputPath!;

        VM compileVm = new();
        CompiledScript compiled = CfgsCompiler.CompileFile(
            inputPath,
            loadPluginDll: compileVm.LoadPlugin,
            namespaces: compileVm.Namespaces,
            compilerVersion: Version);
        CfbFile.Save(compiled, output);
        Console.WriteLine($"[CFB] compiled '{inputPath}' -> '{output}'");
    }

    private static void CheckFile(string inputPath, bool json)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException($"Could not load the script-file : '{inputPath}'", inputPath);

        if (inputPath.EndsWith(".cfb", StringComparison.OrdinalIgnoreCase))
        {
            _ = CfbFile.Load(inputPath);
        }
        else
        {
            VM checkVm = new();
            _ = CfgsCompiler.CompileFile(
                inputPath,
                loadPluginDll: checkVm.LoadPlugin,
                namespaces: checkVm.Namespaces,
                compilerVersion: Version);
        }

        if (json)
            Console.WriteLine("[]");
        else
            Console.WriteLine($"[CHECK] ok: {inputPath}");
    }

    private static async Task RunCompiledFileAsync(
        string file,
        bool debug = false,
        bool interactiveDebug = false,
        CancellationToken ct = default,
        string? workingDirectory = null)
    {
        CompiledScript compiled = CfbFile.Load(file);
        VM vm = new();
        vm.Debugger = interactiveDebug ? new ConsoleVmDebugger() : null;
        vm.EntryScriptPath = Path.GetFullPath(file);
        vm.WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(vm.EntryScriptPath) ?? Environment.CurrentDirectory;
        vm.NativeAssemblyBaseDirectory = workingDirectory ?? Path.GetDirectoryName(vm.EntryScriptPath) ?? Environment.CurrentDirectory;

        foreach (string plugin in compiled.RequiredPlugins)
            vm.LoadPlugin(ResolveCompiledPluginPath(plugin, workingDirectory));

        vm.LoadCompiledScript(compiled);

        if (debug)
            PrintInstructions(file, compiled.Instructions, compiled.Functions);

        await vm.RunAsync(debug, 0, ct);

        if (compiled.ShouldAutoInvokeMain)
            _ = await vm.InvokeGlobalFunctionAsync("main", originFile: file);

        if (debug)
            WriteDebugLog(vm, workingDirectory);
    }

    private static string ResolveCompiledPluginPath(string pluginReference, string? workingDirectory)
    {
        string plugin = Environment.ExpandEnvironmentVariables(pluginReference.Trim());
        if (Path.IsPathFullyQualified(plugin))
            return plugin;

        bool hasDirectoryPart =
            plugin.Contains(Path.DirectorySeparatorChar) ||
            plugin.Contains(Path.AltDirectorySeparatorChar);

        string baseDirectory = workingDirectory ?? Environment.CurrentDirectory;
        List<string> candidates = hasDirectoryPart
            ? [
                Path.Combine(baseDirectory, plugin),
                Path.Combine(AppContext.BaseDirectory, plugin)
              ]
            : [
                Path.Combine(baseDirectory, "plugins", plugin),
                Path.Combine(AppContext.BaseDirectory, "plugins", plugin),
                Path.Combine(baseDirectory, plugin),
                Path.Combine(AppContext.BaseDirectory, plugin)
              ];

        if (!hasDirectoryPart)
        {
            foreach (string packagePluginDirectory in GetPackagePluginDirectories(baseDirectory))
                candidates.Insert(0, Path.Combine(packagePluginDirectory, plugin));
        }

        foreach (string candidate in candidates)
        {
            string fullCandidate = Path.GetFullPath(candidate);
            if (File.Exists(fullCandidate))
                return fullCandidate;
        }

        return Path.GetFullPath(Path.Combine(baseDirectory, hasDirectoryPart ? plugin : Path.Combine("plugins", plugin)));
    }

    private static IEnumerable<string> GetPackagePluginDirectories(string startDirectory)
    {
        DirectoryInfo? current;
        try
        {
            current = new DirectoryInfo(Path.GetFullPath(startDirectory));
        }
        catch (ArgumentException)
        {
            yield break;
        }
        catch (IOException)
        {
            yield break;
        }

        HashSet<string> yielded = new(StringComparer.OrdinalIgnoreCase);
        while (current is not null)
        {
            string packagePluginDirectory = Path.Combine(
                current.FullName,
                CfgsPackageManager.PackageDirectoryName,
                CfgsPackageManager.PluginDirectoryName);

            if (Directory.Exists(packagePluginDirectory) && yielded.Add(packagePluginDirectory))
                yield return packagePluginDirectory;

            if (File.Exists(Path.Combine(current.FullName, CfgsPackageManager.ManifestFileName)))
                yield break;

            current = current.Parent;
        }
    }

    private static void WriteDebugLog(VM vm, string? workingDirectory)
    {
        try
        {
            vm.DebugOutput.Position = 0;
            string logDirectory = workingDirectory ?? Environment.CurrentDirectory;
            Directory.CreateDirectory(logDirectory);
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            string lpath = Path.Combine(logDirectory, $"log_file_{timestamp}_{Environment.ProcessId}.log");
            using FileStream debugFile = File.Create(lpath);
            vm.DebugOutput.CopyTo(debugFile);
            Console.WriteLine($"[DEBUG] log-file created : {lpath}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DEBUG] could not write log-file: {ex.Message}");
        }
    }

    private static void PrintInstructions(string name, List<Instruction> bytecode, Dictionary<string, FunctionInfo> _functions)
    {
        Console.WriteLine($"=== INSTRUCTIONS ({name}) ===");

        int opCodeWidth = Math.Max(bytecode.Max(i => i.Code.ToString().Length), "OpCode".Length);
        int operandWidth = Math.Max(bytecode.Max(i => i.Operand?.ToString()?.Length ?? 4), "Operand".Length);

        string header = "| " + "Line,Col".PadRight(15)
                      + " | " + "Instr#".PadRight(8)
                      + " | " + "OpCode".PadRight(opCodeWidth)
                      + " | " + "Operand".PadRight(operandWidth)
                      + " |";
        Console.WriteLine(header);
        Console.WriteLine(new string('-', header.Length));

        for (int idx = 0; idx < bytecode.Count; idx++)
        {
            Instruction ins = bytecode[idx];
            string lineCol = $"[{ins.Line:00000},{ins.Col:00000}]";
            string instrNum = $"[{idx + 1:00000}]";
            string opCode = ins.Code.ToString().PadRight(opCodeWidth);
            string opval = (ins.Operand?.ToString() ?? "null");
            string operand = (opval.Length > 10 ? opval[..10] : opval).PadRight(operandWidth);
            opval = string.Empty;
            Console.WriteLine($"| {lineCol} | {instrNum} | {opCode} | {operand} |");
        }

        Console.WriteLine("=== END ===");

        if (_functions.Count > 0)
        {
            Console.WriteLine("=== Functions ===");
            foreach (KeyValuePair<string, FunctionInfo> f in _functions)
                Console.WriteLine(f.Key + " -> " + f.Value);
            Console.WriteLine();
        }
    }

    private static string? ReadRedirectedInput()
    {
        while (true)
        {
            string? raw = Console.ReadLine();
            if (raw is null)
                return null;

            string rawTrimmed = raw.Trim();
            if (!rawTrimmed.StartsWith('$'))
                return raw;

            string commandRaw = rawTrimmed[1..].TrimStart('\uFEFF');
            string trimmed = commandRaw.Trim();

            if (string.IsNullOrWhiteSpace(trimmed))
                return string.Empty;

            if (trimmed.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("quit", StringComparison.OrdinalIgnoreCase))
                return null;

            if (trimmed.Equals("clear", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("cls", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(header_text);
                return string.Empty;
            }

            if (trimmed.Equals("debug", StringComparison.OrdinalIgnoreCase))
            {
                IsDebug = !IsDebug;
                Console.WriteLine($"Debug mode is now {(IsDebug ? "Enabled" : "Disabled")}");
                return string.Empty;
            }

            if (trimmed.Equals("ansi", StringComparison.OrdinalIgnoreCase))
            {
                if (AnsiMode)
                    AnsiConsole.DisableAnsi();
                else
                    AnsiConsole.EnableAnsi();

                AnsiMode = !AnsiMode;
                Console.WriteLine("Ansi-Mode is " + (AnsiMode ? "enabled" : "disabled"));
                return string.Empty;
            }

            if (trimmed.Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(help_diag);
                return string.Empty;
            }

            if (trimmed.StartsWith("buffer", StringComparison.OrdinalIgnoreCase))
            {
                int sep = trimmed.IndexOf(":");
                if (sep == -1 || string.IsNullOrWhiteSpace(trimmed[(sep + 1)..]))
                {
                    Console.WriteLine("Usage : $buffer:len");
                    return string.Empty;
                }

                string lenStr = trimmed[(sep + 1)..];
                if (int.TryParse(lenStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int len))
                {
                    VM.DEBUG_BUFFER = len;
                    Console.WriteLine($"[SETTINGS] Debug-Buffer set to {VM.DEBUG_BUFFER}");
                }
                else
                {
                    Console.WriteLine("Error : Buffer length only takes integers");
                }

                return string.Empty;
            }

            return raw;
        }
    }

    /// <summary>
    /// Returns whether the current REPL buffer should be submitted.
    /// Clearly incomplete input stays buffered; complete or invalid input
    /// is submitted so the user gets immediate feedback.
    /// </summary>
    private static bool ShouldSubmitReplInput(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;

        try
        {
            Lexer lexer = new("<repl>", source);
            Parser parser = new(lexer, Parser.TopLevelMode.Script);
            _ = parser.Parse();
            return true;
        }
        catch (ParserException ex) when (IsIncompleteReplParse(ex))
        {
            return false;
        }
        catch (LexerException ex) when (IsIncompleteReplLex(ex))
        {
            return false;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Returns whether the parser exception points to unfinished REPL input.
    /// </summary>
    private static bool IsIncompleteReplParse(ParserException ex)
    {
        string msg = ex.RawMessage ?? ex.Message ?? string.Empty;
        return msg.Contains("before end of file", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("end of file in namespace body", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("got EOF", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns whether the lexer exception points to unfinished REPL input.
    /// </summary>
    private static bool IsIncompleteReplLex(LexerException ex)
    {
        string msg = ex.RawMessage ?? ex.Message ?? string.Empty;
        return msg.Contains("unterminated", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadMultilineInput()
    {
        if (Console.IsInputRedirected)
            return ReadRedirectedInput();

        List<string> buffer = new();
        StringBuilder line = new();
        string prompt = "> ";

        int indentTabs = 0;
        const int TabSize = 4;

        int visibleLen = 0;
        int? lSelect = null;

        static string IndentStr(int tabs, int size) => new(' ', tabs * size);

        (int tabs, string content) SplitIndent(string s)
        {
            int i = 0;
            while (i < s.Length && s[i] == ' ') i++;
            int tabs = i / TabSize;
            int leftover = i - tabs * TabSize;
            string content = new string(' ', leftover) + s.Substring(i);
            return (tabs, content);
        }

        void WritePrompt() => Console.Write(prompt);

        void RenderCurrent()
        {
            while (visibleLen > 0) { Console.Write("\b \b"); visibleLen--; }

            string composed = IndentStr(indentTabs, TabSize) + line.ToString();
            Console.Write(composed);
            visibleLen = composed.Length;
        }

        void SetCurrentLine(string text)
        {
            line.Clear();
            line.Append(text);
            RenderCurrent();
        }

        void StartNewInputLine()
        {
            WritePrompt();
            visibleLen = 0;
            RenderCurrent();
        }

        void RedrawAll()
        {
            Console.Clear();
            Console.WriteLine(header_text);
            for (int i = 0; i < buffer.Count; i++)
            {
                string pr = (i == 0) ? "> " : "> ";
                Console.Write(pr);
                Console.WriteLine(buffer[i]);
            }
            prompt = (buffer.Count == 0) ? "> " : "> ";
            StartNewInputLine();
        }

        void DoClear()
        {
            buffer.Clear();
            line.Clear();
            indentTabs = 0;
            lSelect = null;
            visibleLen = 0;
            RedrawAll();
        }

        StartNewInputLine();

        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter && (key.Modifiers & ConsoleModifiers.Control) != 0)
            {
                Console.WriteLine();
                if (line.Length > 0 || indentTabs > 0)
                    buffer.Add(IndentStr(indentTabs, TabSize) + line.ToString());
                return string.Join("\n", buffer);
            }

            if (key.Key == ConsoleKey.Backspace && (key.Modifiers & ConsoleModifiers.Control) != 0)
            {
                DoClear();
                continue;
            }

            if (key.Key == ConsoleKey.Tab && (key.Modifiers & ConsoleModifiers.Control) != 0)
            {
                if (indentTabs > 0)
                {
                    indentTabs--;
                    RenderCurrent();
                }
                continue;
            }

            if (key.Key == ConsoleKey.Tab)
            {
                indentTabs++;
                RenderCurrent();
                continue;
            }

            if (key.Key == ConsoleKey.UpArrow)
            {
                if (buffer.Count > 0)
                {
                    if (lSelect is null) lSelect = buffer.Count;
                    lSelect = Math.Max(1, lSelect.Value - 1);
                    indentTabs = 0;
                    SetCurrentLine($"$L {lSelect.Value} ");
                }
                continue;
            }

            if (key.Key == ConsoleKey.DownArrow)
            {
                if (lSelect is null) lSelect = Math.Max(1, buffer.Count);
                lSelect = Math.Min(buffer.Count + 1, lSelect.Value + 1);
                indentTabs = 0;
                SetCurrentLine($"$L {lSelect.Value} ");
                continue;
            }

            if (key.Key == ConsoleKey.Enter)
            {
                string current = IndentStr(indentTabs, TabSize) + line.ToString();
                string trimmed = current.Trim();

                if (trimmed.StartsWith("$L", StringComparison.OrdinalIgnoreCase))
                {
                    string rest = trimmed.Substring(2).TrimStart();
                    int spaceIdx = rest.IndexOf(' ');
                    string numStr = (spaceIdx >= 0) ? rest.Substring(0, spaceIdx) : rest;
                    string newContent = (spaceIdx >= 0) ? rest.Substring(spaceIdx + 1) : string.Empty;

                    if (int.TryParse(numStr, out int oneBased) && oneBased >= 1)
                    {
                        int idx = oneBased - 1;

                        if (idx < buffer.Count)
                        {
                            buffer[idx] = newContent;
                            line.Clear();
                            lSelect = null;
                            Console.WriteLine();
                            StartNewInputLine();
                            continue;
                        }
                        else if (idx == buffer.Count)
                        {
                            buffer.Add(newContent);
                            line.Clear();
                            lSelect = null;
                            Console.WriteLine();
                            StartNewInputLine();
                            continue;
                        }
                        else
                        {
                            Console.WriteLine();
                            Console.WriteLine($"# Zeilennummer {oneBased} ist außerhalb des gültigen Bereichs (1..{buffer.Count + 1}).");
                            StartNewInputLine();
                            line.Clear();
                            lSelect = null;
                            continue;
                        }
                    }
                    else
                    {
                        Console.WriteLine();
                        Console.WriteLine("# Invalid usage : $L <Zeilennummer> <Inhalt>");
                        StartNewInputLine();
                        line.Clear();
                        lSelect = null;
                        continue;
                    }
                }

                if (buffer.Count == 0 && trimmed.StartsWith('$'))
                {
                    string command = trimmed[1..].TrimStart();

                    if (command.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                        command.Equals("quit", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine();
                        return null;
                    }

                    if (command.Equals("clear", StringComparison.OrdinalIgnoreCase) ||
                        command.Equals("cls", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine();
                        DoClear();
                        continue;
                    }

                    if (command.Equals("debug", StringComparison.OrdinalIgnoreCase))
                    {
                        IsDebug = !IsDebug;
                        Console.WriteLine();
                        Console.WriteLine($"Debug mode is now {(IsDebug ? "Enabled" : "Disabled")}");
                        buffer.Clear();
                        line.Clear();
                        lSelect = null;
                        prompt = "> ";
                        StartNewInputLine();
                        continue;
                    }

                    if (command.Equals("ansi", StringComparison.OrdinalIgnoreCase))
                    {
                        if (AnsiMode)
                            AnsiConsole.DisableAnsi();
                        else
                            AnsiConsole.EnableAnsi();

                        AnsiMode = !AnsiMode;
                        Console.WriteLine();
                        Console.WriteLine("Ansi-Mode is " + (AnsiMode ? "enabled" : "disabled"));
                        buffer.Clear();
                        line.Clear();
                        lSelect = null;
                        prompt = "> ";
                        StartNewInputLine();
                        continue;
                    }

                    if (command.Equals("help", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine();
                        Console.WriteLine(help_diag);
                        buffer.Clear();
                        line.Clear();
                        lSelect = null;
                        prompt = "> ";
                        StartNewInputLine();
                        continue;
                    }

                    if (command.StartsWith("buffer", StringComparison.OrdinalIgnoreCase))
                    {
                        int restPos = command.IndexOf(":");
                        if (restPos == -1)
                        {
                            Console.WriteLine();
                            Console.WriteLine("Usage : $buffer:len");
                            goto abort_buf;
                        }

                        string lenStr = command.Substring(restPos + 1);
                        if (lenStr.Trim() == string.Empty)
                        {
                            Console.WriteLine();
                            Console.WriteLine("Usage : $buffer:len");
                            goto abort_buf;
                        }

                        if (int.TryParse(lenStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out restPos))
                        {
                            VM.DEBUG_BUFFER = restPos;
                            Console.WriteLine();
                            Console.WriteLine($"[SETTINGS] Debug-Buffer set to {VM.DEBUG_BUFFER}");
                        }
                        else
                        {
                            Console.WriteLine();
                            Console.WriteLine("Error : Buffer length only takes integers");
                        }

                        abort_buf:
                        buffer.Clear();
                        line.Clear();
                        lSelect = null;
                        prompt = "> ";
                        StartNewInputLine();
                        continue;
                    }
                }

                List<string> pendingLines = new(buffer);
                if (current.Length > 0)
                    pendingLines.Add(current);

                string pendingCode = string.Join("\n", pendingLines);
                if (ShouldSubmitReplInput(pendingCode))
                {
                    Console.WriteLine();
                    return pendingCode;
                }

                buffer.Add(current);
                line.Clear();
                lSelect = null;
                Console.WriteLine();
                StartNewInputLine();
                continue;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (line.Length > 0)
                {
                    line.Length--;
                    RenderCurrent();
                }
                else if (indentTabs > 0)
                {
                    indentTabs--;
                    RenderCurrent();
                }
                else
                {
                    if (buffer.Count > 0)
                    {
                        string last = buffer[buffer.Count - 1];
                        buffer.RemoveAt(buffer.Count - 1);

                        (int tabs, string content) split = SplitIndent(last);
                        indentTabs = split.tabs;
                        line.Clear();
                        line.Append(split.content);

                        RedrawAll();
                    }
                }

                lSelect = null;
                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                line.Append(key.KeyChar);
                RenderCurrent();
                lSelect = null;
            }
        }
    }
}
