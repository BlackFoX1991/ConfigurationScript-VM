using CFGS_VM.Analytic.Tree;

namespace CFGS_VM.VMCore.Codegen
{
    internal sealed class CompilationPlan
    {
        public List<FuncDeclStmt> FunctionDecls { get; } = [];

        public List<InterfaceDeclStmt> InterfaceDecls { get; } = [];

        public List<ClassDeclStmt> ClassDecls { get; } = [];

        public List<InterfaceDeclStmt> OrderedInterfaces { get; set; } = [];

        public List<ClassDeclStmt> OrderedClasses { get; set; } = [];
    }
}
