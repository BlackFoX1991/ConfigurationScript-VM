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
    /// Gets a value indicating whether SetCompile
    /// </summary>
    public static bool SetCompile { get; private set; } = false;

    /// <summary>
    /// Gets a value indicating whether BinaryRun
    /// </summary>
    public static bool BinaryRun { get; private set; } = false;

    /// <summary>
    /// Defines the Version
    /// </summary>
    public static readonly string Version = "v2.7.3";

    /// <summary>
    /// Defines the PluginsFolder
    /// </summary>
    public static string PluginsFolder = "plugins";

    /// <summary>
    /// Defines the AnsiMode
    /// </summary>
    public static bool AnsiMode = true;

    /// <summary>
    /// Defines the CLIPath
    /// </summary>
    public static string CLIPath = string.Empty;

    /// <summary>
    /// Defines the logo
    /// </summary>
    private static readonly string header_text = $"[ Configuration-Language ] [ Version {Version} ]\n Enter your code, 'help' or 'exit/quit'\n Visit : https://github.com/BlackFoX1991/ConfigurationScript-VM";

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
  ########        #####++++++++.........::##    [ Commands : -d(ebug) -c(ompile) -b(inary) -p(arams)]
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
    public static void Main(string[] args)
    {
        if (OperatingSystem.IsWindows())
            AnsiConsole.EnableAnsi();

        CLIPath = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        PluginsFolder = CLIPath + "\\" + PluginsFolder;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        List<string> files = new();

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
                                    setCommand = string.Empty;
                                    setMode = false;
                                }
                                break;
                            case "ansi":
                                if (int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int aMode))
                                {
                                    AnsiMode = Convert.ToBoolean(aMode);
                                    Console.WriteLine("Ansi-Mode " + (AnsiMode ? "enabled" : "disabled"));
                                }
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
                        case "-c":
                        case "-compile":
                            SetCompile = true;
                            break;
                        case "-b":
                        case "-binary":
                            BinaryRun = true;
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
                    string input = File.ReadAllText(file);
                    if (file.EndsWith(".cfb", StringComparison.OrdinalIgnoreCase))
                    {
                        BinaryRun = true;
                        SetCompile = false;
                    }
                    else
                        BinaryRun = false;

                    Environment.CurrentDirectory = Path.GetDirectoryName(Path.GetFullPath(file)) ?? Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
                    RunSource(input, file, IsDebug, BinaryRun);

                }
            }
            else
            {
                Console.WriteLine(header_text);
                while (true)
                {
                    string? code = ReadMultilineInput();
                    if (code == null) break;
                    if (string.IsNullOrWhiteSpace(code)) continue;

                    try
                    {
                        SetCompile = false;
                        RunSource(code, "<repl>", IsDebug, false);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"{ex.GetType().Name} : {ex.Message}");

                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{ex.GetType().Name} : {ex.Message}");
        }
    }

    /// <summary>
    /// The RunSourceAsync
    /// </summary>
    /// <param name="source">The source<see cref="string"/></param>
    /// <param name="name">The name<see cref="string"/></param>
    /// <param name="debug">The debug<see cref="bool"/></param>
    /// <param name="binaryRun">The binaryRun<see cref="bool"/></param>
    /// <param name="ct">The ct<see cref="CancellationToken"/></param>
    /// <returns>The <see cref="Task"/></returns>
    private static async Task RunSourceAsync(
    string source,
    string name,
    bool debug = false,
    bool binaryRun = false,
    CancellationToken ct = default)
    {
        Lexer? lexer = null;
        Parser? parser = null;
        List<Stmt> ast = new();
        List<Instruction> bytecode = new();
        Compiler? compiler = null;
        VM vm = new();

        if (!binaryRun)
        {
            lexer = new(name, source);
            parser = new(lexer);
            ast = parser.Parse();

            compiler = new(name);
            bytecode = compiler.Compile(ast);

            if (!SetCompile)
            {
                vm.LoadPluginsFrom(PluginsFolder);
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
            else
            {
                string outPath = Path.ChangeExtension(name, ".cfb");
                CFSBinary.Save(outPath, bytecode, compiler.Functions);
                Console.WriteLine($"Compiled script '{name}' -> '{outPath}'");
            }
        }
        else
        {
            (bytecode, Dictionary<string, FunctionInfo>? funcs) = CFSBinary.Load(name);
            vm.LoadInstructions(bytecode);
            vm.LoadFunctions(funcs);
            vm.LoadPluginsFrom(PluginsFolder);

            if (debug)
                PrintInstructions(name, bytecode, vm.Functions);

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
    /// The RunSource
    /// </summary>
    /// <param name="source">The source<see cref="string"/></param>
    /// <param name="name">The name<see cref="string"/></param>
    /// <param name="debug">The debug<see cref="bool"/></param>
    /// <param name="binaryRun">The binaryRun<see cref="bool"/></param>
    private static void RunSource(string source, string name, bool debug = false, bool binaryRun = false)
        => RunSourceAsync(source, name, debug, binaryRun).GetAwaiter().GetResult();

    /// <summary>
    /// The ReadMultilineInput
    /// </summary>
    /// <returns>The <see cref="string?"/></returns>
    private static string? ReadMultilineInput()
    {
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
