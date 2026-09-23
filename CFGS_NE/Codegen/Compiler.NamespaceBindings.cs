using CFGS_VM.Analytic.Semantics;
using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore.Command;

namespace CFGS_VM.VMCore
{
    public partial class Compiler
    {
        /// <summary>
        /// Recognizes the block a namespace declaration lowers to and returns its qualified path.
        /// </summary>
        internal static bool TryGetNamespaceScopePath(BlockStmt block, out string namespacePath)
        {
            namespacePath = block.IsNamespaceScope ? block.NamespacePath ?? string.Empty : string.Empty;
            return namespacePath.Length > 0;
        }

        /// <summary>
        /// Returns the name a declaration binds: its namespace member binding inside a namespace block,
        /// otherwise its own name.
        /// </summary>
        internal string GetDeclarationBindingName(Stmt declaration, string name)
            => _context.DeclarationBindingNames.TryGetValue(declaration, out string? bindingName) ? bindingName : name;

        /// <summary>
        /// A reference the resolver bound to a global binding (a namespace member, or an export reached
        /// through a namespace import alias) is read and written in the global scope directly, so no local,
        /// capture, or implicit class member of the same name can hide it.
        /// </summary>
        private static bool IsResolvedGlobalReference(VarExpr v)
            => v.SourceExpr is not null || NamespaceRuntimeBindingNames.IsMemberBindingName(v.Name);

        /// <summary>
        /// Loads a name the compiler itself references (a class loading its own binding, a foreach or
        /// destructuring target): a namespace member binding is read from the global scope directly.
        /// </summary>
        private void EmitNameLoad(string name, Node node)
        {
            OpCode code = NamespaceRuntimeBindingNames.IsMemberBindingName(name) ? OpCode.LOAD_GLOBAL : OpCode.LOAD_VAR;
            _insns.Add(new Instruction(code, name, node.Line, node.Col, node.OriginFile));
        }

        private void EmitNameStore(string name, Node node)
        {
            OpCode code = NamespaceRuntimeBindingNames.IsMemberBindingName(name) ? OpCode.STORE_GLOBAL : OpCode.STORE_VAR;
            _insns.Add(new Instruction(code, name, node.Line, node.Col, node.OriginFile));
        }
    }
}
