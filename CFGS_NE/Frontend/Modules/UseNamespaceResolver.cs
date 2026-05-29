using CFGS_VM.Analytic.Semantics;
using CFGS_VM.Analytic.Tree;
using CFGS_VM.Analytic.Ex;

namespace CFGS_VM.Analytic.Modules
{
    internal sealed class UseNamespaceResolver
    {
        private enum NamespaceMemberKind
        {
            Namespace,
            Function,
            Class,
            Interface,
            Enum,
            Variable,
            Constant
        }

        private sealed record NamespaceMemberInfo(string Name, string QualifiedPath, NamespaceMemberKind Kind);

        private sealed class RewriteState
        {
            public RewriteState(string currentOrigin, Dictionary<string, List<NamespaceMemberInfo>> useLookup)
            {
                CurrentOrigin = currentOrigin;
                UseLookup = useLookup;
            }

            public string CurrentOrigin { get; }

            public string? CurrentNamespacePath { get; init; }

            public Dictionary<string, List<NamespaceMemberInfo>> UseLookup { get; }

            public Stack<HashSet<string>> LocalScopes { get; } = new();

            public List<HashSet<string>> ContainingClassNestedTypeNames { get; init; } = [];

            public HashSet<string> CurrentLocals
                => LocalScopes.Count > 0 ? LocalScopes.Peek() : EmptyLocals;
        }

        private static readonly HashSet<string> EmptyLocals = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, NamespaceMemberInfo>> _namespaceMembersByPath = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _namespaceDeclaredValueNamesByPath = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _namespaceDeclaredTypeNamesByPath = new(StringComparer.Ordinal);
        private readonly HashSet<string> _globalTopLevelValueNames = new(StringComparer.Ordinal);
        private readonly HashSet<string> _globalTopLevelTypeNames = new(StringComparer.Ordinal);

        public List<Stmt> Resolve(List<Stmt> syntaxStatements, List<Stmt> resolvedStatements)
        {
            List<UseNamespaceStmt> useStatements = syntaxStatements
                .OfType<UseNamespaceStmt>()
                .ToList();

            if (useStatements.Count == 0)
                return resolvedStatements;

            string currentOrigin = useStatements[0].OriginFile;

            BuildNamespaceModel(resolvedStatements);
            BuildGlobalTopLevelSurfaces(resolvedStatements);

            Dictionary<string, List<NamespaceMemberInfo>> useLookup = BuildUseLookup(useStatements);
            RewriteState rootState = new(currentOrigin, useLookup);

            List<Stmt> rewritten = new(resolvedStatements.Count);
            foreach (Stmt stmt in resolvedStatements)
            {
                if (stmt is UseNamespaceStmt useStmt &&
                    string.Equals(useStmt.OriginFile, currentOrigin, StringComparison.Ordinal))
                {
                    continue;
                }

                rewritten.Add(RewriteTopLevelStatement(stmt, rootState));
            }

            return rewritten;
        }

        private void BuildNamespaceModel(IEnumerable<Stmt> statements)
        {
            foreach (Stmt raw in statements)
            {
                Stmt stmt = raw is ExportStmt exportStmt ? exportStmt.Inner : raw;
                if (stmt is not NamespaceDeclStmt namespaceDecl)
                    continue;

                RegisterNamespaceDecl(namespaceDecl);
            }
        }

        private void BuildGlobalTopLevelSurfaces(IEnumerable<Stmt> statements)
        {
            foreach (Stmt stmt in statements)
            {
                if (TopLevelSymbolFacts.TryGetNamedTopLevel(stmt, out string? name) &&
                    !string.IsNullOrWhiteSpace(name))
                {
                    _globalTopLevelValueNames.Add(name);
                }

                switch (stmt)
                {
                    case ExportStmt exportStmt:
                        TrackGlobalTypeSurface(exportStmt.Inner);
                        break;
                    default:
                        TrackGlobalTypeSurface(stmt);
                        break;
                }
            }
        }

        private void TrackGlobalTypeSurface(Stmt stmt)
        {
            switch (stmt)
            {
                case ClassDeclStmt classDecl:
                    _globalTopLevelTypeNames.Add(classDecl.Name);
                    break;
                case InterfaceDeclStmt interfaceDecl:
                    _globalTopLevelTypeNames.Add(interfaceDecl.Name);
                    break;
            }
        }

        private void RegisterNamespaceDecl(NamespaceDeclStmt namespaceDecl)
        {
            if (namespaceDecl.Parts.Count == 0)
                return;

            for (int i = 1; i < namespaceDecl.Parts.Count; i++)
            {
                string parentPath = string.Join(".", namespaceDecl.Parts.Take(i));
                string childName = namespaceDecl.Parts[i];
                AddNamespaceMember(parentPath, childName, $"{parentPath}.{childName}", NamespaceMemberKind.Namespace);
            }

            string fullPath = string.Join(".", namespaceDecl.Parts);
            EnsureNamespaceBuckets(fullPath);

            foreach (Stmt bodyStmt in namespaceDecl.BodyStatements)
            {
                if (!TryGetNamespaceDeclaredMember(bodyStmt, out string? name, out NamespaceMemberKind kind))
                    continue;

                if (string.IsNullOrWhiteSpace(name))
                    continue;

                AddNamespaceMember(fullPath, name, $"{fullPath}.{name}", kind);

                if (kind is NamespaceMemberKind.Class or NamespaceMemberKind.Interface)
                    _namespaceDeclaredTypeNamesByPath[fullPath].Add(name);

                _namespaceDeclaredValueNamesByPath[fullPath].Add(name);
            }
        }

        private static bool TryGetNamespaceDeclaredMember(Stmt stmt, out string? name, out NamespaceMemberKind kind)
        {
            if (stmt is ExportStmt exportStmt)
                return TryGetNamespaceDeclaredMember(exportStmt.Inner, out name, out kind);

            switch (stmt)
            {
                case FuncDeclStmt funcDecl:
                    name = funcDecl.Name;
                    kind = NamespaceMemberKind.Function;
                    return true;
                case ClassDeclStmt classDecl:
                    name = classDecl.Name;
                    kind = NamespaceMemberKind.Class;
                    return true;
                case InterfaceDeclStmt interfaceDecl:
                    name = interfaceDecl.Name;
                    kind = NamespaceMemberKind.Interface;
                    return true;
                case EnumDeclStmt enumDecl:
                    name = enumDecl.Name;
                    kind = NamespaceMemberKind.Enum;
                    return true;
                case VarDecl varDecl:
                    name = varDecl.Name;
                    kind = NamespaceMemberKind.Variable;
                    return true;
                case ConstDecl constDecl:
                    name = constDecl.Name;
                    kind = NamespaceMemberKind.Constant;
                    return true;
                default:
                    name = null;
                    kind = default;
                    return false;
            }
        }

