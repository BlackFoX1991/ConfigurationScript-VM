using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore.Codegen;
using CFGS_VM.VMCore.Extensions;
using CFGS_VM.VMCore.Extensions.Core;

namespace CFGS_VM.VMCore
{
    public partial class Compiler(string fname)
    {
        private readonly BytecodeEmissionContext _emission = new();

        private readonly CompilationContext _context = new(fname);

        /// <summary>
        /// Gets or sets the FileName
        /// </summary>
        public string FileName
        {
            get => _context.FileName;
            set => _context.FileName = value;
        }

        /// <summary>
        /// Gets the Functions
        /// </summary>
        public Dictionary<string, FunctionInfo> Functions => _context.Functions;

        /// <summary>
        /// Function-address → call-scope descriptor, produced by <see cref="FinalizeScopeDescriptors"/>.
        /// </summary>
        public Dictionary<int, ScopeDescriptor> RootScopeDescriptors => _context.RootScopeDescriptors;

        public List<Instruction> Compile(List<Stmt> program)
        {
            try
            {
                return new CompilationPipeline().Compile(this, program);
            }
            catch (CompilerException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new CompilerException(
                    $"internal compiler error: {ex.Message}",
                    0, 0, "<compiler>");
            }
        }

    }
}

