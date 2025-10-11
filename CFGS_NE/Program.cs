using CFGS_VM.Analytic;
using CFGS_VM.VMCore;
using System.Globalization;

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
    public static readonly string Version = "v1.8.6";

    /// <summary>
    /// Defines the PluginsFolder
    /// </summary>
    public static readonly string PluginsFolder = "plugins";

    /// <summary>
    /// Defines the logo
    /// </summary>
    private static readonly string logo = $@"                         
                      #####                    
            ####     ##:::::+#####             
           ############......::::=###          
           ############..........:::*##        
     ############     #.............:::##      
    ##########        #...............::##     
      ######         %#+++.............::##    
      #####        ####+++++++.........:::##   
  ########        #####++++++++.........::##   
  ########        #####+++++++++........::##   
      #####        ####++++++++*.......::-##   
      ######        *##++++++**........::##    
    ##########        #******........:::##     
     ############     #.:...........::###   [ Configuration-Language ] [ Version {Version} ]  
           ############..........:::###     [ REPL - Enter your code or use exit/quit to leave ]   
            ###########......::::####          
            *###     ##:::::#####              
                      #####                    
";

    /// <summary>
    /// The Main
    /// </summary>
    /// <param name="args">The args<see cref="string[]"/></param>
    public static void Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        List<string> files = new();

        foreach (string arg in args)
        {
            switch (arg)
            {
                case "-d":
                case "--debug":
                    IsDebug = true;
                    break;
                case "-c":
                    SetCompile = true;
                    break;

                case "-b":
                    BinaryRun = true;
                    break;

                default:
                    files.Add(arg);
                    break;
            }

        }

        try
        {
            if (files.Count > 0)
            {
                foreach (string file in files)
                {
                    if (!File.Exists(file))
                    {
                        Console.WriteLine($"Script-file not found '{file}'.");
                        continue;
                    }

                    string input = File.ReadAllText(file);

                    if (file.EndsWith(".cfb", StringComparison.OrdinalIgnoreCase))
                    {
                        BinaryRun = true;
                        SetCompile = false;
                    }
                    else
                        BinaryRun = false;

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
    /// The RunSource
    /// </summary>
    /// <param name="source">The source<see cref="string"/></param>
    /// <param name="name">The name<see cref="string"/></param>
    /// <param name="debug">The debug<see cref="bool"/></param>
    /// <param name="binaryRun">The binaryRun<see cref="bool"/></param>
    private static void RunSource(string source, string name, bool debug = false, bool binaryRun = false)
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

            string baseDir = AppContext.BaseDirectory;
            string pluginDir = Path.Combine(baseDir, PluginsFolder);
            vm.LoadPluginsFrom(pluginDir);
            vm.LoadFunctions(compiler._functions);
            vm.LoadInstructions(bytecode);
            if (debug)
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
                    string operand = (ins.Operand?.ToString() ?? "null").PadRight(operandWidth);

                    Console.WriteLine($"| {lineCol} | {instrNum} | {opCode} | {operand} |");
                }

                Console.WriteLine("=== END ===");

                if (compiler._functions.Count > 0)
                {
                    Console.WriteLine("=== Functions ===");
                    foreach (KeyValuePair<string, CFGS_VM.VMCore.Extension.FunctionInfo> f in compiler._functions)
                        Console.WriteLine(f.Key + " -> " + f.Value);
                    Console.WriteLine();
                }
            }
            vm.Run(debug);
            if (SetCompile)
                CFGS_VM.VMCore.IO.CFSBinary.Save(name + ".cfb", bytecode, compiler._functions);
            if (debug)
            {
                vm.DebugStream.Position = 0;
                using FileStream file = File.Create("log_file.log");
                vm.DebugStream.CopyTo(file);
            }
        }
        else
        {
            (bytecode, Dictionary<string, CFGS_VM.VMCore.Extension.FunctionInfo>? funcs) = CFGS_VM.VMCore.IO.CFSBinary.Load(name);
            vm.LoadInstructions(bytecode);
            vm.LoadFunctions(funcs);
            string baseDir = AppContext.BaseDirectory;
            string pluginDir = Path.Combine(baseDir, PluginsFolder);
            vm.LoadPluginsFrom(pluginDir);
            if (debug)
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
                    string operand = (ins.Operand?.ToString() ?? "null").PadRight(operandWidth);

                    Console.WriteLine($"| {lineCol} | {instrNum} | {opCode} | {operand} |");
                }

                Console.WriteLine("=== END ===");

                if (vm._functions.Count > 0)
                {
                    Console.WriteLine("=== Functions ===");
                    foreach (KeyValuePair<string, CFGS_VM.VMCore.Extension.FunctionInfo> f in vm._functions)
                        Console.WriteLine(f.Key + " -> " + f.Value);
                    Console.WriteLine();
                }
            }
            vm.Run(debug);
            if (debug)
            {
                vm.DebugStream.Position = 0;
                using FileStream file = File.Create("log_file.log");
                vm.DebugStream.CopyTo(file);
            }

        }
    }

    /// <summary>
    /// The ReadMultilineInput
    /// </summary>
    /// <returns>The <see cref="string?"/></returns>
    private static string? ReadMultilineInput()
    {
        List<string> buffer = new();
        string prompt = "> ";

        while (true)
        {
            Console.Write(prompt);
            string? line = Console.ReadLine();
            if (line == null) return null;

            string trimmed = line.Trim();

            if (buffer.Count == 0)
            {
                if (trimmed.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Equals("quit", StringComparison.OrdinalIgnoreCase))
                    return null;

                if (trimmed.Equals("clear", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Equals("cls", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Clear();
                    Console.WriteLine(logo);
                    continue;
                }

                if (trimmed.Equals("debug", StringComparison.OrdinalIgnoreCase))
                {
                    IsDebug = !IsDebug;
                    Console.WriteLine($"Debug mode is now {(IsDebug ? "Enabled" : "Disabled")}");
                    continue;
                }
            }

            buffer.Add(line);

            string joined = string.Join("\n", buffer);

            bool looksTerminated =
                trimmed.EndsWith(";") ||
                trimmed.EndsWith("}") ||
                trimmed.Length == 0;

            if (BracketsBalanced(joined) && looksTerminated)
                return joined;

            prompt = "... ";
        }
    }

    /// <summary>
    /// The BracketsBalanced
    /// </summary>
    /// <param name="code">The code<see cref="string"/></param>
    /// <returns>The <see cref="bool"/></returns>
    private static bool BracketsBalanced(string code)
    {
        int round = 0, curly = 0;
        foreach (char c in code)
        {
            if (c == '(') round++;
            if (c == ')') round--;
            if (c == '{') curly++;
            if (c == '}') curly--;
        }
        return round <= 0 && curly <= 0;
    }
}