        private void EnsureNamespaceBuckets(string path)
        {
            if (!_namespaceMembersByPath.ContainsKey(path))
                _namespaceMembersByPath[path] = new Dictionary<string, NamespaceMemberInfo>(StringComparer.Ordinal);

            if (!_namespaceDeclaredValueNamesByPath.ContainsKey(path))
                _namespaceDeclaredValueNamesByPath[path] = new HashSet<string>(StringComparer.Ordinal);

            if (!_namespaceDeclaredTypeNamesByPath.ContainsKey(path))
                _namespaceDeclaredTypeNamesByPath[path] = new HashSet<string>(StringComparer.Ordinal);
        }

        private void AddNamespaceMember(string namespacePath, string name, string qualifiedPath, NamespaceMemberKind kind)
        {
            EnsureNamespaceBuckets(namespacePath);
            _namespaceMembersByPath[namespacePath].TryAdd(name, new NamespaceMemberInfo(name, qualifiedPath, kind));
        }

        private Dictionary<string, List<NamespaceMemberInfo>> BuildUseLookup(IEnumerable<UseNamespaceStmt> useStatements)
        {
            Dictionary<string, List<NamespaceMemberInfo>> lookup = new(StringComparer.Ordinal);
            HashSet<string> seenPaths = new(StringComparer.Ordinal);

            foreach (UseNamespaceStmt useStmt in useStatements)
            {
                string path = useStmt.QualifiedPath;
                if (!seenPaths.Add(path))
                    continue;

                if (!_namespaceMembersByPath.TryGetValue(path, out Dictionary<string, NamespaceMemberInfo>? members))
                {
                    throw new ParserException(
                        $"unknown namespace '{path}' in use directive",
                        useStmt.Line,
                        useStmt.Col,
                        useStmt.OriginFile);
                }

                foreach (NamespaceMemberInfo member in members.Values)
                {
                    if (!lookup.TryGetValue(member.Name, out List<NamespaceMemberInfo>? bucket))
                    {
                        bucket = new List<NamespaceMemberInfo>();
                        lookup[member.Name] = bucket;
                    }

                    if (!bucket.Any(existing => string.Equals(existing.QualifiedPath, member.QualifiedPath, StringComparison.Ordinal)))
                        bucket.Add(member);
                }
            }

            return lookup;
        }

        private Stmt RewriteTopLevelStatement(Stmt stmt, RewriteState state)
        {
            if (!string.Equals(stmt.OriginFile, state.CurrentOrigin, StringComparison.Ordinal))
                return stmt;

            return RewriteStatement(stmt, state);
        }

