using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore.Codegen;
using CFGS_VM.VMCore.Extensions;
using CFGS_VM.VMCore.Extensions.Core;

namespace CFGS_VM.VMCore
{
    /// <summary>
    /// Defines the <see cref="Compiler" />
    /// </summary>
    public partial class Compiler(string fname)
    {
        /// <summary>
        /// Defines the emission context
        /// </summary>
        private readonly BytecodeEmissionContext _emission = new();

        /// <summary>
        /// Defines the compilation context
        /// </summary>
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
        /// The Compile
        /// </summary>
        /// <param name="program">The program<see cref="List{Stmt}"/></param>
        /// <returns>The <see cref="List{Instruction}"/></returns>
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

