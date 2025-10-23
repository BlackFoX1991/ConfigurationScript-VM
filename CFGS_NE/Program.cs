using CFGS_VM.Analytic.Core;
using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore;
using CFGS_VM.VMCore.Extensions;
using CFGS_VM.VMCore.Extensions.Core;
using System.Globalization;
using System.Text;

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
    public static readonly string Version = "v2.4.0";

    /// <summary>
    /// Defines the PluginsFolder
    /// </summary>
    public static string PluginsFolder = "plugins";

    /// <summary>
    /// Defines the CLIPath
    /// </summary>
    public static string CLIPath = string.Empty;

    /// <summary>
    /// Defines the logo
    /// </summary>
    private static readonly string logo = $@"
=====================================================================================================
                      #####                    
            ####     ##:::::+#####             
           ############......::::=###           [ Configuration-Language ] [ Version {Version}      ]
           ############..........:::*##         [ Enter your code or use the following commands     ]     
     ############     #.............:::##       [ cls/ clear to clear the console                   ]  
    ##########        #...............::##      [ debug to enable/disable the debug mode            ]
      ######         %#+++.............::##     =====================================================
      #####        ####+++++++.........:::##    [ Commands : -d(ebug) -c(ompile) -b(inary) -p(arams)]
  ########        #####++++++++.........::##    https://github.com/BlackFoX1991/ConfigurationScript-VM
  ########        #####+++++++++........::##   
      #####        ####++++++++*.......::-##    [--------------REPL INFO----------------------------]   
      ######        *##++++++**........::##     [ Use Ctrl+Enter to run the entered code            ]
    ##########        #******........:::##      [ Use Ctrl+Backspace to clear the code              ]
     ############     #.:...........::###       [ Enter $L <Line> <Content> to edit a specific Line ]
           ############..........:::###         [ Use Arrow Up/Down to trigger $L command           ]
            ###########......::::####           [---------------------------------------------------]
            *###     ##:::::#####           
                      #####                  
=====================================================================================================";

    /// <summary>
    /// The Main
    /// </summary>
    /// <param name="args">The args<see cref="string[]"/></param>
    public static void Main(string[] args)
    {
        CLIPath = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        PluginsFolder = CLIPath + "\\" + PluginsFolder;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        List<string> files = new();

        bool setMode = false;
        string setCommand = string.Empty;
        foreach (string arg in args)
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
                Console.WriteLine(logo);
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

        int? lSelect = null;

        void WritePrompt() => Console.Write(prompt);

        void SetCurrentLine(string text)
        {
            while (line.Length > 0) { Console.Write("\b \b"); line.Length--; }
            line.Append(text);
            Console.Write(text);
        }

        void RedrawAll()
        {
            Console.Clear();
            Console.WriteLine(logo);
            for (int i = 0; i < buffer.Count; i++)
            {
                string pr = (i == 0) ? "> " : "> ";
                Console.Write(pr);
                Console.WriteLine(buffer[i]);
            }
            prompt = (buffer.Count == 0) ? "> " : "> ";
            WritePrompt();
        }

        void DoClear()
        {
            buffer.Clear();
            line.Clear();
            lSelect = null;
            RedrawAll();
        }

        WritePrompt();

        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter && (key.Modifiers & ConsoleModifiers.Control) != 0)
            {
                Console.WriteLine();
                if (line.Length > 0)
                    buffer.Add(line.ToString());
                return string.Join("\n", buffer);
            }

            if (key.Key == ConsoleKey.Backspace && (key.Modifiers & ConsoleModifiers.Control) != 0)
            {
                DoClear();
                continue;
            }

            if (key.Key == ConsoleKey.UpArrow)
            {
                if (buffer.Count > 0)
                {
                    if (lSelect is null) lSelect = buffer.Count;
                    lSelect = Math.Max(1, lSelect.Value - 1);
                    SetCurrentLine($"$L {lSelect.Value} ");
                }
                continue;
            }

            if (key.Key == ConsoleKey.DownArrow)
            {
                if (lSelect is null) lSelect = Math.Max(1, buffer.Count);
                lSelect = Math.Min(buffer.Count + 1, lSelect.Value + 1);
                SetCurrentLine($"$L {lSelect.Value} ");
                continue;
            }

            if (key.Key == ConsoleKey.Enter)
            {
                string current = line.ToString();
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
                            RedrawAll();
                            continue;
                        }
                        else if (idx == buffer.Count)
                        {
                            buffer.Add(newContent);
                            line.Clear();
                            lSelect = null;
                            RedrawAll();
                            continue;
                        }
                        else
                        {
                            Console.WriteLine();
                            Console.WriteLine($"# Zeilennummer {oneBased} ist außerhalb des gültigen Bereichs (1..{buffer.Count + 1}).");
                            prompt = (buffer.Count == 0) ? "> " : "> ";
                            WritePrompt();
                            line.Clear();
                            lSelect = null;
                            continue;
                        }
                    }
                    else
                    {
                        Console.WriteLine();
                        Console.WriteLine("# Invalid usage : $L <Zeilennummer> <Inhalt>");
                        prompt = (buffer.Count == 0) ? "> " : "> ";
                        WritePrompt();
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
                        WritePrompt();
                        continue;
                    }
                }

                buffer.Add(current);
                line.Clear();
                lSelect = null;
                Console.WriteLine();
                prompt = "> ";
                WritePrompt();
                continue;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (line.Length > 0)
                {
                    line.Length--;
                    Console.Write("\b \b");
                }
                lSelect = null;
                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                line.Append(key.KeyChar);
                Console.Write(key.KeyChar);
                lSelect = null;
            }
        }
    }
}