        private Stmt RewriteStatement(Stmt stmt, RewriteState state)
        {
            return stmt switch
            {
                ExportStmt exportStmt => new ExportStmt(exportStmt.Name, RewriteStatement(exportStmt.Inner, state), exportStmt.Line, exportStmt.Col, exportStmt.OriginFile),
                EmptyStmt => stmt,
                VarDecl varDecl => new VarDecl(varDecl.Name, RewriteNullableExpr(varDecl.Value, state), varDecl.Line, varDecl.Col, varDecl.OriginFile)
                {
                    IsSyntheticNamespaceRoot = varDecl.IsSyntheticNamespaceRoot
                },
                ConstDecl constDecl => new ConstDecl(constDecl.Name, RewriteExpr(constDecl.Value, state), constDecl.Line, constDecl.Col, constDecl.OriginFile),
                DestructureDeclStmt destructureDecl => new DestructureDeclStmt(
                    RewritePattern(destructureDecl.Pattern, state),
                    RewriteExpr(destructureDecl.Value, state),
                    destructureDecl.IsConst,
                    destructureDecl.Line,
                    destructureDecl.Col,
                    destructureDecl.OriginFile),
                DestructureAssignStmt destructureAssign => new DestructureAssignStmt(
                    RewritePattern(destructureAssign.Pattern, state),
                    RewriteExpr(destructureAssign.Value, state),
                    destructureAssign.Line,
                    destructureAssign.Col,
                    destructureAssign.OriginFile),
                AssignStmt assignStmt => RewriteAssignStatement(assignStmt, state),
                AssignIndexExprStmt assignIndexExpr => new AssignIndexExprStmt(
                    (IndexExpr)RewriteExpr(assignIndexExpr.Target, state),
                    RewriteExpr(assignIndexExpr.Value, state),
                    assignIndexExpr.Line,
                    assignIndexExpr.Col,
                    assignIndexExpr.OriginFile),
                AssignExprStmt assignExpr => new AssignExprStmt(
                    RewriteExpr(assignExpr.Target, state),
                    RewriteExpr(assignExpr.Value, state),
                    assignExpr.Line,
                    assignExpr.Col,
                    assignExpr.OriginFile),
                CompoundAssignStmt compoundAssign => new CompoundAssignStmt(
                    RewriteExpr(compoundAssign.Target, state),
                    compoundAssign.Op,
                    RewriteExpr(compoundAssign.Value, state),
                    compoundAssign.Line,
                    compoundAssign.Col,
                    compoundAssign.OriginFile),
                SliceSetStmt sliceSet => new SliceSetStmt(
                    (SliceExpr)RewriteExpr(sliceSet.Slice, state),
                    RewriteExpr(sliceSet.Value, state),
                    sliceSet.Line,
                    sliceSet.Col,
                    sliceSet.OriginFile),
                ExprStmt exprStmt => new ExprStmt(RewriteExpr(exprStmt.Expression, state), exprStmt.Line, exprStmt.Col, exprStmt.OriginFile),
                PushStmt pushStmt => new PushStmt(RewriteExpr(pushStmt.Target, state), RewriteExpr(pushStmt.Value, state), pushStmt.Line, pushStmt.Col, pushStmt.OriginFile),
                DeleteVarStmt deleteVar => RewriteDeleteVarStatement(deleteVar, state),
                DeleteIndexStmt deleteIndex => RewriteDeleteIndexStatement(deleteIndex, state),
                DeleteAllStmt deleteAll => new DeleteAllStmt(RewriteExpr(deleteAll.Target, state), deleteAll.Line, deleteAll.Col, deleteAll.OriginFile),
                DeleteExprStmt deleteExpr => new DeleteExprStmt(RewriteExpr(deleteExpr.Target, state), deleteExpr.DeleteAll, deleteExpr.Line, deleteExpr.Col, deleteExpr.OriginFile),
                SetFieldStmt setField => new SetFieldStmt(RewriteExpr(setField.Target, state), setField.Field, RewriteExpr(setField.Value, state), setField.Line, setField.Col, setField.OriginFile),
                BlockStmt blockStmt => RewriteBlock(blockStmt, state),
                IfStmt ifStmt => new IfStmt(
                    RewriteExpr(ifStmt.Condition, state),
                    RewriteBlock(ifStmt.ThenBlock, state),
                    ifStmt.ElseBranch is null ? null : RewriteStatement(ifStmt.ElseBranch, state),
                    ifStmt.Line,
                    ifStmt.Col,
                    ifStmt.OriginFile),
                WhileStmt whileStmt => new WhileStmt(RewriteExpr(whileStmt.Condition, state), RewriteBlock(whileStmt.Body, state), whileStmt.Line, whileStmt.Col, whileStmt.OriginFile),
                DoWhileStmt doWhileStmt => new DoWhileStmt(RewriteStatement(doWhileStmt.Body, state), RewriteExpr(doWhileStmt.Condition, state), doWhileStmt.Line, doWhileStmt.Col, doWhileStmt.OriginFile),
                ForStmt forStmt => RewriteForStatement(forStmt, state),
                ForeachStmt foreachStmt => RewriteForeachStatement(foreachStmt, state),
                BreakStmt => stmt,
                ContinueStmt => stmt,
                MatchStmt matchStmt => RewriteMatchStatement(matchStmt, state),
                TryStmt tryStmt => RewriteTryStatement(tryStmt, state),
                UsingStmt usingStmt => RewriteUsingStatement(usingStmt, state),
                ThrowStmt throwStmt => new ThrowStmt(RewriteExpr(throwStmt.Value, state), throwStmt.Line, throwStmt.Col, throwStmt.OriginFile),
                YieldStmt => stmt,
                ReturnStmt returnStmt => new ReturnStmt(RewriteNullableExpr(returnStmt.Value, state), returnStmt.Line, returnStmt.Col, returnStmt.OriginFile),
                FuncDeclStmt funcDecl => RewriteFunctionDecl(funcDecl, state),
                ClassDeclStmt classDecl => RewriteClassDecl(classDecl, state),
                InterfaceDeclStmt interfaceDecl => RewriteInterfaceDecl(interfaceDecl, state),
                EnumDeclStmt => stmt,
                NamespaceDeclStmt namespaceDecl => RewriteNamespaceDecl(namespaceDecl, state),
                UseNamespaceStmt => stmt,
                NamespaceImportAliasStmt => stmt,
                ImportAliasDeclStmt => stmt,
                _ => stmt
            };
        }

        private NamespaceDeclStmt RewriteNamespaceDecl(NamespaceDeclStmt namespaceDecl, RewriteState state)
        {
            string namespacePath = string.Join(".", namespaceDecl.Parts);
            RewriteState namespaceState = new(state.CurrentOrigin, state.UseLookup)
            {
                CurrentNamespacePath = namespacePath,
                ContainingClassNestedTypeNames = new List<HashSet<string>>(state.ContainingClassNestedTypeNames)
            };

            foreach (HashSet<string> locals in state.LocalScopes.Reverse())
                namespaceState.LocalScopes.Push(new HashSet<string>(locals, StringComparer.Ordinal));

            List<Stmt> rewrittenBody = RewriteStatementList(namespaceDecl.BodyStatements, namespaceState, trackSequentialLocals: false);
            return new NamespaceDeclStmt(namespaceDecl.Parts, rewrittenBody, namespaceDecl.Line, namespaceDecl.Col, namespaceDecl.OriginFile, namespaceDecl.IsFileScoped);
        }

        private BlockStmt RewriteBlock(BlockStmt block, RewriteState state)
        {
            List<Stmt> rewritten = RewriteStatementList(block.Statements, state, trackSequentialLocals: state.LocalScopes.Count > 0);
            BlockStmt copy = new(rewritten, block.Line, block.Col, block.OriginFile)
            {
                IsFunctionBody = block.IsFunctionBody,
                IsNamespaceScope = block.IsNamespaceScope
            };
            return copy;
        }

        private List<Stmt> RewriteStatementList(IEnumerable<Stmt> statements, RewriteState state, bool trackSequentialLocals)
        {
            List<Stmt> rewritten = new();
            foreach (Stmt stmt in statements)
            {
                Stmt rewrittenStmt = RewriteStatement(stmt, state);
                rewritten.Add(rewrittenStmt);

                if (trackSequentialLocals)
                    TrackSequentialLocalDeclaration(rewrittenStmt, state);
            }

            return rewritten;
        }

        private void TrackSequentialLocalDeclaration(Stmt stmt, RewriteState state)
        {
            if (state.LocalScopes.Count == 0)
                return;

            switch (stmt)
            {
                case VarDecl varDecl:
                    state.CurrentLocals.Add(varDecl.Name);
                    break;
                case ConstDecl constDecl:
                    state.CurrentLocals.Add(constDecl.Name);
                    break;
                case FuncDeclStmt funcDecl:
                    state.CurrentLocals.Add(funcDecl.Name);
                    break;
                case DestructureDeclStmt destructureDecl:
                    foreach (string binding in CollectPatternBindingNames(destructureDecl.Pattern))
                        state.CurrentLocals.Add(binding);
                    break;
                case ForeachStmt foreachStmt when foreachStmt.DeclareLocal:
                    state.CurrentLocals.Add(foreachStmt.VarName);
                    if (foreachStmt.TargetPattern is not null)
                    {
                        foreach (string binding in CollectPatternBindingNames(foreachStmt.TargetPattern))
                            state.CurrentLocals.Add(binding);
                    }
                    break;
                case UsingStmt usingStmt when !string.IsNullOrWhiteSpace(usingStmt.BindingName):
                    state.CurrentLocals.Add(usingStmt.BindingName!);
                    break;
            }
        }

