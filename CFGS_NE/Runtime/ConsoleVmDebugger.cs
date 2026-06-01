using CFGS_VM.VMCore.Debugging;
using System.Globalization;

namespace CFGS_VM.VMCore
{
    public sealed class ConsoleVmDebugger : IVmDebugger
    {
        private enum RunMode
        {
            Step,
            Continue,
            Next
        }

        private readonly TextReader _input;
        private readonly TextWriter _output;
        private readonly List<LineBreakpoint> _lineBreakpoints = [];
        private readonly HashSet<int> _ipBreakpoints = [];
        private RunMode _mode = RunMode.Step;
        private int _nextDepth;
        private bool _stopped;

        public ConsoleVmDebugger(TextReader? input = null, TextWriter? output = null)
        {
            _input = input ?? Console.In;
            _output = output ?? Console.Out;
        }

        public VmDebuggerControl BeforeInstruction(VM vm, VmDebugContext context)
        {
            if (!ShouldStop(context))
                return VmDebuggerControl.Continue;

            PrintLocation(vm, context);

            while (true)
            {
                _output.Write("dbg> ");
                string? line = _input.ReadLine();
                if (line is null)
                {
                    _mode = RunMode.Continue;
                    return VmDebuggerControl.Continue;
                }

                string command = line.Trim();
                if (command.Length == 0)
                    command = "step";

                if (ExecuteCommand(vm, context, command, out VmDebuggerControl control))
                    return control;
            }
        }

        public void OnException(VM vm, VmDebugContext context, Exception exception)
        {
            _output.WriteLine($"[dbg] exception at {FormatLocation(context.Instruction)}: {exception.Message}");
        }

        private bool ShouldStop(VmDebugContext context)
        {
            if (_stopped)
                return false;

            if (_ipBreakpoints.Contains(context.Ip) || MatchesLineBreakpoint(context.Instruction))
                return true;

            return _mode switch
            {
                RunMode.Step => true,
                RunMode.Next => context.CallStackDepth <= _nextDepth,
                _ => false
            };
        }

        private bool ExecuteCommand(VM vm, VmDebugContext context, string command, out VmDebuggerControl control)
        {
            control = VmDebuggerControl.Continue;
            string[] parts = command.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            string op = parts[0].ToLowerInvariant();
            string arg = parts.Length > 1 ? parts[1].Trim() : string.Empty;

            switch (op)
            {
                case "s":
                case "step":
                    _mode = RunMode.Step;
                    return true;

                case "n":
                case "next":
                    _mode = RunMode.Next;
                    _nextDepth = context.CallStackDepth;
                    return true;

                case "c":
                case "cont":
                case "continue":
                    _mode = RunMode.Continue;
                    return true;

                case "q":
                case "quit":
                case "exit":
                    _stopped = true;
                    control = VmDebuggerControl.Stop;
                    return true;

                case "b":
                case "break":
                    AddBreakpoint(context, arg);
                    return false;

                case "breaks":
                case "breakpoints":
                    PrintBreakpoints();
                    return false;

                case "clear":
                    ClearBreakpoint(arg);
                    return false;

                case "locals":
                    PrintVariables("locals", vm.GetDebugLocalsSnapshot(), vm);
                    return false;

                case "globals":
                    PrintVariables("globals", vm.GetDebugGlobalsSnapshot(), vm);
                    return false;

                case "stack":
                    PrintStack(vm);
                    return false;

                case "bt":
                case "where":
                case "callstack":
                    PrintCallStack(vm);
                    return false;

                case "p":
                case "print":
                    PrintVariable(vm, arg);
                    return false;

                case "ip":
                    _output.WriteLine($"ip = {context.Ip}");
                    return false;

                case "help":
                case "h":
                case "?":
                    PrintHelp();
                    return false;

                default:
                    _output.WriteLine($"unknown debugger command '{op}'. Type 'help'.");
                    return false;
            }
        }

        private void AddBreakpoint(VmDebugContext context, string arg)
        {
            if (string.IsNullOrWhiteSpace(arg))
            {
                _output.WriteLine("usage: break <line>|<file>:<line>|ip <number>");
                return;
            }

            string[] parts = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[0].Equals("ip", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ip) && ip >= 0)
                {
                    _ipBreakpoints.Add(ip);
                    _output.WriteLine($"breakpoint added at ip {ip}");
                }
                else
                {
                    _output.WriteLine("invalid ip breakpoint");
                }
                return;
            }

            if (TryParseLineBreakpoint(context, arg, out LineBreakpoint bp))
            {
                _lineBreakpoints.Add(bp);
                string file = bp.File is null ? "*" : bp.File;
                _output.WriteLine($"breakpoint added at {file}:{bp.Line}");
                return;
            }

