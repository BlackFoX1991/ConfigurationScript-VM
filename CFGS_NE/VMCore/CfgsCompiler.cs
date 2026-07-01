using CFGS_VM.Analytic;
using CFGS_VM.Analytic.Core;
using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore.Extensions.Core;
using CFGS_VM.VMCore.Plugin;
using System.Security.Cryptography;
using System.Text;

namespace CFGS_VM.VMCore
{
    public static class CfgsCompiler
    {
        public static CompiledScript CompileFile(
            string path,
            Action<string>? loadPluginDll = null,
            INamespaceRegistry? namespaces = null,
            string compilerVersion = "",
            IReadOnlyList<string>? additionalSearchRoots = null)
        {
            string source = File.ReadAllText(path);
            string workingDirectory =
                FrontendPipeline.TryGetWorkingDirectory(path)
                ?? Path.GetDirectoryName(Path.GetFullPath(path))
                ?? Environment.CurrentDirectory;

            return CompileSource(path, source, loadPluginDll, namespaces, workingDirectory, compilerVersion, additionalSearchRoots);
        }

        public static CompiledScript CompileSource(
            string name,
            string source,
            Action<string>? loadPluginDll = null,
            INamespaceRegistry? namespaces = null,
            string? workingDirectory = null,
            string compilerVersion = "",
            IReadOnlyList<string>? additionalSearchRoots = null)
        {
            List<string> requiredPlugins = new();
            Action<string> trackingPluginLoader = path =>
            {
                string fullPath = Path.GetFullPath(path);
                requiredPlugins.Add(fullPath);
                loadPluginDll?.Invoke(fullPath);
            };

            FrontendPipeline frontendPipeline = new(
                loadPluginDll: trackingPluginLoader,
                namespaces: namespaces,
                workingDirectory: workingDirectory,
                additionalSearchRoots: additionalSearchRoots);

            FrontendBuildResult frontendBuild = frontendPipeline.BuildLoweredAstWithSyntax(name, source);
            Compiler compiler = new(name);
            List<Instruction> bytecode = compiler.Compile(frontendBuild.LoweredAst);

            return new CompiledScript(
                name,
                ComputeSourceHash(source),
                compilerVersion,
                CompiledScript.CurrentBytecodeVersion,
                ShouldAutoInvokeMain(frontendBuild.SyntaxAst),
                requiredPlugins,
                bytecode,
                compiler.Functions);
        }

        private static string ComputeSourceHash(string source)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static bool ShouldAutoInvokeMain(IReadOnlyList<Stmt> syntaxAst)
            => HasTopLevelMainFunction(syntaxAst) &&
               !HasImperativeTopLevelStatements(syntaxAst) &&
               !HasExplicitTopLevelMainInvocation(syntaxAst);

        private static bool HasTopLevelMainFunction(IEnumerable<Stmt> statements)
        {
            foreach (Stmt stmt in statements)
            {
                switch (stmt)
                {
                    case FuncDeclStmt funcDecl when string.Equals(funcDecl.Name, "main", StringComparison.Ordinal):
                        return true;
                    case ExportStmt exportStmt when exportStmt.Inner is FuncDeclStmt exportedFunc &&
                                                   string.Equals(exportedFunc.Name, "main", StringComparison.Ordinal):
                        return true;
                }
            }

            return false;
        }

        private static bool HasImperativeTopLevelStatements(IEnumerable<Stmt> statements)
        {
            foreach (Stmt stmt in statements)
            {
                if (!IsDeclarativeTopLevelStatement(stmt))
                    return true;
            }

            return false;
        }

        private static bool IsDeclarativeTopLevelStatement(Stmt stmt)
            => stmt switch
            {
                EmptyStmt => true,
                BareImportSyntaxStmt => true,
                NamespaceImportSyntaxStmt => true,
                NamedImportSyntaxStmt => true,
                DefaultImportSyntaxStmt => true,
                NativeImportSyntaxStmt => true,
                UseNamespaceStmt => true,
                VarDecl => true,
                ConstDecl => true,
                FuncDeclStmt => true,
                ClassDeclStmt => true,
                InterfaceDeclStmt => true,
                EnumDeclStmt => true,
                NamespaceDeclStmt => true,
                ExportStmt exportStmt => IsDeclarativeTopLevelStatement(exportStmt.Inner),
                _ => false
            };

        private static bool HasExplicitTopLevelMainInvocation(IEnumerable<Stmt> statements)
        {
            foreach (Stmt stmt in statements)
            {
                if (HasExplicitTopLevelMainInvocation(stmt))
                    return true;
            }

            return false;
        }