        private Stmt RewriteAssignStatement(AssignStmt assignStmt, RewriteState state)
        {
            Expr value = RewriteExpr(assignStmt.Value, state);
            if (TryResolveUsedValueRoot(assignStmt.Name, assignStmt, state, out NamespaceMemberInfo? resolved))
            {
                return new AssignExprStmt(
                    BuildQualifiedAccessExpr(resolved!.QualifiedPath, assignStmt),
                    value,
                    assignStmt.Line,
                    assignStmt.Col,
                    assignStmt.OriginFile);
            }

            return new AssignStmt(assignStmt.Name, value, assignStmt.Line, assignStmt.Col, assignStmt.OriginFile);
        }

        private Stmt RewriteDeleteVarStatement(DeleteVarStmt deleteVar, RewriteState state)
        {
            if (TryResolveUsedValueRoot(deleteVar.Name, deleteVar, state, out NamespaceMemberInfo? resolved))
            {
                return new DeleteExprStmt(
                    BuildQualifiedAccessExpr(resolved!.QualifiedPath, deleteVar),
                    deleteAll: true,
                    deleteVar.Line,
                    deleteVar.Col,
                    deleteVar.OriginFile);
            }

            return deleteVar;
        }

        private Stmt RewriteDeleteIndexStatement(DeleteIndexStmt deleteIndex, RewriteState state)
        {
            Expr rewrittenIndex = RewriteExpr(deleteIndex.Index, state);
            if (TryResolveUsedValueRoot(deleteIndex.Name, deleteIndex, state, out NamespaceMemberInfo? resolved))
            {
                return new DeleteExprStmt(
                    new IndexExpr(
                        BuildQualifiedAccessExpr(resolved!.QualifiedPath, deleteIndex),
                        rewrittenIndex,
                        deleteIndex.Line,
                        deleteIndex.Col,
                        deleteIndex.OriginFile),
                    deleteAll: false,
                    deleteIndex.Line,
                    deleteIndex.Col,
                    deleteIndex.OriginFile);
            }

            return new DeleteIndexStmt(deleteIndex.Name, rewrittenIndex, deleteIndex.Line, deleteIndex.Col, deleteIndex.OriginFile);
        }

        private ForStmt RewriteForStatement(ForStmt forStmt, RewriteState state)
        {
            Stmt? init = forStmt.Init is null ? null : RewriteStatement(forStmt.Init, state);
            if (state.LocalScopes.Count > 0 && init is not null)
                TrackSequentialLocalDeclaration(init, state);

            return new ForStmt(
                init,
                RewriteNullableExpr(forStmt.Condition, state),
                forStmt.Increment is null ? null : RewriteStatement(forStmt.Increment, state),
                RewriteBlock(forStmt.Body, state),
                forStmt.Line,
                forStmt.Col,
                forStmt.OriginFile);
        }

        private ForeachStmt RewriteForeachStatement(ForeachStmt foreachStmt, RewriteState state)
        {
            Expr iterable = RewriteExpr(foreachStmt.Iterable, state);

            if (state.LocalScopes.Count > 0 && foreachStmt.DeclareLocal)
            {
                state.CurrentLocals.Add(foreachStmt.VarName);
                if (foreachStmt.TargetPattern is not null)
                {
                    foreach (string binding in CollectPatternBindingNames(foreachStmt.TargetPattern))
                        state.CurrentLocals.Add(binding);
                }
            }

            return new ForeachStmt(
                foreachStmt.VarName,
                foreachStmt.TargetPattern is null ? null : RewritePattern(foreachStmt.TargetPattern, state),
                foreachStmt.DeclareLocal,
                iterable,
                foreachStmt.UseIndexValuePair,
                RewriteStatement(foreachStmt.Body, state),
                foreachStmt.Line,
                foreachStmt.Col,
                foreachStmt.OriginFile);
        }

        private MatchStmt RewriteMatchStatement(MatchStmt matchStmt, RewriteState state)
        {
            List<CaseClause> cases = new(matchStmt.Cases.Count);
            foreach (CaseClause clause in matchStmt.Cases)
            {
                MatchPattern rewrittenPattern = RewritePattern(clause.Pattern, state);
                Expr? rewrittenGuard;
                BlockStmt rewrittenBody;

                if (state.LocalScopes.Count > 0)
                {
                    PushLocalScopeCopy(state);
                    foreach (string binding in CollectPatternBindingNames(rewrittenPattern))
                        state.CurrentLocals.Add(binding);

                    try
                    {
                        rewrittenGuard = RewriteNullableExpr(clause.Guard, state);
                        rewrittenBody = RewriteBlock(clause.Body, state);
                    }
                    finally
                    {
                        state.LocalScopes.Pop();
                    }
                }
                else
                {
                    rewrittenGuard = RewriteNullableExpr(clause.Guard, state);
                    rewrittenBody = RewriteBlock(clause.Body, state);
                }

                cases.Add(new CaseClause(rewrittenPattern, rewrittenGuard, rewrittenBody, clause.Line, clause.Col, clause.OriginFile));
            }

            return new MatchStmt(
                RewriteExpr(matchStmt.Expression, state),
                cases,
                matchStmt.DefaultCase is null ? null : RewriteBlock(matchStmt.DefaultCase, state),
                matchStmt.Line,
                matchStmt.Col,
                matchStmt.OriginFile);
        }

