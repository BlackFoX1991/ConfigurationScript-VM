using CFGS_VM.VMCore.Codegen;
using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Extensions.Core;

namespace CFGS_VM.VMCore
{
    public partial class VM
    {
        private sealed class VmState
        {
            /// <summary>
            /// Gets the operand stack.
            /// </summary>
            public ValueStack Stack { get; } = new();

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
            /// Gets or sets the function-address → call-scope descriptor map used to pre-size call Envs.
            /// Replaced on each script load; empty when a script carries no descriptors (e.g. from .cfb).
            /// </summary>
            public IReadOnlyDictionary<int, ScopeDescriptor> RootScopeDescriptors { get; set; }
                = new Dictionary<int, ScopeDescriptor>();

            /// <summary>
            /// Class simple name → instance field shape (null value = ambiguous name → dictionary storage).
            /// Replaced on each script load; empty for classes without a shape (e.g. from .cfb).
            /// </summary>
            public IReadOnlyDictionary<string, ScopeDescriptor?> ClassFieldShapes { get; set; }
                = new Dictionary<string, ScopeDescriptor?>(StringComparer.Ordinal);

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
