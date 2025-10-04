using CFGS_VM.Analytic;
using CFGS_VM.VMCore;
using System.Globalization;
using System.Text;

/// <summary>
/// Defines the <see cref="Program" />
/// </summary>
public class Program
{
    /// <summary>
    /// The Main
    /// </summary>
    /// <param name="args">The args<see cref="string[]"/></param>
    /// 

    public static bool IsDebug { get; private set; } = false;

    private static string logo = @"
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
     ############     #.:...........::###   [ Configuration-Language ]   
           ############..........:::###     [ REPL - Enter your code or use exit/quit to leave ]   
            ###########......::::####          
            *###     ##:::::#####              
                      #####                    
";
    public static void Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        var files = new List<string>();

        foreach (var arg in args)
        {
            switch (arg)
            {
                case "-d":
                case "--debug":
                    IsDebug = true;
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
                foreach (var file in files)
                {
                    if (!File.Exists(file))
                    {
                        Console.WriteLine($"Script-file not found '{file}'.");
                        continue;
                    }

                    string input = File.ReadAllText(file);
                    RunSource(input, file, IsDebug);
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
                        RunSource(code, "<repl>", IsDebug);
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
    private static void RunSource(string source, string name, bool debug = false)
    {
        var lexer = new Lexer(name, source);
        var parser = new Parser(lexer);
        var ast = parser.Parse();

        var compiler = new Compiler(name);
        var bytecode = compiler.Compile(ast);

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
                var ins = bytecode[idx];

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
                foreach (var f in compiler._functions)
                    Console.WriteLine(f.Key + " -> " + f.Value);
                Console.WriteLine();
            }
        }

        var vm = new VM();
        vm.LoadFunctions(compiler._functions);
        vm.LoadInstructions(bytecode);
        vm.Run(debug);
        if (debug)
        {
            vm.DebugStream.Position = 0;
            using var file = File.Create("log_file.log");
            vm.DebugStream.CopyTo(file);
        }
    }



    /// <summary>
    /// The ReadMultilineInput
    /// </summary>
    /// <returns>The <see cref="string?"/></returns>
    private static string? ReadMultilineInput()
    {
        var buffer = new List<string>();
        string prompt = "> ";

        while (true)
        {
            Console.Write(prompt);
            string? line = Console.ReadLine();
            if (line == null) return null;

            string trimmed = line.Trim();

            // Single-word commands nur wenn noch kein Block begonnen hat
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
                    continue; // neuer Prompt, kein Buffer gestartet
                }

                if (trimmed.Equals("debug", StringComparison.OrdinalIgnoreCase))
                {
                    IsDebug = !IsDebug;
                    Console.WriteLine($"Debug mode is now {(IsDebug ? "Enabled" : "Disabled")}");
                    continue; // noch kein Buffer gestartet
                }
            }

            // Ab hier gehört die Zeile zum aktuellen Block
            buffer.Add(line);

            // Mit der aktuellen Zeile prüfen
            string joined = string.Join("\n", buffer);

            // Abschlussregeln: Klammern balanciert + heuristische Endung ODER Leerzeile
            bool looksTerminated =
                trimmed.EndsWith(";") ||
                trimmed.EndsWith("}") ||
                trimmed.Length == 0;

            if (BracketsBalanced(joined) && looksTerminated)
                return joined;

            // Sonst: wir sind mitten im Block → Folgeprompt anzeigen
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