        private TryStmt RewriteTryStatement(TryStmt tryStmt, RewriteState state)
        {
            BlockStmt rewrittenTry = RewriteBlock(tryStmt.TryBlock, state);
            BlockStmt? rewrittenCatch = null;

            if (tryStmt.CatchBlock is not null)
            {
                if (state.LocalScopes.Count > 0 && !string.IsNullOrWhiteSpace(tryStmt.CatchIdent))
                {
                    PushLocalScopeCopy(state);
                    state.CurrentLocals.Add(tryStmt.CatchIdent!);
                    try
                    {
                        rewrittenCatch = RewriteBlock(tryStmt.CatchBlock, state);
                    }
                    finally
                    {
                        state.LocalScopes.Pop();
                    }
                }
                else
                {
                    rewrittenCatch = RewriteBlock(tryStmt.CatchBlock, state);
                }
            }

            return new TryStmt(
                rewrittenTry,
                tryStmt.CatchIdent,
                rewrittenCatch,
                tryStmt.FinallyBlock is null ? null : RewriteBlock(tryStmt.FinallyBlock, state),
                tryStmt.Line,
                tryStmt.Col,
                tryStmt.OriginFile);
        }

        private UsingStmt RewriteUsingStatement(UsingStmt usingStmt, RewriteState state)
        {
            Expr rewrittenResource = RewriteExpr(usingStmt.Resource, state);

            if (state.LocalScopes.Count > 0 && !string.IsNullOrWhiteSpace(usingStmt.BindingName))
                state.CurrentLocals.Add(usingStmt.BindingName!);

            return new UsingStmt(
                usingStmt.BindingName,
                usingStmt.BindingIsConst,
                rewrittenResource,
                RewriteBlock(usingStmt.Body, state),
                usingStmt.Line,
                usingStmt.Col,
                usingStmt.OriginFile);
        }

        private FuncDeclStmt RewriteFunctionDecl(FuncDeclStmt funcDecl, RewriteState state)
        {
            List<FunctionParameterSpec> parameterSpecs = RewriteParameterSpecs(funcDecl.ParameterSpecs, state);

            PushFunctionScope(state, funcDecl.Parameters);
            try
            {
                BlockStmt rewrittenBody = RewriteBlock(funcDecl.Body, state);
                return new FuncDeclStmt(
                    funcDecl.Name,
                    funcDecl.Parameters,
                    rewrittenBody,
                    funcDecl.MinArgs,
                    funcDecl.RestParameter,
                    funcDecl.Line,
                    funcDecl.Col,
                    funcDecl.OriginFile,
                    funcDecl.IsAsync,
                    parameterSpecs);
            }
            finally
            {
                state.LocalScopes.Pop();
            }
        }

        private ClassDeclStmt RewriteClassDecl(ClassDeclStmt classDecl, RewriteState state)
        {
            RewriteState classState = new(state.CurrentOrigin, state.UseLookup)
            {
                CurrentNamespacePath = state.CurrentNamespacePath,
                ContainingClassNestedTypeNames = new List<HashSet<string>>(state.ContainingClassNestedTypeNames)
                {
                    new HashSet<string>(classDecl.NestedClasses.Select(static c => c.Name), StringComparer.Ordinal)
                }
            };

            foreach (HashSet<string> locals in state.LocalScopes.Reverse())
                classState.LocalScopes.Push(new HashSet<string>(locals, StringComparer.Ordinal));

            Dictionary<string, Expr?> fields = RewriteFieldMap(classDecl.Fields, classState);
            Dictionary<string, Expr?> staticFields = RewriteFieldMap(classDecl.StaticFields, classState);
            List<FuncDeclStmt> methods = classDecl.Methods.Select(m => RewriteFunctionDecl(m, classState)).ToList();
            List<FuncDeclStmt> staticMethods = classDecl.StaticMethods.Select(m => RewriteFunctionDecl(m, classState)).ToList();
            List<PropertyDeclStmt> properties = classDecl.Properties.Select(p => RewritePropertyDecl(p, classState)).ToList();
            List<PropertyDeclStmt> staticProperties = classDecl.StaticProperties.Select(p => RewritePropertyDecl(p, classState)).ToList();
            List<ClassDeclStmt> nestedClasses = classDecl.NestedClasses.Select(c => RewriteClassDecl(c, classState)).ToList();
            List<Expr> baseCtorArgs = classDecl.BaseCtorArgs.Select(arg => RewriteExpr(arg, classState)).ToList();

            return new ClassDeclStmt(
                classDecl.Name,
                methods,
                properties,
                classDecl.Enums,
                fields,
                staticFields,
                staticMethods,
                staticProperties,
                classDecl.Parameters,
                classDecl.Line,
                classDecl.Col,
                classDecl.OriginFile,
                classDecl.BaseName,
                baseCtorArgs,
                new List<string>(classDecl.ImplementedInterfaces),
                nestedClasses,
                classDecl.IsNested,
                new Dictionary<string, MemberVisibility>(classDecl.FieldVisibility, StringComparer.Ordinal),
                new Dictionary<string, MemberVisibility>(classDecl.StaticFieldVisibility, StringComparer.Ordinal),
                new Dictionary<string, MemberVisibility>(classDecl.MethodVisibility, StringComparer.Ordinal),
                new Dictionary<string, MemberVisibility>(classDecl.StaticMethodVisibility, StringComparer.Ordinal),
                new Dictionary<string, MemberVisibility>(classDecl.EnumVisibility, StringComparer.Ordinal),
                new Dictionary<string, MemberVisibility>(classDecl.NestedClassVisibility, StringComparer.Ordinal),
                new HashSet<string>(classDecl.ConstFields, StringComparer.Ordinal),
                new HashSet<string>(classDecl.StaticConstFields, StringComparer.Ordinal));
        }

        private InterfaceDeclStmt RewriteInterfaceDecl(InterfaceDeclStmt interfaceDecl, RewriteState state)
        {
            return new InterfaceDeclStmt(
                interfaceDecl.Name,
                interfaceDecl.Methods,
                interfaceDecl.Properties,
                new List<string>(interfaceDecl.BaseInterfaces),
                interfaceDecl.Line,
                interfaceDecl.Col,
                interfaceDecl.OriginFile);
        }

        private Dictionary<string, Expr?> RewriteFieldMap(Dictionary<string, Expr?> source, RewriteState state)
        {
            Dictionary<string, Expr?> rewritten = new(StringComparer.Ordinal);
            foreach ((string name, Expr? value) in source)
                rewritten[name] = RewriteNullableExpr(value, state);
            return rewritten;
        }

