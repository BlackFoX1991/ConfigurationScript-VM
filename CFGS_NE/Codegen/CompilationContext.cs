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

        public Dictionary<ClassDeclStmt, ClassInfo> ClassInfos { get; } = new();

        public Dictionary<string, ClassDeclStmt> TopLevelClassDecls { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, InterfaceDeclStmt> TopLevelInterfaceDecls { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, ClassDeclStmt> QualifiedClassDecls { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, InterfaceDeclStmt> QualifiedInterfaceDecls { get; } = new(StringComparer.Ordinal);

        public Dictionary<ClassDeclStmt, string> ClassQualifiedPaths { get; } = new();

        public Dictionary<InterfaceDeclStmt, string> InterfaceQualifiedPaths { get; } = new();

        public Dictionary<InterfaceDeclStmt, Dictionary<string, InterfaceMethodDecl>> InterfaceContractCache { get; } = new();

        public Dictionary<InterfaceDeclStmt, Dictionary<string, InterfacePropertyDecl>> InterfacePropertyContractCache { get; } = new();

        public Dictionary<ClassDeclStmt, (HashSet<string> InstanceMembers, HashSet<string> StaticMembers)> ClassMemberSetCache { get; } = new();

        public void Reset()
        {
            Instructions.Clear();
            Functions.Clear();
            ClassInfos.Clear();
            TopLevelClassDecls.Clear();
            TopLevelInterfaceDecls.Clear();
            QualifiedClassDecls.Clear();
            QualifiedInterfaceDecls.Clear();
            ClassQualifiedPaths.Clear();
            InterfaceQualifiedPaths.Clear();
            InterfaceContractCache.Clear();
            InterfacePropertyContractCache.Clear();
            ClassMemberSetCache.Clear();
        }
    }
}
