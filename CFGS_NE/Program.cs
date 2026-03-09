using CFGS_VM.Analytic.Core;
using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore;
using CFGS_VM.VMCore.Extensions;
using CFGS_VM.VMCore.Extensions.Core;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// Defines the <see cref="AnsiConsole" />
/// </summary>
internal static class AnsiConsole
{
    /// <summary>
    /// Defines the STD_OUTPUT_HANDLE
    /// </summary>
    internal const int STD_OUTPUT_HANDLE = -11;

    /// <summary>
    /// Defines the ENABLE_VIRTUAL_TERMINAL_PROCESSING
    /// </summary>
    internal const int ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

    /// <summary>
    /// The GetStdHandle
    /// </summary>
    /// <param name="nStdHandle">The nStdHandle<see cref="int"/></param>
    /// <returns>The <see cref="IntPtr"/></returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr GetStdHandle(int nStdHandle);

    /// <summary>
    /// The GetConsoleMode
    /// </summary>
    /// <param name="hConsoleHandle">The hConsoleHandle<see cref="IntPtr"/></param>
    /// <param name="lpMode">The lpMode<see cref="int"/></param>
    /// <returns>The <see cref="bool"/></returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetConsoleMode(IntPtr hConsoleHandle, out int lpMode);

    /// <summary>
    /// The SetConsoleMode
    /// </summary>
    /// <param name="hConsoleHandle">The hConsoleHandle<see cref="IntPtr"/></param>
    /// <param name="dwMode">The dwMode<see cref="int"/></param>
    /// <returns>The <see cref="bool"/></returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool SetConsoleMode(IntPtr hConsoleHandle, int dwMode);

    /// <summary>
    /// The EnableAnsi
    /// </summary>
    public static void EnableAnsi()
    {
        nint hOut = GetStdHandle(STD_OUTPUT_HANDLE);
        if (hOut == IntPtr.Zero) return;
        if (!GetConsoleMode(hOut, out int mode)) return;
        SetConsoleMode(hOut, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
    }

    /// <summary>
    /// The DisableAnsi
    /// </summary>
    public static void DisableAnsi()
    {
        nint hOut = GetStdHandle(STD_OUTPUT_HANDLE);
        if (hOut == IntPtr.Zero) return;
        if (!GetConsoleMode(hOut, out int mode)) return;
        mode &= ~ENABLE_VIRTUAL_TERMINAL_PROCESSING;
        SetConsoleMode(hOut, mode);
    }
}

/// <summary>
/// Defines the <see cref="Program" />
/// </summary>
public class Program
{
    /// <summary>
    /// Gets a value indicating whether IsDebug
    /// </summary>
    public static bool IsDebug { get; private set; } = false;

    /// <summary>
    /// Defines the Version
    /// </summary>
    public const string Version = "v4.2.7";

    /// <summary>
    /// Defines the AnsiMode
    /// </summary>
    public static bool AnsiMode = true;

    /// <summary>
    /// Defines the CLIPath
    /// </summary>
    public static string CLIPath = string.Empty;

    /// <summary>
    /// Defines the header_text
    /// </summary>
    private static readonly string header_text =
        $"[ Configuration-Language ] [ Version {Version} ]\n Enter your code, 'help' or 'exit/quit'\n Visit : https://github.com/BlackFoX1991/ConfigurationScript-VM";

    /// <summary>
    /// Defines the help_diag
    /// </summary>
    private static readonly string help_diag = @"
=====================================================================================================
                      #####                    
            ####     ##:::::+#####              [===================HELP============================]
           ############......::::=###           [ Enter your code or use the following commands     ]
           ############..........:::*##         [ buffer:length to set the logfile max. output      ]
     ############     #.............:::##       [ cls/ clear to clear the console                   ]  
    ##########        #...............::##      [ debug to enable/disable the debug mode            ]
      ######         %#+++.............::##     [ ansi to enable or disable Ansi-Console-Mode       ]
      #####        ####+++++++.........:::##    =====================================================
  ########        #####++++++++.........::##    [ Commands : -d(ebug) -p(arams)]
  ########        #####+++++++++........::##    [            -s(et) buffer  maxlen                  ]
      #####        ####++++++++*.......::-##    [                   ansi    1 or 0 to en/dis-able   ]   
      ######        *##++++++**........::##     [--------------REPL INFO----------------------------]
    ##########        #******........:::##      [ Use Ctrl+Enter to run the entered code            ]
     ############     #.:...........::###       [ Use Ctrl+Backspace to clear the code              ]
           ############..........:::###         [ Enter $L <Line> <Content> to edit a specific Line ]
            ###########......::::####           [ Use Arrow Up/Down to trigger $L command           ]
            *###     ##:::::#####               [---------------------------------------------------]
                      #####                  
=====================================================================================================";