        private PropertyDeclStmt RewritePropertyDecl(PropertyDeclStmt propertyDecl, RewriteState state)
        {
            List<PropertyAccessorDecl> rewrittenAccessors = propertyDecl.Accessors
                .Select(accessor => RewritePropertyAccessor(accessor, state))
                .ToList();

            return new PropertyDeclStmt(
                propertyDecl.Name,
                propertyDecl.Visibility,
                propertyDecl.IsStatic,
                rewrittenAccessors,
                RewriteNullableExpr(propertyDecl.Initializer, state),
                propertyDecl.HasAutoStorage,
                propertyDecl.Line,
                propertyDecl.Col,
                propertyDecl.OriginFile);
        }

        private PropertyAccessorDecl RewritePropertyAccessor(PropertyAccessorDecl accessor, RewriteState state)
        {
            BlockStmt? rewrittenBody = null;
            if (accessor.Body is not null)
            {
                PushLocalScopeCopy(state);
                try
                {
                    if (!string.IsNullOrWhiteSpace(accessor.ValueParameterName))
                        state.CurrentLocals.Add(accessor.ValueParameterName);

                    rewrittenBody = RewriteBlock(accessor.Body, state);
                }
                finally
                {
                    state.LocalScopes.Pop();
                }
            }

            return new PropertyAccessorDecl(
                accessor.Kind,
                accessor.Visibility,
                accessor.HasExplicitVisibility,
                accessor.ValueParameterName,
                rewrittenBody,
                accessor.IsAuto,
                accessor.Line,
                accessor.Col,
                accessor.OriginFile);
        }

        private List<FunctionParameterSpec> RewriteParameterSpecs(List<FunctionParameterSpec> parameterSpecs, RewriteState state)
        {
            if (parameterSpecs.Count == 0)
                return [];

            HashSet<string> parameterScope = state.LocalScopes.Count > 0
                ? new HashSet<string>(state.CurrentLocals, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);

            List<FunctionParameterSpec> rewritten = new(parameterSpecs.Count);
            foreach (FunctionParameterSpec parameter in parameterSpecs)
            {
                MatchPattern? rewrittenPattern = parameter.DestructurePattern is null
                    ? null
                    : RewritePattern(parameter.DestructurePattern, state);

                PushSyntheticParameterScope(state, parameterScope);
                Expr? rewrittenDefault;
                try
                {
                    rewrittenDefault = RewriteNullableExpr(parameter.DefaultValue, state);
                }
                finally
                {
                    state.LocalScopes.Pop();
                }

                parameterScope.Add(parameter.Name);
                if (rewrittenPattern is not null)
                {
                    foreach (string binding in CollectPatternBindingNames(rewrittenPattern))
                        parameterScope.Add(binding);
                }

                rewritten.Add(new FunctionParameterSpec(
                    parameter.Name,
                    rewrittenPattern,
                    rewrittenDefault,
                    parameter.IsRest,
                    parameter.Line,
                    parameter.Col,
                    parameter.File));
            }

            return rewritten;
        }

        private Expr RewriteExpr(Expr expr, RewriteState state)
        {
            return expr switch
            {
                NumberExpr => expr,
                StringExpr => expr,
                CharExpr => expr,
                BoolExpr => expr,
                NullExpr => expr,
                VarExpr varExpr => RewriteVarExpr(varExpr, state),
                BinaryExpr binaryExpr => new BinaryExpr(
                    RewriteExpr(binaryExpr.Left, state),
                    binaryExpr.Op,
                    RewriteExpr(binaryExpr.Right, state),
                    binaryExpr.Line,
                    binaryExpr.Col,
                    binaryExpr.OriginFile),
                UnaryExpr unaryExpr => new UnaryExpr(unaryExpr.Op, RewriteExpr(unaryExpr.Right, state), unaryExpr.Line, unaryExpr.Col, unaryExpr.OriginFile),
                ArrayExpr arrayExpr => new ArrayExpr(arrayExpr.Elements.Select(element => RewriteExpr(element, state)).ToList(), arrayExpr.Line, arrayExpr.Col, arrayExpr.OriginFile),
                IndexExpr indexExpr => new IndexExpr(
                    indexExpr.Target is null ? null : RewriteExpr(indexExpr.Target, state),
                    indexExpr.Index is null ? null : RewriteExpr(indexExpr.Index, state),
                    indexExpr.Line,
                    indexExpr.Col,
                    indexExpr.OriginFile,
                    indexExpr.IsDotAccess),
                SliceExpr sliceExpr => new SliceExpr(
                    sliceExpr.Target is null ? null : RewriteExpr(sliceExpr.Target, state),
                    RewriteNullableExpr(sliceExpr.Start, state),
                    RewriteNullableExpr(sliceExpr.End, state),
                    sliceExpr.Line,
                    sliceExpr.Col,
                    sliceExpr.OriginFile),
                DictExpr dictExpr => new DictExpr(
                    dictExpr.Pairs
                        .Select(pair => (RewriteExpr(pair.Item1, state), RewriteExpr(pair.Item2, state)))
                        .ToList(),
                    dictExpr.Line,
                    dictExpr.Col,
                    dictExpr.OriginFile),
                PrefixExpr prefixExpr => new PrefixExpr(RewriteNullableExpr(prefixExpr.Target, state), prefixExpr.Op, prefixExpr.Line, prefixExpr.Col, prefixExpr.OriginFile),
                PostfixExpr postfixExpr => new PostfixExpr(RewriteNullableExpr(postfixExpr.Target, state), postfixExpr.Op, postfixExpr.Line, postfixExpr.Col, postfixExpr.OriginFile),
                TryUnwrapExpr tryUnwrapExpr => new TryUnwrapExpr(RewriteNullableExpr(tryUnwrapExpr.Inner, state), tryUnwrapExpr.Line, tryUnwrapExpr.Col, tryUnwrapExpr.OriginFile),
                NewExpr newExpr => RewriteNewExpr(newExpr, state),
                GetFieldExpr getFieldExpr => new GetFieldExpr(RewriteExpr(getFieldExpr.Target, state), getFieldExpr.Field, getFieldExpr.Line, getFieldExpr.Col, getFieldExpr.OriginFile),
                OutExpr outExpr => new OutExpr(RewriteBlock(outExpr.Body, state), outExpr.Line, outExpr.Col, outExpr.OriginFile),
                ConditionalExpr conditionalExpr => new ConditionalExpr(
                    RewriteExpr(conditionalExpr.Condition, state),
                    RewriteExpr(conditionalExpr.ThenExpr, state),
                    RewriteExpr(conditionalExpr.ElseExpr, state),
                    conditionalExpr.Line,
                    conditionalExpr.Col,
                    conditionalExpr.OriginFile),
                MatchExpr matchExpr => RewriteMatchExpr(matchExpr, state),
                AwaitExpr awaitExpr => new AwaitExpr(RewriteExpr(awaitExpr.Inner, state), awaitExpr.Line, awaitExpr.Col, awaitExpr.OriginFile),
                FuncExpr funcExpr => RewriteFunctionExpr(funcExpr, state),
                NamedArgExpr namedArgExpr => new NamedArgExpr(namedArgExpr.Name, RewriteExpr(namedArgExpr.Value, state), namedArgExpr.Line, namedArgExpr.Col, namedArgExpr.OriginFile),
                SpreadArgExpr spreadArgExpr => new SpreadArgExpr(RewriteExpr(spreadArgExpr.Value, state), spreadArgExpr.Line, spreadArgExpr.Col, spreadArgExpr.OriginFile),
                CallExpr callExpr => new CallExpr(
                    RewriteNullableExpr(callExpr.Target, state),
                    callExpr.Args.Select(arg => RewriteExpr(arg, state)).ToList(),
                    callExpr.Line,
                    callExpr.Col,
                    callExpr.OriginFile),
                ObjectInitExpr objectInitExpr => new ObjectInitExpr(
                    RewriteExpr(objectInitExpr.Target, state),
                    objectInitExpr.Inits.Select(init => (init.Item1, RewriteExpr(init.Item2, state))).ToList(),
                    objectInitExpr.Line,
                    objectInitExpr.Col,
                    objectInitExpr.OriginFile),
                MethodCallExpr methodCallExpr => new MethodCallExpr(
                    RewriteExpr(methodCallExpr.Target, state),
                    methodCallExpr.Method,
                    methodCallExpr.Args.Select(arg => RewriteExpr(arg, state)).ToList(),
                    methodCallExpr.Line,
                    methodCallExpr.Col,
                    methodCallExpr.OriginFile),
                _ => expr
            };
        }