        private static bool HasExplicitTopLevelMainInvocation(Stmt stmt)
            => stmt switch
            {
                ExprStmt exprStmt => ContainsDirectMainCall(exprStmt.Expression),
                AssignExprStmt assignStmt => ContainsDirectMainCall(assignStmt.Value),
                SliceSetStmt sliceSetStmt => ContainsDirectMainCall(sliceSetStmt.Value),
                PushStmt pushStmt => ContainsDirectMainCall(pushStmt.Value),
                VarDecl varDecl => ContainsDirectMainCall(varDecl.Value),
                ConstDecl constDecl => ContainsDirectMainCall(constDecl.Value),
                ExportStmt exportStmt => HasExplicitTopLevelMainInvocation(exportStmt.Inner),
                BlockStmt blockStmt => HasExplicitTopLevelMainInvocation(blockStmt.Statements),
                NamespaceDeclStmt namespaceDecl => HasExplicitTopLevelMainInvocation(namespaceDecl.BodyStatements),
                _ => false
            };

        private static bool ContainsDirectMainCall(Expr? expr)
        {
            switch (expr)
            {
                case null:
                case VarExpr:
                case FuncExpr:
                    return false;
                case CallExpr callExpr:
                    if (callExpr.Target is VarExpr targetVar &&
                        string.Equals(targetVar.Name, "main", StringComparison.Ordinal))
                    {
                        return true;
                    }

                    return ContainsDirectMainCall(callExpr.Target) ||
                           callExpr.Args.Any(ContainsDirectMainCall);
                case AwaitExpr awaitExpr:
                    return ContainsDirectMainCall(awaitExpr.Inner);
                case InterpolatedStringExpr interpolatedStringExpr:
                    return interpolatedStringExpr.Parts
                        .OfType<InterpolatedStringExprPart>()
                        .Any(part => ContainsDirectMainCall(part.Expression));
                case BinaryExpr binaryExpr:
                    return ContainsDirectMainCall(binaryExpr.Left) || ContainsDirectMainCall(binaryExpr.Right);
                case UnaryExpr unaryExpr:
                    return ContainsDirectMainCall(unaryExpr.Right);
                case ConditionalExpr conditionalExpr:
                    return ContainsDirectMainCall(conditionalExpr.Condition) ||
                           ContainsDirectMainCall(conditionalExpr.ThenExpr) ||
                           ContainsDirectMainCall(conditionalExpr.ElseExpr);
                case ArrayExpr arrayExpr:
                    return arrayExpr.Elements.Any(ContainsDirectMainCall);
                case DictExpr dictExpr:
                    return dictExpr.Pairs.Any(pair => ContainsDirectMainCall(pair.Key) || ContainsDirectMainCall(pair.Value));
                case IndexExpr indexExpr:
                    return ContainsDirectMainCall(indexExpr.Target) || ContainsDirectMainCall(indexExpr.Index);
                case SliceExpr sliceExpr:
                    return ContainsDirectMainCall(sliceExpr.Target) ||
                           ContainsDirectMainCall(sliceExpr.Start) ||
                           ContainsDirectMainCall(sliceExpr.End);
                case ObjectInitExpr objectInitExpr:
                    return ContainsDirectMainCall(objectInitExpr.Target) ||
                           objectInitExpr.Inits.Any(ContainsDirectMainCall);
                case NewExpr newExpr:
                    return newExpr.Args.Any(ContainsDirectMainCall) ||
                           newExpr.Initializers.Any(ContainsDirectMainCall);
                case MatchExpr matchExpr:
                    return ContainsDirectMainCall(matchExpr.Scrutinee) ||
                           ContainsDirectMainCall(matchExpr.DefaultArm) ||
                           matchExpr.Arms.Any(arm => ContainsDirectMainCall(arm.Guard) || ContainsDirectMainCall(arm.Body));
                case MethodCallExpr methodCallExpr:
                    return ContainsDirectMainCall(methodCallExpr.Target) ||
                           methodCallExpr.Args.Any(ContainsDirectMainCall);
                case GetFieldExpr getFieldExpr:
                    return ContainsDirectMainCall(getFieldExpr.Target);
                case OutExpr outExpr:
                    return HasExplicitTopLevelMainInvocation(outExpr.Body);
                case NamedArgExpr namedArgExpr:
                    return ContainsDirectMainCall(namedArgExpr.Value);
                case SpreadArgExpr spreadArgExpr:
                    return ContainsDirectMainCall(spreadArgExpr.Value);
                default:
                    return false;
            }
        }

        private static bool ContainsDirectMainCall(ObjectInitializerEntry? entry)
        {
            return entry switch
            {
                null => false,
                ObjectMemberInitializerEntry memberInitializer =>
                    ContainsDirectMainCall(memberInitializer.Value) ||
                    memberInitializer.NestedInitializers.Any(ContainsDirectMainCall),
                ObjectIndexerInitializerEntry indexerInitializer =>
                    ContainsDirectMainCall(indexerInitializer.Index) ||
                    ContainsDirectMainCall(indexerInitializer.Value),
                _ => false
            };
        }
    }
}
