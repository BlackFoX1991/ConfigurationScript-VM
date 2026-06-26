using CFGS_VM.Analytic.Tree;

namespace CFGS_VM.Analytic.Semantics
{
    internal static class NamespaceMemberVisibilityFacts
    {
        public static bool TryApply(Stmt stmt, MemberVisibility visibility, bool hasExplicitVisibility)
        {
            switch (stmt)
            {
                case FuncDeclStmt funcDecl:
                    funcDecl.Visibility = visibility;
                    funcDecl.HasExplicitVisibility = hasExplicitVisibility;
                    return true;

                case ClassDeclStmt classDecl:
                    classDecl.Visibility = visibility;
                    classDecl.HasExplicitVisibility = hasExplicitVisibility;
                    return true;

                case InterfaceDeclStmt interfaceDecl:
                    interfaceDecl.Visibility = visibility;
                    interfaceDecl.HasExplicitVisibility = hasExplicitVisibility;
                    return true;

                case EnumDeclStmt enumDecl:
                    enumDecl.Visibility = visibility;
                    enumDecl.HasExplicitVisibility = hasExplicitVisibility;
                    return true;

                case VarDecl varDecl:
                    varDecl.Visibility = visibility;
                    varDecl.HasExplicitVisibility = hasExplicitVisibility;
                    return true;

                case ConstDecl constDecl:
                    constDecl.Visibility = visibility;
                    constDecl.HasExplicitVisibility = hasExplicitVisibility;
                    return true;

                default:
                    return false;
            }
        }

        public static bool TryGet(Stmt stmt, out MemberVisibility visibility, out bool hasExplicitVisibility)
        {
            switch (stmt)
            {
                case FuncDeclStmt funcDecl:
                    visibility = funcDecl.Visibility;
                    hasExplicitVisibility = funcDecl.HasExplicitVisibility;
                    return true;

                case ClassDeclStmt classDecl:
                    visibility = classDecl.Visibility;
                    hasExplicitVisibility = classDecl.HasExplicitVisibility;
                    return true;

                case InterfaceDeclStmt interfaceDecl:
                    visibility = interfaceDecl.Visibility;
                    hasExplicitVisibility = interfaceDecl.HasExplicitVisibility;
                    return true;

                case EnumDeclStmt enumDecl:
                    visibility = enumDecl.Visibility;
                    hasExplicitVisibility = enumDecl.HasExplicitVisibility;
                    return true;

                case VarDecl varDecl:
                    visibility = varDecl.Visibility;
                    hasExplicitVisibility = varDecl.HasExplicitVisibility;
                    return true;

                case ConstDecl constDecl:
                    visibility = constDecl.Visibility;
                    hasExplicitVisibility = constDecl.HasExplicitVisibility;
                    return true;

                default:
                    visibility = MemberVisibility.Public;
                    hasExplicitVisibility = false;
                    return false;
            }
        }
    }
}