        private FuncExpr RewriteFunctionExpr(FuncExpr funcExpr, RewriteState state)
        {
            List<FunctionParameterSpec> parameterSpecs = RewriteParameterSpecs(funcExpr.ParameterSpecs, state);

            PushFunctionScope(state, funcExpr.Parameters);
            try
            {
                return new FuncExpr(
                    funcExpr.Parameters,
                    RewriteBlock(funcExpr.Body, state),
                    funcExpr.MinArgs,
                    funcExpr.RestParameter,
                    funcExpr.Line,
                    funcExpr.Col,
                    funcExpr.OriginFile,
                    funcExpr.IsAsync,
                    parameterSpecs);
            }
            finally
            {
                state.LocalScopes.Pop();
            }
        }

        private MatchExpr RewriteMatchExpr(MatchExpr matchExpr, RewriteState state)
        {
            List<CaseExprArm> arms = new(matchExpr.Arms.Count);
            foreach (CaseExprArm arm in matchExpr.Arms)
            {
                MatchPattern rewrittenPattern = RewritePattern(arm.Pattern, state);
                Expr? rewrittenGuard;
                Expr rewrittenBody;

                if (state.LocalScopes.Count > 0)
                {
                    PushLocalScopeCopy(state);
                    foreach (string binding in CollectPatternBindingNames(rewrittenPattern))
                        state.CurrentLocals.Add(binding);

                    try
                    {
                        rewrittenGuard = RewriteNullableExpr(arm.Guard, state);
                        rewrittenBody = RewriteExpr(arm.Body, state);
                    }
                    finally
                    {
                        state.LocalScopes.Pop();
                    }
                }
                else
                {
                    rewrittenGuard = RewriteNullableExpr(arm.Guard, state);
                    rewrittenBody = RewriteExpr(arm.Body, state);
                }

                arms.Add(new CaseExprArm(rewrittenPattern, rewrittenGuard, rewrittenBody, arm.Line, arm.Col, arm.OriginFile));
            }

            return new MatchExpr(
                RewriteExpr(matchExpr.Scrutinee, state),
                arms,
                RewriteNullableExpr(matchExpr.DefaultArm, state),
                matchExpr.Line,
                matchExpr.Col,
                matchExpr.OriginFile);
        }

        private NewExpr RewriteNewExpr(NewExpr newExpr, RewriteState state)
        {
            string rewrittenClassName = RewriteTypeReference(newExpr.ClassName, newExpr, state, NamespaceMemberKind.Class, NamespaceMemberKind.Namespace) ?? newExpr.ClassName;
            return new NewExpr(
                rewrittenClassName,
                newExpr.Args.Select(arg => RewriteExpr(arg, state)).ToList(),
                newExpr.Initializers.Select(init => (init.Name, RewriteExpr(init.Value, state))).ToList(),
                newExpr.Line,
                newExpr.Col,
                newExpr.OriginFile);
        }

        private MatchPattern RewritePattern(MatchPattern pattern, RewriteState state)
        {
            return pattern switch
            {
                WildcardMatchPattern => pattern,
                BindingMatchPattern => pattern,
                ValueMatchPattern valuePattern => new ValueMatchPattern(RewriteExpr(valuePattern.Value, state), valuePattern.Line, valuePattern.Col, valuePattern.OriginFile),
                ArrayMatchPattern arrayPattern => new ArrayMatchPattern(arrayPattern.Elements.Select(element => RewritePattern(element, state)).ToList(), arrayPattern.Line, arrayPattern.Col, arrayPattern.OriginFile),
                DictMatchPattern dictPattern => new DictMatchPattern(dictPattern.Entries.Select(entry => (entry.Key, RewritePattern(entry.Pattern, state))).ToList(), dictPattern.Line, dictPattern.Col, dictPattern.OriginFile),
                _ => pattern
            };
        }

        private Expr RewriteVarExpr(VarExpr varExpr, RewriteState state)
        {
            if (!TryResolveUsedValueRoot(varExpr.Name, varExpr, state, out NamespaceMemberInfo? resolved))
                return new VarExpr(varExpr.Name, varExpr.Line, varExpr.Col, varExpr.OriginFile);

            return BuildQualifiedAccessExpr(resolved!.QualifiedPath, varExpr);
        }