    /// <summary>
    /// The Main
    /// </summary>
    /// <param name="args">The args<see cref="string[]"/></param>
    public static async Task Main(string[] args)
    {
        if (OperatingSystem.IsWindows())
            AnsiConsole.EnableAnsi();

        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

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

                    if (file.EndsWith(".cfb", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"Could not load the script-file : '{file}'. Binary .cfb support has been removed.");
                        currentSourceForError = null;
                        continue;
                    }

                    string input = File.ReadAllText(file);

                    Environment.CurrentDirectory =
                        Path.GetDirectoryName(Path.GetFullPath(file))
                        ?? Path.GetDirectoryName(Environment.ProcessPath)
                        ?? AppContext.BaseDirectory;

                    await RunSourceAsync(input, file, IsDebug);
                    currentSourceForError = null;
                }
            }
            else
            {
                Console.WriteLine(header_text);

                VM replVm = new();

                while (true)
                {
                    string? code = ReadMultilineInput();
                    if (code == null) break;
                    if (string.IsNullOrWhiteSpace(code)) continue;

                    try
                    {
                        await RunSourceAsync(code, "<repl>", IsDebug, sharedVm: replVm);
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
        }
    }

    /// <summary>
    /// The PrintTrackedException
    /// </summary>
    /// <param name="ex">The ex<see cref="Exception"/></param>
    /// <param name="phase">The phase<see cref="string"/></param>
    /// <param name="sourceName">The sourceName<see cref="string?"/></param>
    private static void PrintTrackedException(Exception ex, string phase, string? sourceName = null)
    {
        ErrorDiagnostic diag = ErrorDiagnostics.FromException(ex, sourceName);
        string? errorId = ErrorTracker.Track(ex, phase, sourceName);
        Console.WriteLine(ErrorDiagnostics.FormatHeadline(diag));

        string? location = ErrorDiagnostics.FormatLocation(diag);
        if (!string.IsNullOrWhiteSpace(location))
            Console.WriteLine($"at {location}");

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

    /// <summary>
    /// The RunSourceAsync
    /// </summary>
    /// <param name="source">The source<see cref="string"/></param>
    /// <param name="name">The name<see cref="string"/></param>
    /// <param name="debug">The debug<see cref="bool"/></param>
    /// <param name="ct">The ct<see cref="CancellationToken"/></param>
    /// <param name="sharedVm">The sharedVm<see cref="VM?"/></param>
    /// <returns>The <see cref="Task"/></returns>
    private static async Task RunSourceAsync(
        string source,
        string name,
        bool debug = false,
        CancellationToken ct = default,
        VM? sharedVm = null)
    {
        Lexer? lexer = null;
        Parser? parser = null;
        List<Stmt> ast = new();
        List<Instruction> bytecode = new();
        Compiler? compiler = null;

        VM vm = sharedVm ?? new VM();

        lexer = new(name, source);
        parser = new(lexer, vm.LoadPlugin);
        ast = parser.Parse();

        compiler = new(name);
        bytecode = compiler.Compile(ast);

        vm.LoadFunctions(compiler.Functions);
        vm.LoadInstructions(bytecode);

        if (debug)
            PrintInstructions(name, bytecode, compiler.Functions);

        await vm.RunAsync(debug, 0, ct);

        if (debug)
        {
            VM.DebugStream!.Position = 0;
            string lpath = $"{Environment.CurrentDirectory}\\log_file.log";
            using FileStream file = File.Create(lpath);
            VM.DebugStream.CopyTo(file);
            Console.WriteLine($"[DEBUG] log-file created : {lpath}");
        }
    }

    /// <summary>
    /// The PrintInstructions
    /// </summary>
    /// <param name="name">The name<see cref="string"/></param>
    /// <param name="bytecode">The bytecode<see cref="List{Instruction}"/></param>
    /// <param name="_functions">The _functions<see cref="Dictionary{string, FunctionInfo}"/></param>
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

    /// <summary>
    /// The ReadRedirectedInput
    /// </summary>
    /// <returns>The <see cref="string?"/></returns>
    private static string? ReadRedirectedInput()
    {
        while (true)
        {
            string? raw = Console.ReadLine();
            if (raw is null)
                return null;

            raw = raw.TrimStart('\uFEFF');
            string trimmed = raw.Trim();

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
                    Console.WriteLine("Usage : buffer:len");
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
    /// The ReadMultilineInput
    /// </summary>
    /// <returns>The <see cref="string?"/></returns>
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

                if (buffer.Count == 0)
                {
                    if (trimmed.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.Equals("quit", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine();
                        return null;
                    }

                    if (trimmed.Equals("clear", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.Equals("cls", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine();
                        DoClear();
                        continue;
                    }

                    if (trimmed.Equals("debug", StringComparison.OrdinalIgnoreCase))
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

                    if (trimmed.Equals("ansi", StringComparison.OrdinalIgnoreCase))
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

                    if (trimmed.Equals("help", StringComparison.OrdinalIgnoreCase))
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

                    if (trimmed.StartsWith("buffer", StringComparison.OrdinalIgnoreCase))
                    {
                        int restPos = trimmed.IndexOf(":");
                        if (restPos == -1)
                        {
                            Console.WriteLine();
                            Console.WriteLine("Usage : buffer:len");
                            goto abort_buf;
                        }

                        string lenStr = trimmed.Substring(restPos + 1);
                        if (lenStr.Trim() == string.Empty)
                        {
                            Console.WriteLine();
                            Console.WriteLine("Usage : buffer:len");
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
