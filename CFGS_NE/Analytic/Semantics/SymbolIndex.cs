using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore;
using CFGS_VM.VMCore.Codegen;
using CFGS_VM.VMCore.Extensions;
using CFGS_VM.VMCore.Extensions.Core;

namespace CFGS_VM.Analytic.Semantics
{
    internal sealed class SymbolIndex
    {
        public CompilationPlan Build(CompilationContext context, Dictionary<string, FunctionInfo> functions, List<Stmt> program)
        {
            CompilationPlan plan = new();

            foreach (Stmt raw in program)
            {
                Stmt stmt = raw is ExportStmt ex ? ex.Inner : raw;

                if (stmt is FuncDeclStmt funcDecl)
                {
                    if (functions.ContainsKey(funcDecl.Name))
                    {
                        throw new CompilerException(
                            $"duplicate function '{funcDecl.Name}'",
                            funcDecl.Line,
                            funcDecl.Col,
                            funcDecl.OriginFile);
                    }

                    functions[funcDecl.Name] = new FunctionInfo(
                        funcDecl.Parameters,
                        -1,
                        funcDecl.MinArgs,
                        funcDecl.RestParameter,
                        funcDecl.IsAsync);

                    plan.FunctionDecls.Add(funcDecl);
                }
                else if (stmt is ClassDeclStmt classDecl)
                {
                    plan.ClassDecls.Add(classDecl);
                    context.TopLevelClassDecls[classDecl.Name] = classDecl;
                    RegisterQualifiedClassDecl(context, classDecl, classDecl.Name);
                }
                else if (stmt is InterfaceDeclStmt interfaceDecl)
                {
                    plan.InterfaceDecls.Add(interfaceDecl);
                    context.TopLevelInterfaceDecls[interfaceDecl.Name] = interfaceDecl;
                    RegisterQualifiedInterfaceDecl(context, interfaceDecl, interfaceDecl.Name);
                }
                else if (stmt is BlockStmt block && Compiler.TryGetNamespaceScopePath(block, out string namespacePath))
                {
                    RegisterNamespaceScopeClasses(context, block, namespacePath);
                    RegisterNamespaceScopeInterfaces(context, block, namespacePath);
                }
            }

            return plan;
        }

        private static void RegisterQualifiedClassDecl(CompilationContext context, ClassDeclStmt decl, string qualifiedPath)
        {
            if (!context.QualifiedClassDecls.TryAdd(qualifiedPath, decl))
            {
                throw new CompilerException(
                    $"duplicate class '{qualifiedPath}'",
                    decl.Line,
                    decl.Col,
                    decl.OriginFile);
            }

            context.ClassQualifiedPaths[decl] = qualifiedPath;

            foreach (ClassDeclStmt nested in decl.NestedClasses)
                RegisterQualifiedClassDecl(context, nested, $"{qualifiedPath}.{nested.Name}");
        }

        private static void RegisterQualifiedInterfaceDecl(CompilationContext context, InterfaceDeclStmt decl, string qualifiedPath)
        {
            if (!context.QualifiedInterfaceDecls.TryAdd(qualifiedPath, decl))
            {
                throw new CompilerException(
                    $"duplicate interface '{qualifiedPath}'",
                    decl.Line,
                    decl.Col,
                    decl.OriginFile);
            }

            context.InterfaceQualifiedPaths[decl] = qualifiedPath;
        }

        private static void RegisterNamespaceScopeClasses(CompilationContext context, BlockStmt namespaceScope, string namespacePath)
        {
            foreach (Stmt stmt in namespaceScope.Statements)
            {
                if (stmt is not ClassDeclStmt classDecl)
                    continue;

                RegisterQualifiedClassDecl(context, classDecl, $"{namespacePath}.{classDecl.Name}");
            }
        }

        private static void RegisterNamespaceScopeInterfaces(CompilationContext context, BlockStmt namespaceScope, string namespacePath)
        {
            foreach (Stmt stmt in namespaceScope.Statements)
            {
                if (stmt is not InterfaceDeclStmt interfaceDecl)
                    continue;

                RegisterQualifiedInterfaceDecl(context, interfaceDecl, $"{namespacePath}.{interfaceDecl.Name}");
            }
        }
    }
}