        private Expr? RewriteNullableExpr(Expr? expr, RewriteState state)
            => expr is null ? null : RewriteExpr(expr, state);

        private string? RewriteTypeReference(string? typeName, Node node, RewriteState state, params NamespaceMemberKind[] allowedRootKinds)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return typeName;

            string[] parts = typeName.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return typeName;

            string root = parts[0];
            if (IsShadowedTypeRoot(root, state))
                return typeName;

            if (!TryResolveUseMember(root, node, state, out List<NamespaceMemberInfo>? candidates))
                return typeName;

            List<NamespaceMemberInfo> filtered = candidates!
                .Where(candidate => allowedRootKinds.Contains(candidate.Kind))
                .ToList();

            if (filtered.Count == 0)
                return typeName;

            if (filtered.Count > 1)
                throw CreateAmbiguousUseException(root, filtered, node);

            string suffix = parts.Length > 1 ? "." + string.Join(".", parts.Skip(1)) : string.Empty;
            return filtered[0].QualifiedPath + suffix;
        }

        private bool TryResolveUsedValueRoot(string name, Node node, RewriteState state, out NamespaceMemberInfo? resolved)
        {
            resolved = null;

            if (IsSpecialIdentifier(name) || IsShadowedValueRoot(name, state))
                return false;

            if (!TryResolveUseMember(name, node, state, out List<NamespaceMemberInfo>? candidates))
                return false;

            if (candidates!.Count > 1)
                throw CreateAmbiguousUseException(name, candidates, node);

            resolved = candidates[0];
            return true;
        }

        private bool TryResolveUseMember(string name, Node node, RewriteState state, out List<NamespaceMemberInfo>? candidates)
        {
            candidates = null;
            if (!state.UseLookup.TryGetValue(name, out List<NamespaceMemberInfo>? bucket) || bucket.Count == 0)
                return false;

            candidates = bucket;
            return true;
        }

        private bool IsShadowedValueRoot(string name, RewriteState state)
        {
            if (state.CurrentLocals.Contains(name))
                return true;

            if (!string.IsNullOrWhiteSpace(state.CurrentNamespacePath) &&
                _namespaceDeclaredValueNamesByPath.TryGetValue(state.CurrentNamespacePath, out HashSet<string>? namespaceNames) &&
                namespaceNames.Contains(name))
            {
                return true;
            }

            return _globalTopLevelValueNames.Contains(name);
        }

        private bool IsShadowedTypeRoot(string name, RewriteState state)
        {
            foreach (HashSet<string> nestedTypeNames in state.ContainingClassNestedTypeNames)
            {
                if (nestedTypeNames.Contains(name))
                    return true;
            }

            if (!string.IsNullOrWhiteSpace(state.CurrentNamespacePath))
            {
                foreach (string namespacePath in EnumerateNamespacePrefixes(state.CurrentNamespacePath))
                {
                    if (_namespaceDeclaredTypeNamesByPath.TryGetValue(namespacePath, out HashSet<string>? namespaceTypeNames) &&
                        namespaceTypeNames.Contains(name))
                    {
                        return true;
                    }
                }
            }

            return _globalTopLevelTypeNames.Contains(name);
        }

        private static IEnumerable<string> EnumerateNamespacePrefixes(string namespacePath)
        {
            string current = namespacePath;
            while (!string.IsNullOrWhiteSpace(current))
            {
                yield return current;
                int lastDot = current.LastIndexOf('.');
                if (lastDot <= 0)
                    yield break;
                current = current[..lastDot];
            }
        }

        private static ParserException CreateAmbiguousUseException(string name, IEnumerable<NamespaceMemberInfo> candidates, Node node)
        {
            string targets = string.Join(", ", candidates.Select(static candidate => $"'{candidate.QualifiedPath}'"));
            return new ParserException(
                $"ambiguous use reference '{name}': matches {targets}",
                node.Line,
                node.Col,
                node.OriginFile);
        }

        private static Expr BuildQualifiedAccessExpr(string qualifiedPath, Node node)
        {
            string[] parts = qualifiedPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
            Expr expr = new VarExpr(parts[0], node.Line, node.Col, node.OriginFile);
            for (int i = 1; i < parts.Length; i++)
            {
                expr = new IndexExpr(expr, new StringExpr(parts[i], node.Line, node.Col, node.OriginFile), node.Line, node.Col, node.OriginFile, isDotAccess: true);
            }

            return expr;
        }

        private static void PushFunctionScope(RewriteState state, IEnumerable<string> parameters)
        {
            HashSet<string> locals = state.LocalScopes.Count > 0
                ? new HashSet<string>(state.CurrentLocals, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);

            foreach (string parameter in parameters)
                locals.Add(parameter);

            state.LocalScopes.Push(locals);
        }

        private static void PushLocalScopeCopy(RewriteState state)
        {
            HashSet<string> locals = state.LocalScopes.Count > 0
                ? new HashSet<string>(state.CurrentLocals, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);

            state.LocalScopes.Push(locals);
        }

        private static void PushSyntheticParameterScope(RewriteState state, HashSet<string> locals)
            => state.LocalScopes.Push(new HashSet<string>(locals, StringComparer.Ordinal));

        private static bool IsSpecialIdentifier(string name)
            => name is "this" or "type" or "super" or "outer" or "field";

        private static IEnumerable<string> CollectPatternBindingNames(MatchPattern pattern)
        {
            switch (pattern)
            {
                case BindingMatchPattern bindingPattern:
                    yield return bindingPattern.Name;
                    yield break;

                case ArrayMatchPattern arrayPattern:
                    foreach (MatchPattern element in arrayPattern.Elements)
                    {
                        foreach (string binding in CollectPatternBindingNames(element))
                            yield return binding;
                    }
                    yield break;

                case DictMatchPattern dictPattern:
                    foreach ((string _, MatchPattern nestedPattern) in dictPattern.Entries)
                    {
                        foreach (string binding in CollectPatternBindingNames(nestedPattern))
                            yield return binding;
                    }
                    yield break;
            }
        }
    }
}
