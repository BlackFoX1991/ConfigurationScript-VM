using System.Globalization;
using CFGS_VM.VMCore;

/// <summary>
/// Defines the <see cref="Program" />
/// </summary>
public class Program
{
    /// <summary>
    /// The Main
    /// </summary>
    /// <param name="args">The args<see cref="string[]"/></param>
    public static void Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        bool debug = false;
        var files = new List<string>();

        foreach (var arg in args)
        {
            if (arg == "-d" || arg == "--debug")
                debug = true;
            else
                files.Add(arg);
        }

        try
        {
            if (files.Count > 0)
            {
                foreach (var file in files)
                {
                    if (!File.Exists(file))
                    {
                        Console.WriteLine($"Datei nicht gefunden: {file}");
                        continue;
                    }

                    string input = File.ReadAllText(file);
                    RunSource(input, file, debug);
                }
            }
            else
            {
                Console.WriteLine("CFGS VM REPL ( exit or quit to close the Application )");
                while (true)
                {
                    string? code = ReadMultilineInput();
                    if (code == null) break;
                    if (string.IsNullOrWhiteSpace(code)) continue;

                    try
                    {
                        RunSource(code, "<repl>", debug);
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
    private static void RunSource(string source, string name, bool debug)
    {
        var lexer = new Lexer(name,source);
        var parser = new Parser(lexer);
        var ast = parser.Parse();

        var compiler = new Compiler(name);
        var bytecode = compiler.Compile(ast);

        if (debug)
        {
            Console.WriteLine($"=== INSTRUCTIONS ({name}) ===");

            // Berechne Spaltenbreiten dynamisch
            // Berechnung der Spaltenbreiten zur Laufzeit
            int opCodeWidth = Math.Max(bytecode.Max(i => i.Code.ToString().Length), "OpCode".Length);
            int operandWidth = Math.Max(bytecode.Max(i => i.Operand?.ToString()?.Length ?? 4), "Operand".Length);

            // Tabellenkopf
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
        vm.Run(name, bytecode);
        
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

            if (buffer.Count == 0 && (trimmed == "exit" || trimmed == "quit"))
                return null;

            buffer.Add(line);

            string joined = string.Join("\n", buffer);
            if (BracketsBalanced(joined) && (trimmed.EndsWith(";") || trimmed.EndsWith("}") || trimmed == ""))
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
