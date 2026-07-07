using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Extensions.Core;

namespace CFGS_VM.VMCore.Codegen
{
    internal sealed class CompilationContext
    {
        public CompilationContext(string fileName)
        {
            FileName = fileName;
        }

        public string FileName { get; set; }

        public List<Instruction> Instructions { get; } = [];

        public Dictionary<string, FunctionInfo> Functions { get; } = [];

        // Function-body start address → the scope descriptor for that function's call scope (params +
        // body-top-level locals). Built by the descriptor finalization pass; the runtime pre-sizes each
        // call Env from it, keyed by the closure/function address.
        public Dictionary<int, ScopeDescriptor> RootScopeDescriptors { get; } = new();

        // Class simple name → the shape (name→slot) of that class's OWN declared instance fields, so an
        // instance stores its fields in a Value[] instead of a dictionary. A null value marks an ambiguous
        // (duplicated) simple name, for which the runtime falls back to dictionary storage.
        public Dictionary<string, ScopeDescriptor?> ClassFieldShapes { get; } = new(StringComparer.Ordinal);

        public Dictionary<ClassDeclStmt, ClassInfo> ClassInfos { get; } = new();

        public Dictionary<string, ClassDeclStmt> TopLevelClassDecls { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, InterfaceDeclStmt> TopLevelInterfaceDecls { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, ClassDeclStmt> QualifiedClassDecls { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, InterfaceDeclStmt> QualifiedInterfaceDecls { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, EnumDeclStmt> TopLevelEnumDecls { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, EnumDeclStmt> QualifiedEnumDecls { get; } = new(StringComparer.Ordinal);

        public Dictionary<ClassDeclStmt, string> ClassQualifiedPaths { get; } = new();

        public Dictionary<InterfaceDeclStmt, string> InterfaceQualifiedPaths { get; } = new();

        public Dictionary<InterfaceDeclStmt, Dictionary<string, InterfaceMethodDecl>> InterfaceContractCache { get; } = new();

        public Dictionary<InterfaceDeclStmt, Dictionary<string, InterfacePropertyDecl>> InterfacePropertyContractCache { get; } = new();

        public Dictionary<InterfaceDeclStmt, Dictionary<string, InterfaceIndexerDecl>> InterfaceIndexerContractCache { get; } = new();

        public Dictionary<ClassDeclStmt, (HashSet<string> InstanceMembers, HashSet<string> StaticMembers)> ClassMemberSetCache { get; } = new();

        public void Reset()
        {
            Instructions.Clear();
            Functions.Clear();
            RootScopeDescriptors.Clear();
            ClassFieldShapes.Clear();
            ClassInfos.Clear();
            TopLevelClassDecls.Clear();
            TopLevelInterfaceDecls.Clear();
            QualifiedClassDecls.Clear();
            QualifiedInterfaceDecls.Clear();
            TopLevelEnumDecls.Clear();
            QualifiedEnumDecls.Clear();
            ClassQualifiedPaths.Clear();
            InterfaceQualifiedPaths.Clear();
            InterfaceContractCache.Clear();
            InterfacePropertyContractCache.Clear();
            InterfaceIndexerContractCache.Clear();
            ClassMemberSetCache.Clear();
        }
    }
}