            _output.WriteLine("invalid breakpoint");
        }

        private bool TryParseLineBreakpoint(VmDebugContext context, string raw, out LineBreakpoint breakpoint)
        {
            breakpoint = default;
            string file = context.Instruction.OriginFile;
            string linePart = raw;

            int sep = raw.LastIndexOf(':');
            if (sep >= 0)
            {
                file = raw[..sep].Trim();
                linePart = raw[(sep + 1)..].Trim();
            }

            if (!int.TryParse(linePart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int line) || line <= 0)
                return false;

            breakpoint = new LineBreakpoint(string.IsNullOrWhiteSpace(file) ? null : file, line);
            return true;
        }

        private void ClearBreakpoint(string arg)
        {
            if (string.IsNullOrWhiteSpace(arg) || arg.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                _lineBreakpoints.Clear();
                _ipBreakpoints.Clear();
                _output.WriteLine("all breakpoints cleared");
                return;
            }

            if (arg.StartsWith("ip ", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(arg[3..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int ip))
            {
                _ipBreakpoints.Remove(ip);
                _output.WriteLine($"breakpoint cleared at ip {ip}");
                return;
            }

            _output.WriteLine("usage: clear all|ip <number>");
        }

        private void PrintBreakpoints()
        {
            if (_ipBreakpoints.Count == 0 && _lineBreakpoints.Count == 0)
            {
                _output.WriteLine("no breakpoints");
                return;
            }

            foreach (int ip in _ipBreakpoints.Order())
                _output.WriteLine($"ip {ip}");

            foreach (LineBreakpoint bp in _lineBreakpoints)
                _output.WriteLine($"{(bp.File is null ? "*" : bp.File)}:{bp.Line}");
        }

        private void PrintLocation(VM vm, VmDebugContext context)
        {
            Instruction instr = context.Instruction;
            _output.WriteLine(
                $"[dbg] ip={context.Ip} {vm.FormatDebugInstruction(instr)} @ {FormatLocation(instr)} stack={context.StackCount} scopes={context.ScopeCount} calls={context.CallStackDepth}");
        }

        private static string FormatLocation(Instruction instr)
            => $"{instr.OriginFile}:{instr.Line}:{instr.Col}";

        private void PrintVariables(string title, IReadOnlyList<VmDebugVariable> variables, VM vm)
        {
            _output.WriteLine($"{title}:");
            if (variables.Count == 0)
            {
                _output.WriteLine("  <empty>");
                return;
            }

            foreach (VmDebugVariable variable in variables)
            {
                string marker = variable.IsConst ? "const " : string.Empty;
                _output.WriteLine($"  {marker}{variable.Name} = {vm.FormatDebugValue(variable.Value)}");
            }
        }

        private void PrintStack(VM vm)
        {
            IReadOnlyList<object?> stack = vm.GetDebugStackSnapshot();
            if (stack.Count == 0)
            {
                _output.WriteLine("stack: <empty>");
                return;
            }

            _output.WriteLine("stack:");
            for (int i = 0; i < stack.Count; i++)
                _output.WriteLine($"  [{i}] {vm.FormatDebugValue(stack[i])}");
        }

        private void PrintCallStack(VM vm)
        {
            IReadOnlyList<VmDebugFrame> frames = vm.GetDebugCallStackSnapshot();
            if (frames.Count == 0)
            {
                _output.WriteLine("callstack: <empty>");
                return;
            }

            _output.WriteLine("callstack:");
            foreach (VmDebugFrame frame in frames)
            {
                _output.WriteLine($"  #{frame.Index} ret={frame.ReturnIp} base={frame.BaseScopeDepth} at {frame.OriginFile}:{frame.Line}:{frame.Col}");
            }
        }

        private void PrintVariable(VM vm, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                _output.WriteLine("usage: print <name>");
                return;
            }

            if (vm.TryGetDebugVariable(name, out object? value))
                _output.WriteLine($"{name} = {vm.FormatDebugValue(value)}");
            else
                _output.WriteLine($"undefined variable '{name}'");
        }

        private void PrintHelp()
        {
            _output.WriteLine("commands:");
            _output.WriteLine("  step|s               execute one VM instruction");
            _output.WriteLine("  next|n               step over nested calls");
            _output.WriteLine("  continue|c           run until a breakpoint or program end");
            _output.WriteLine("  break|b <line>       break at a source line in the current file");
            _output.WriteLine("  break <file>:<line>  break at a source location");
            _output.WriteLine("  break ip <number>    break at an instruction pointer");
            _output.WriteLine("  breaks               list breakpoints");
            _output.WriteLine("  clear all|ip <n>     clear breakpoints");
            _output.WriteLine("  locals|globals       show variables");
            _output.WriteLine("  stack|callstack      show VM stack or call stack");
            _output.WriteLine("  print|p <name>       show a variable");
            _output.WriteLine("  ip                   show current instruction pointer");
            _output.WriteLine("  quit|q               stop execution");
        }

        private bool MatchesLineBreakpoint(Instruction instruction)
        {
            foreach (LineBreakpoint bp in _lineBreakpoints)
            {
                if (bp.Line != instruction.Line)
                    continue;

                if (bp.File is null || PathEquals(bp.File, instruction.OriginFile))
                    return true;
            }

            return false;
        }

        private static bool PathEquals(string left, string right)
        {
            if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
                return true;

            string leftName = Path.GetFileName(left);
            string rightName = Path.GetFileName(right);
            return leftName.Length > 0 &&
                   string.Equals(leftName, rightName, StringComparison.OrdinalIgnoreCase);
        }

        private readonly record struct LineBreakpoint(string? File, int Line);
    }
}
