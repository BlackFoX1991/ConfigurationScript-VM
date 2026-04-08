using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Extensions.Core;

namespace CFGS_VM.VMCore
{
    public partial class VM
    {
        /// <summary>
        /// Defines the mutable runtime state carried across VM execution.
        /// </summary>
        private sealed class VmState
        {
            /// <summary>
            /// Gets the operand stack.
            /// </summary>
            public Stack<object> Stack { get; } = new();

            /// <summary>
            /// Gets the lexical scope chain.
            /// </summary>
            public List<Env> Scopes { get; } = new() { new Env(null) };

            /// <summary>
            /// Gets the function table.
            /// </summary>
            public Dictionary<string, FunctionInfo> Functions { get; } = [];

            /// <summary>
            /// Gets the address-based function lookup.
            /// </summary>
            public Dictionary<int, FunctionInfo> FunctionsByAddress { get; } = new();

            /// <summary>
            /// Gets the call stack.
            /// </summary>
            public Stack<CallFrame> CallStack { get; } = new();

            /// <summary>
            /// Gets the try handler stack.
            /// </summary>
            public Stack<TryHandler> TryHandlers { get; } = new();

            /// <summary>
            /// Gets or sets the loaded instruction list.
            /// </summary>
            public List<Instruction>? Program { get; set; }

            /// <summary>
            /// Gets or sets the pending await task.
            /// </summary>
            public Task<object?>? AwaitTask { get; set; }

            /// <summary>
            /// Gets or sets the await resume ip.
            /// </summary>
            public int AwaitResumeIp { get; set; }
        }
    }
}
