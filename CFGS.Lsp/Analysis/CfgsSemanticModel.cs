using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using CFGS_VM.Analytic.Tree;

namespace CFGS.Lsp;

internal sealed record CfgsSemanticModel(
    IReadOnlyList<CfgsSymbol> Symbols,
    IReadOnlyDictionary<string, CfgsSymbol> SymbolsById,
    IReadOnlyList<CfgsResolvedOccurrence> Occurrences,
    IReadOnlyList<CfgsResolvedCallSite> CallSites,
    IReadOnlyList<CfgsResolvedCompletionTarget> CompletionTargets);

internal sealed record CfgsResolvedOccurrence(
    string Uri,
    RangeInfo Range,
    string SymbolId,
    bool IsDeclaration);

internal sealed record CfgsResolvedCallSite(
    string Uri,
    RangeInfo Range,
    IReadOnlyList<string> SymbolIds);

internal sealed record CfgsResolvedCompletionTarget(
    string Uri,
    PositionInfo End,
    IReadOnlyList<CfgsCompletionItem> Items);

internal sealed record CfgsSignature(
    string Label,
    IReadOnlyList<string> Parameters,
    string? RestParameter,
    int MinArgs);

internal sealed record CfgsSignatureHelpResult(
    IReadOnlyList<CfgsSignature> Signatures,
    int ActiveSignature,
    int ActiveParameter);

internal readonly record struct SignatureRequest(
    int TargetLine,
    int TargetCharacter,
    int ActiveParameter);

internal sealed class CfgsSemanticModelBuilder
{
    private const int MaxLoopFixpointIterations = 16;
    private readonly SemanticSourceResolver _sources;
    private readonly string _documentUri;
    private readonly Dictionary<object, CfgsSymbol> _symbolsByNode = new(ReferenceEqualityComparer.Instance);
    private readonly List<CfgsSymbol> _symbols = [];
    private readonly Dictionary<string, CfgsSymbol> _symbolsById = new(StringComparer.Ordinal);
    private readonly List<CfgsResolvedOccurrence> _occurrences = [];
    private readonly Dictionary<string, HashSet<string>> _callSiteSymbolIdsByKey = new(StringComparer.Ordinal);
    private readonly List<CfgsResolvedCallSite> _callSites = [];
    private readonly Dictionary<string, Dictionary<string, CfgsCompletionItem>> _completionItemsByAnchorKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<CfgsCompletionItem>> _completionItemsByPathMode = new(StringComparer.Ordinal);
    private readonly List<CfgsResolvedCompletionTarget> _completionTargets = [];
    private readonly Dictionary<string, string> _memberTypesByQualifiedName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CfgsSymbol> _memberSymbolAliasesByQualifiedName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _memberAccessPathsByQualifiedName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, ClassMemberBinding>> _instanceMembersByType = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, ClassMemberBinding>> _staticMembersByType = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _baseTypeByType = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _containerBaseTypeByPath = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _functionReturnCallTargetIdsBySymbolId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _functionReturnAccessPathsBySymbolId = new(StringComparer.Ordinal);
    private readonly Dictionary<object, string> _accessPathsByNode = new(ReferenceEqualityComparer.Instance);
    private readonly Stack<FunctionFlowContext> _functionContexts = new();
    private int _flowOnlyDepth;
    private int _nextSymbolId;
    private int _nextAccessPathId;

    public CfgsSemanticModelBuilder(string documentUri, string documentOrigin, string sourceText, IReadOnlyDictionary<string, string>? openDocumentSources = null)
    {
        _documentUri = documentUri;
        _sources = new SemanticSourceResolver(documentUri, documentOrigin, sourceText, openDocumentSources);
    }

    public CfgsSemanticModel Build(IEnumerable<Stmt> statements)
    {
        SemanticScope rootScope = new(null);
        PredeclareStatements(statements, rootScope, null);

        foreach (Stmt stmt in statements)
            VisitStatement(stmt, rootScope, null);

        FinalizeCallSites();
        FinalizeCompletionTargets();
        return new CfgsSemanticModel(_symbols, _symbolsById, _occurrences, _callSites, _completionTargets);
    }

    private void PredeclareStatements(IEnumerable<Stmt> statements, SemanticScope scope, string? containerQualifiedName)
    {
        foreach (Stmt stmt in statements)
        {
            if (stmt is BlockStmt block && block.IsNamespaceScope)
            {
                if (TryExtractNamespaceInfo(block, out string? namespacePath, out string? tempName))
                {
                    List<Stmt> bodyStatements = EnumerateNamespaceBodyStatements(block, tempName!).ToList();
                    SemanticScope namespaceScope = new(scope);
                    PredeclareStatements(bodyStatements, namespaceScope, namespacePath);
                }
                continue;
            }

            PredeclareStatement(stmt, scope, containerQualifiedName);
        }
    }

    private void PredeclareStatement(Stmt stmt, SemanticScope scope, string? containerQualifiedName)
    {
        Stmt effective = stmt is ExportStmt export ? export.Inner : stmt;
        switch (effective)
        {
            case FuncDeclStmt func:
                DeclareFunctionSymbol(func, scope, containerQualifiedName, isMethod: false, synthetic: false);
                break;

            case ClassDeclStmt @class:
                DeclareClassSymbol(@class, scope, containerQualifiedName, synthetic: false);
                break;

            case InterfaceDeclStmt @interface:
                DeclareInterfaceSymbol(@interface, scope, containerQualifiedName, synthetic: false);
                break;

            case EnumDeclStmt @enum:
                DeclareEnumSymbol(@enum, scope, containerQualifiedName, synthetic: false);
                break;
        }
    }

    private CompletionResult VisitStatement(Stmt stmt, SemanticScope scope, string? containerQualifiedName)
    {
        if (stmt is ExportStmt export)
            return VisitStatement(export.Inner, scope, containerQualifiedName);

        if (stmt is BlockStmt block && block.IsNamespaceScope)
        {
            VisitNamespaceScope(block, scope);
            return CompletionResult.Fallthrough();
        }

        switch (stmt)
        {
            case EmptyStmt:
            case YieldStmt:
                return CompletionResult.Fallthrough();

            case BreakStmt:
                return CompletionResult.WithBreak(CaptureCompletionScope(scope));

            case ContinueStmt:
                return CompletionResult.WithContinue(CaptureCompletionScope(scope));

            case ExprStmt exprStmt:
                VisitExpr(exprStmt.Expression, scope);
                return CompletionResult.Fallthrough();

            case VarDecl varDecl:
                VisitVarDecl(varDecl, scope, containerQualifiedName);
                return CompletionResult.Fallthrough();

            case ConstDecl constDecl:
                VisitConstDecl(constDecl, scope, containerQualifiedName);
                return CompletionResult.Fallthrough();

            case UsingStmt usingStmt:
                return VisitUsingStmt(usingStmt, scope, containerQualifiedName);

            case DestructureDeclStmt destructureDecl:
                VisitExpr(destructureDecl.Value, scope);
                BindPattern(destructureDecl.Pattern, scope, declareBindings: true, containerQualifiedName);
                return CompletionResult.Fallthrough();

            case DestructureAssignStmt destructureAssign:
                VisitExpr(destructureAssign.Value, scope);
                BindPattern(destructureAssign.Pattern, scope, declareBindings: false, containerQualifiedName);
                return CompletionResult.Fallthrough();

            case AssignStmt assignStmt:
                RecordNamedReference(assignStmt.Name, assignStmt.Line, assignStmt.Col, assignStmt.OriginFile, scope);
                VisitExpr(assignStmt.Value, scope);
                scope.SetResolvedValueType(assignStmt.Name, TryInferValueTypeQualifiedName(assignStmt.Value, scope));
                scope.SetResolvedCallTargetIds(assignStmt.Name, TryResolveCallableSymbols(assignStmt.Value, scope).Select(static symbol => symbol.Id!).Where(static id => !string.IsNullOrWhiteSpace(id)));
                scope.SetResolvedAccessPaths(assignStmt.Name, TryResolveContainerAccessPaths(assignStmt.Value, scope));
                return CompletionResult.Fallthrough();

            case AssignIndexExprStmt assignIndexExpr:
                VisitExpr(assignIndexExpr.Target, scope);
                VisitExpr(assignIndexExpr.Value, scope);
                TrackAssignedMemberBinding(assignIndexExpr.Target, assignIndexExpr.Value, scope);
                return CompletionResult.Fallthrough();

            case AssignExprStmt assignExpr:
                VisitExpr(assignExpr.Target, scope);
                VisitExpr(assignExpr.Value, scope);
                if (assignExpr.Target is VarExpr targetVar)
                {
                    scope.SetResolvedValueType(targetVar.Name, TryInferValueTypeQualifiedName(assignExpr.Value, scope));
                    scope.SetResolvedCallTargetIds(targetVar.Name, TryResolveCallableSymbols(assignExpr.Value, scope).Select(static symbol => symbol.Id!).Where(static id => !string.IsNullOrWhiteSpace(id)));
                    scope.SetResolvedAccessPaths(targetVar.Name, TryResolveContainerAccessPaths(assignExpr.Value, scope));
                }
                else
                    TrackAssignedMemberBinding(assignExpr.Target, assignExpr.Value, scope);
                return CompletionResult.Fallthrough();

            case CompoundAssignStmt compoundAssign:
                VisitExpr(compoundAssign.Target, scope);
                VisitExpr(compoundAssign.Value, scope);
                return CompletionResult.Fallthrough();

            case SliceSetStmt sliceSet:
                VisitExpr(sliceSet.Slice, scope);
                VisitExpr(sliceSet.Value, scope);
                return CompletionResult.Fallthrough();

            case PushStmt pushStmt:
                VisitExpr(pushStmt.Target, scope);
                VisitExpr(pushStmt.Value, scope);
                return CompletionResult.Fallthrough();

            case DeleteVarStmt deleteVar:
                RecordNamedReference(deleteVar.Name, deleteVar.Line, deleteVar.Col, deleteVar.OriginFile, scope);
                return CompletionResult.Fallthrough();

            case DeleteIndexStmt deleteIndex:
                RecordNamedReference(deleteIndex.Name, deleteIndex.Line, deleteIndex.Col, deleteIndex.OriginFile, scope);
                VisitExpr(deleteIndex.Index, scope);
                return CompletionResult.Fallthrough();

            case DeleteAllStmt deleteAll:
                VisitExpr(deleteAll.Target, scope);
                return CompletionResult.Fallthrough();

            case DeleteExprStmt deleteExpr:
                VisitExpr(deleteExpr.Target, scope);
                return CompletionResult.Fallthrough();

            case FuncDeclStmt func:
                VisitFunctionDecl(func, scope);
                return CompletionResult.Fallthrough();

            case ClassDeclStmt @class:
                VisitClassDecl(@class, scope, containerQualifiedName);
                return CompletionResult.Fallthrough();

            case InterfaceDeclStmt @interface:
                VisitInterfaceDecl(@interface, scope, containerQualifiedName);
                return CompletionResult.Fallthrough();

            case EnumDeclStmt @enum:
                VisitEnumDecl(@enum, scope, containerQualifiedName);
                return CompletionResult.Fallthrough();

            case IfStmt ifStmt:
                return VisitIfStmt(ifStmt, scope, containerQualifiedName);

            case WhileStmt whileStmt:
                return VisitWhileStmt(whileStmt, scope, containerQualifiedName);

            case DoWhileStmt doWhileStmt:
                return VisitDoWhileStmt(doWhileStmt, scope, containerQualifiedName);

            case ForStmt forStmt:
                return VisitForStmt(forStmt, scope, containerQualifiedName);

            case ForeachStmt foreachStmt:
                return VisitForeachStmt(foreachStmt, scope, containerQualifiedName);

            case MatchStmt matchStmt:
                return VisitMatchStmt(matchStmt, scope, containerQualifiedName);

            case TryStmt tryStmt:
                return VisitTryStmt(tryStmt, scope, containerQualifiedName);

            case ThrowStmt throwStmt:
                VisitExpr(throwStmt.Value, scope);
                return CompletionResult.WithThrow(CaptureCompletionScope(scope));

            case ReturnStmt returnStmt:
                if (returnStmt.Value is not null)
                    VisitExpr(returnStmt.Value, scope);

                return CompletionResult.WithReturn(CaptureReturnFlow(returnStmt.Value, scope));

            case SetFieldStmt setField:
                VisitExpr(setField.Target, scope);
                RecordQualifiedMemberReference(setField.Target, setField.Field, setField.Line, setField.Col, setField.OriginFile, scope);
                VisitExpr(setField.Value, scope);
                TrackAssignedMemberBinding(new GetFieldExpr(setField.Target, setField.Field, setField.Line, setField.Col, setField.OriginFile), setField.Value, scope);
                return CompletionResult.Fallthrough();

            default:
                return CompletionResult.Fallthrough();
        }
    }

    private void VisitNamespaceScope(BlockStmt block, SemanticScope parentScope)
    {
        if (!TryExtractNamespaceInfo(block, out string? namespacePath, out string? tempName))
            return;

        List<Stmt> bodyStatements = EnumerateNamespaceBodyStatements(block, tempName!).ToList();
        SemanticScope namespaceScope = new(parentScope);
        PredeclareStatements(bodyStatements, namespaceScope, namespacePath);

        foreach (Stmt bodyStmt in bodyStatements)
            VisitStatement(bodyStmt, namespaceScope, namespacePath);
    }

    private void VisitVarDecl(VarDecl varDecl, SemanticScope scope, string? containerQualifiedName)
    {
        if (varDecl.Value is not null)
            VisitExpr(varDecl.Value, scope);

        bool synthetic = IsSyntheticGeneratedName(varDecl.Name);
        string? valueTypeQualifiedName = TryInferValueTypeQualifiedName(varDecl.Value, scope);
        DeclareValueSymbol(varDecl, scope, containerQualifiedName, 13, "var", synthetic, TryGetFunctionSignature(varDecl.Value, scope), valueTypeQualifiedName, callTargetSymbolId: null);
        scope.SetResolvedCallTargetIds(varDecl.Name, TryResolveCallableSymbols(varDecl.Value, scope).Select(static symbol => symbol.Id!).Where(static id => !string.IsNullOrWhiteSpace(id)));
        scope.SetResolvedAccessPaths(varDecl.Name, TryResolveContainerAccessPaths(varDecl.Value, scope));
    }

    private void VisitConstDecl(ConstDecl constDecl, SemanticScope scope, string? containerQualifiedName)
    {
        VisitExpr(constDecl.Value, scope);
        bool synthetic = IsSyntheticGeneratedName(constDecl.Name);
        string? valueTypeQualifiedName = TryInferValueTypeQualifiedName(constDecl.Value, scope);
        CfgsSymbol? callTargetSymbol = TryResolveCallableSymbol(constDecl.Value, scope);
        DeclareValueSymbol(constDecl, scope, containerQualifiedName, 14, "const", synthetic, TryGetFunctionSignature(constDecl.Value, scope), valueTypeQualifiedName, callTargetSymbol?.Id);
        scope.SetResolvedCallTargetIds(constDecl.Name, TryResolveCallableSymbols(constDecl.Value, scope).Select(static symbol => symbol.Id!).Where(static id => !string.IsNullOrWhiteSpace(id)));
        scope.SetResolvedAccessPaths(constDecl.Name, TryResolveContainerAccessPaths(constDecl.Value, scope));
    }

    private void VisitFunctionDecl(FuncDeclStmt func, SemanticScope scope)
    {
        CfgsSymbol? functionSymbol = _symbolsByNode.GetValueOrDefault(func);
        FunctionFlowContext? functionContext = BeginFunctionFlow(functionSymbol);
        SemanticScope functionScope = new(scope, inheritReceiverContext: false);
        DeclareParameters(func.Parameters, func.RestParameter, functionScope, func.OriginFile, func.Line, func.Col);
        CompletionResult functionCompletion = VisitBlockStatements(func.Body.Statements, functionScope, null);
        RecordReturnCompletions(functionCompletion);
        EndFunctionFlow(functionContext);
    }

    private void VisitClassDecl(ClassDeclStmt classDecl, SemanticScope scope, string? containerQualifiedName)
    {
        CfgsSymbol classSymbol = DeclareClassSymbol(classDecl, scope, containerQualifiedName, synthetic: false);
        string qualifiedClassName = classSymbol.QualifiedName;
        string? baseQualifiedName = ResolveBaseQualifiedName(classDecl, containerQualifiedName);
        _baseTypeByType[qualifiedClassName] = baseQualifiedName;

        if (!string.IsNullOrWhiteSpace(classDecl.BaseName))
            RecordTypeReference(classDecl.BaseName, classDecl.Line, classDecl.Col, classDecl.OriginFile, scope);

        foreach (string interfaceName in classDecl.ImplementedInterfaces)
            RecordTypeReference(interfaceName, classDecl.Line, classDecl.Col, classDecl.OriginFile, scope);

        foreach ((string fieldName, Expr? fieldValue) in classDecl.Fields)
        {
            bool isConst = classDecl.ConstFields.Contains(fieldName);
            string? valueTypeQualifiedName = TryInferValueTypeQualifiedName(fieldValue, scope);
            CfgsSymbol? callTargetSymbol = TryResolveCallableSymbol(fieldValue, scope);
            CfgsSymbol fieldSymbol = DeclareClassFieldSymbol(classDecl, fieldName, qualifiedClassName, isStatic: false, isConst, TryGetFunctionSignature(fieldValue, scope), valueTypeQualifiedName, isConst ? callTargetSymbol?.Id : null);
            RegisterClassMember(qualifiedClassName, fieldName, fieldSymbol, isStatic: false, valueTypeQualifiedName);
        }

        foreach ((string staticFieldName, Expr? staticFieldValue) in classDecl.StaticFields)
        {
            bool isConst = classDecl.StaticConstFields.Contains(staticFieldName);
            string? valueTypeQualifiedName = TryInferValueTypeQualifiedName(staticFieldValue, scope);
            CfgsSymbol? callTargetSymbol = TryResolveCallableSymbol(staticFieldValue, scope);
            CfgsSymbol fieldSymbol = DeclareClassFieldSymbol(classDecl, staticFieldName, qualifiedClassName, isStatic: true, isConst, TryGetFunctionSignature(staticFieldValue, scope), valueTypeQualifiedName, isConst ? callTargetSymbol?.Id : null);
            RegisterClassMember(qualifiedClassName, staticFieldName, fieldSymbol, isStatic: true, valueTypeQualifiedName);
        }

        foreach (FuncDeclStmt method in classDecl.Methods)
        {
            CfgsSymbol methodSymbol = DeclareFunctionSymbol(method, scope, qualifiedClassName, isMethod: true, synthetic: false);
            RegisterClassMember(qualifiedClassName, method.Name, methodSymbol, isStatic: false, valueTypeQualifiedName: null);
        }

        foreach (FuncDeclStmt staticMethod in classDecl.StaticMethods)
        {
            CfgsSymbol methodSymbol = DeclareFunctionSymbol(staticMethod, scope, qualifiedClassName, isMethod: true, synthetic: false);
            RegisterClassMember(qualifiedClassName, staticMethod.Name, methodSymbol, isStatic: true, valueTypeQualifiedName: null);
        }

        foreach (ClassDeclStmt nestedClass in classDecl.NestedClasses)
        {
            CfgsSymbol nestedClassSymbol = DeclareClassSymbol(nestedClass, scope, qualifiedClassName, synthetic: false);
            RegisterClassMember(qualifiedClassName, nestedClass.Name, nestedClassSymbol, isStatic: true, valueTypeQualifiedName: nestedClassSymbol.QualifiedName);
        }

        foreach (EnumDeclStmt enumDecl in classDecl.Enums)
        {
            CfgsSymbol enumSymbol = DeclareEnumSymbol(enumDecl, scope, qualifiedClassName, synthetic: false);
            RegisterClassMember(qualifiedClassName, enumDecl.Name, enumSymbol, isStatic: true, valueTypeQualifiedName: enumSymbol.QualifiedName);
        }

        foreach ((string fieldName, Expr? fieldValue) in classDecl.Fields)
        {
            if (fieldValue is not null)
                VisitExpr(fieldValue, scope);
            TrackMemberValueType(qualifiedClassName, fieldName, fieldValue, scope);
        }

        foreach ((string staticFieldName, Expr? staticFieldValue) in classDecl.StaticFields)
        {
            if (staticFieldValue is not null)
                VisitExpr(staticFieldValue, scope);
            TrackMemberValueType(qualifiedClassName, staticFieldName, staticFieldValue, scope);
        }

        string? outerClassQualifiedName = classDecl.IsNested ? containerQualifiedName : null;

        foreach (FuncDeclStmt staticMethod in classDecl.StaticMethods)
            VisitMethodBody(staticMethod, scope, qualifiedClassName, baseQualifiedName, outerClassQualifiedName, isStaticMethod: true);

        foreach (FuncDeclStmt method in classDecl.Methods)
            VisitMethodBody(method, scope, qualifiedClassName, baseQualifiedName, outerClassQualifiedName, isStaticMethod: false);

        foreach (EnumDeclStmt enumDecl in classDecl.Enums)
            VisitEnumDecl(enumDecl, scope, qualifiedClassName);

        foreach (ClassDeclStmt nestedClass in classDecl.NestedClasses)
            VisitClassDecl(nestedClass, scope, qualifiedClassName);
    }

    private void VisitInterfaceDecl(InterfaceDeclStmt interfaceDecl, SemanticScope scope, string? containerQualifiedName)
    {
        CfgsSymbol interfaceSymbol = DeclareInterfaceSymbol(interfaceDecl, scope, containerQualifiedName, synthetic: false);
        string qualifiedInterfaceName = interfaceSymbol.QualifiedName;

        foreach (string baseInterfaceName in interfaceDecl.BaseInterfaces)
            RecordTypeReference(baseInterfaceName, interfaceDecl.Line, interfaceDecl.Col, interfaceDecl.OriginFile, scope);

        foreach (InterfaceMethodDecl method in interfaceDecl.Methods)
            DeclareInterfaceMethodSymbol(method, qualifiedInterfaceName, synthetic: false);
    }

    private void VisitMethodBody(
        FuncDeclStmt method,
        SemanticScope parentScope,
        string classQualifiedName,
        string? baseQualifiedName,
        string? outerClassQualifiedName,
        bool isStaticMethod)
    {
        SemanticScope implicitMemberScope = CreateImplicitMemberScope(parentScope, classQualifiedName, isStaticMethod);
        SemanticScope methodScope = new(
            implicitMemberScope,
            currentClassQualifiedName: classQualifiedName,
            currentBaseQualifiedName: baseQualifiedName,
            currentOuterClassQualifiedName: outerClassQualifiedName,
            allowThis: !isStaticMethod,
            allowType: true,
            allowSuper: !string.IsNullOrWhiteSpace(baseQualifiedName),
            allowOuter: !isStaticMethod && !string.IsNullOrWhiteSpace(outerClassQualifiedName),
            inheritReceiverContext: false);
        FunctionFlowContext? functionContext = BeginFunctionFlow(_symbolsByNode.GetValueOrDefault(method));
        DeclareParameters(method.Parameters, method.RestParameter, methodScope, method.OriginFile, method.Line, method.Col);
        CompletionResult methodCompletion = VisitBlockStatements(method.Body.Statements, methodScope, null);
        RecordReturnCompletions(methodCompletion);
        EndFunctionFlow(functionContext);
    }

    private void VisitEnumDecl(EnumDeclStmt enumDecl, SemanticScope scope, string? containerQualifiedName)
    {
        CfgsSymbol enumSymbol = DeclareEnumSymbol(enumDecl, scope, containerQualifiedName, synthetic: false);
        foreach (EnumMemberNode member in enumDecl.Members)
            DeclareEnumMemberSymbol(member, enumSymbol.QualifiedName);
    }

    private CompletionResult VisitIfStmt(IfStmt ifStmt, SemanticScope scope, string? containerQualifiedName)
    {
        VisitExpr(ifStmt.Condition, scope);

        SemanticScope thenFlowScope = CreateFlowBranchScope(scope);
        CompletionResult thenCompletion = VisitBlock(ifStmt.ThenBlock, thenFlowScope, containerQualifiedName);

        SemanticScope elseFlowScope = CreateFlowBranchScope(scope);
        CompletionResult elseCompletion = CompletionResult.Fallthrough();
        if (ifStmt.ElseBranch is not null)
            elseCompletion = VisitElseBranch(ifStmt.ElseBranch, elseFlowScope, containerQualifiedName);

        List<SemanticScope> fallthroughScopes = [];
        if (thenCompletion.FallsThrough)
            fallthroughScopes.Add(thenFlowScope);
        if (elseCompletion.FallsThrough)
            fallthroughScopes.Add(elseFlowScope);

        MergeFlowBranches(scope, fallthroughScopes);

        CompletionResult completion = new();
        completion.SetFallsThrough(fallthroughScopes.Count > 0);
        completion.AddPropagated(thenCompletion);
        completion.AddPropagated(elseCompletion);
        return completion;
    }

    private CompletionResult VisitElseBranch(Stmt elseBranch, SemanticScope scope, string? containerQualifiedName)
    {
        if (elseBranch is BlockStmt elseBlock)
            return VisitBlock(elseBlock, scope, containerQualifiedName);

        SemanticScope elseScope = new(scope);
        return VisitStatement(elseBranch, elseScope, containerQualifiedName);
    }

    private CompletionResult VisitBlock(BlockStmt block, SemanticScope parentScope, string? containerQualifiedName)
    {
        SemanticScope blockScope = new(parentScope);
        return VisitBlockStatements(block.Statements, blockScope, containerQualifiedName);
    }

    private CompletionResult VisitBlockStatements(IEnumerable<Stmt> statements, SemanticScope scope, string? containerQualifiedName)
    {
        PredeclareStatements(statements, scope, containerQualifiedName);
        CompletionResult aggregate = CompletionResult.Fallthrough();
        foreach (Stmt stmt in statements)
        {
            if (!aggregate.FallsThrough)
                break;

            CompletionResult statementCompletion = VisitStatement(stmt, scope, containerQualifiedName);
            aggregate.AddPropagated(statementCompletion);
            aggregate.SetFallsThrough(statementCompletion.FallsThrough);
        }

        return aggregate;
    }

    private static SemanticScope CreateFlowBranchScope(SemanticScope parentScope)
        => new(parentScope, delayParentFlowMutations: true);

    private static SemanticScope CreateFlowSnapshotScope(SemanticScope parentScope)
    {
        SemanticScope snapshotScope = CreateFlowBranchScope(parentScope);
        foreach (string name in parentScope.GetVisibleNames())
        {
            if (parentScope.TryGetResolvedValueType(name, out string? valueTypeQualifiedName))
                snapshotScope.SetResolvedValueType(name, valueTypeQualifiedName);

            if (parentScope.TryGetResolvedCallTargetIds(name, out IReadOnlyCollection<string> callTargetIds))
                snapshotScope.SetResolvedCallTargetIds(name, callTargetIds);

            if (parentScope.TryGetResolvedAccessPaths(name, out IReadOnlyCollection<string> accessPaths))
                snapshotScope.SetResolvedAccessPaths(name, accessPaths);
        }

        return snapshotScope;
    }

    private static SemanticScope CaptureCompletionScope(SemanticScope scope)
        => CreateFlowSnapshotScope(scope);

    private static SemanticScope CreateMergedFlowScope(SemanticScope parentScope, params SemanticScope?[] branchScopes)
    {
        SemanticScope mergedScope = CreateFlowBranchScope(parentScope);
        MergeFlowBranches(mergedScope, branchScopes);
        return mergedScope;
    }

    private static void MergeFlowBranches(SemanticScope parentScope, IEnumerable<SemanticScope?> branchScopes)
    {
        List<SemanticScope> branches = branchScopes.Where(static scope => scope is not null).Cast<SemanticScope>().ToList();
        if (branches.Count == 0)
            return;

        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (SemanticScope branch in branches)
            names.UnionWith(branch.GetLocalFlowNames());

        foreach (string name in names)
        {
            if (!parentScope.TryResolve(name, out _))
                continue;

            if (branches.Any(branch => branch.HasLocalValueTypeEntry(name)))
            {
                HashSet<string?> possibleTypes = new();
                foreach (SemanticScope branch in branches)
                {
                    string? valueType = branch.HasLocalValueTypeEntry(name)
                        ? branch.GetLocalValueType(name)
                        : parentScope.GetResolvedValueTypeOrNull(name);
                    possibleTypes.Add(valueType);
                }

                parentScope.SetResolvedValueType(name, possibleTypes.Count == 1 ? possibleTypes.First() : null);
            }

            if (branches.Any(branch => branch.HasLocalCallTargetEntry(name)))
            {
                HashSet<string> mergedCallTargets = new(StringComparer.Ordinal);
                foreach (SemanticScope branch in branches)
                {
                    IEnumerable<string> callTargets = branch.HasLocalCallTargetEntry(name)
                        ? branch.GetLocalCallTargetIds(name)
                        : parentScope.GetResolvedCallTargetIds(name);
                    mergedCallTargets.UnionWith(callTargets);
                }

                parentScope.SetResolvedCallTargetIds(name, mergedCallTargets);
            }

            if (branches.Any(branch => branch.HasLocalAccessPathEntry(name)))
            {
                HashSet<string> mergedAccessPaths = new(StringComparer.Ordinal);
                foreach (SemanticScope branch in branches)
                {
                    IEnumerable<string> accessPaths = branch.HasLocalAccessPathEntry(name)
                        ? branch.GetLocalAccessPaths(name)
                        : parentScope.GetResolvedAccessPaths(name);
                    mergedAccessPaths.UnionWith(accessPaths);
                }

                parentScope.SetResolvedAccessPaths(name, mergedAccessPaths);
            }
        }
    }

    private LoopAnalysisResult ComputeLoopFixpoint(
        SemanticScope snapshotScope,
        bool includeSnapshotState,
        Func<SemanticScope, CompletionResult> executeIteration)
    {
        List<SemanticScope> breakScopes = [];
        CompletionResult propagatedCompletion = new();

        SemanticScope firstIterationScope = CreateFlowBranchScope(snapshotScope);
        CompletionResult firstCompletion = executeIteration(firstIterationScope);
        SemanticScope? reachableScope = MergeLoopReentryScopes(snapshotScope, null, firstIterationScope, firstCompletion);
        CollectLoopPropagatedCompletions(firstCompletion, breakScopes, propagatedCompletion);

        for (int iteration = 1; iteration < MaxLoopFixpointIterations && reachableScope is not null; iteration++)
        {
            SemanticScope iterationSnapshot = CreateFlowSnapshotScope(reachableScope);
            SemanticScope iterationScope = CreateFlowBranchScope(iterationSnapshot);
            CompletionResult iterationCompletion = CompletionResult.Fallthrough();
            RunFlowOnly(() => iterationCompletion = executeIteration(iterationScope));

            SemanticScope? nextReachableScope = MergeLoopReentryScopes(snapshotScope, reachableScope, iterationScope, iterationCompletion);
            CollectLoopPropagatedCompletions(iterationCompletion, breakScopes, propagatedCompletion);
            if (HaveEquivalentFlowState(reachableScope, nextReachableScope))
            {
                reachableScope = nextReachableScope;
                break;
            }

            reachableScope = nextReachableScope;
        }

        List<SemanticScope> exitScopes = [];
        if (includeSnapshotState)
            exitScopes.Add(snapshotScope);
        if (reachableScope is not null)
            exitScopes.Add(reachableScope);
        exitScopes.AddRange(breakScopes);

        propagatedCompletion.SetFallsThrough(exitScopes.Count > 0);
        return new LoopAnalysisResult
        {
            ExitScope = exitScopes.Count > 0
                ? CreateMergedFlowScope(snapshotScope, exitScopes.ToArray())
                : CreateFlowBranchScope(snapshotScope),
            Completion = propagatedCompletion
        };
    }

    private static SemanticScope? MergeLoopReentryScopes(
        SemanticScope snapshotScope,
        SemanticScope? existingReachableScope,
        SemanticScope iterationScope,
        CompletionResult completion)
    {
        List<SemanticScope> reentryScopes = [];
        if (existingReachableScope is not null)
            reentryScopes.Add(existingReachableScope);
        if (completion.FallsThrough)
            reentryScopes.Add(CaptureCompletionScope(iterationScope));
        reentryScopes.AddRange(completion.ContinueScopes);
        return reentryScopes.Count > 0
            ? CreateMergedFlowScope(snapshotScope, reentryScopes.ToArray())
            : null;
    }

    private static void CollectLoopPropagatedCompletions(
        CompletionResult source,
        List<SemanticScope> breakScopes,
        CompletionResult destination)
    {
        breakScopes.AddRange(source.BreakScopes);
        destination.ReturnFlows.AddRange(source.ReturnFlows);
        destination.ThrowScopes.AddRange(source.ThrowScopes);
    }

    private static bool HaveEquivalentFlowState(SemanticScope? left, SemanticScope? right)
    {
        if (ReferenceEquals(left, right))
            return true;

        if (left is null || right is null)
            return false;

        HashSet<string> names = new(StringComparer.Ordinal);
        names.UnionWith(left.GetVisibleNames());
        names.UnionWith(right.GetVisibleNames());

        foreach (string name in names)
        {
            if (!left.TryResolve(name, out _) && !right.TryResolve(name, out _))
                continue;

            if (!string.Equals(left.GetResolvedValueTypeOrNull(name), right.GetResolvedValueTypeOrNull(name), StringComparison.Ordinal))
                return false;

            if (!HaveEquivalentValues(left.GetResolvedCallTargetIds(name), right.GetResolvedCallTargetIds(name)))
                return false;

            if (!HaveEquivalentValues(left.GetResolvedAccessPaths(name), right.GetResolvedAccessPaths(name)))
                return false;
        }

        return true;
    }

    private static bool HaveEquivalentValues(IEnumerable<string> left, IEnumerable<string> right)
        => NormalizeEquivalentValues(left).SetEquals(NormalizeEquivalentValues(right));

    private static HashSet<string> NormalizeEquivalentValues(IEnumerable<string> values)
        => values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.Ordinal);

    private bool IsFlowOnlyPass
        => _flowOnlyDepth > 0;

    private void RunFlowOnly(Action action)
    {
        _flowOnlyDepth++;
        try
        {
            action();
        }
        finally
        {
            _flowOnlyDepth--;
        }
    }

    private CompletionResult VisitWhileStmt(WhileStmt whileStmt, SemanticScope scope, string? containerQualifiedName)
    {
        VisitExpr(whileStmt.Condition, scope);

        SemanticScope snapshotScope = CreateFlowSnapshotScope(scope);
        LoopAnalysisResult loopResult = ComputeLoopFixpoint(
            snapshotScope,
            includeSnapshotState: true,
            iterationScope => VisitBlock(whileStmt.Body, iterationScope, containerQualifiedName));

        MergeFlowBranches(scope, [loopResult.ExitScope]);
        return loopResult.Completion;
    }

    private CompletionResult VisitDoWhileStmt(DoWhileStmt doWhileStmt, SemanticScope scope, string? containerQualifiedName)
    {
        SemanticScope snapshotScope = CreateFlowSnapshotScope(scope);
        LoopAnalysisResult loopResult = ComputeLoopFixpoint(
            snapshotScope,
            includeSnapshotState: false,
            iterationScope =>
            {
                CompletionResult completion = VisitLoopBody(doWhileStmt.Body, iterationScope, containerQualifiedName);
                VisitExpr(doWhileStmt.Condition, iterationScope);
                return completion;
            });

        MergeFlowBranches(scope, [loopResult.ExitScope]);
        return loopResult.Completion;
    }

    private CompletionResult VisitForStmt(ForStmt forStmt, SemanticScope scope, string? containerQualifiedName)
    {
        SemanticScope loopScope = new(scope);

        if (forStmt.Init is not null)
            VisitStatement(forStmt.Init, loopScope, containerQualifiedName);

        if (forStmt.Condition is not null)
            VisitExpr(forStmt.Condition, loopScope);

        SemanticScope snapshotScope = CreateFlowSnapshotScope(loopScope);
        LoopAnalysisResult loopResult = ComputeLoopFixpoint(
            snapshotScope,
            includeSnapshotState: true,
            iterationScope =>
            {
                CompletionResult bodyCompletion = VisitBlock(forStmt.Body, iterationScope, containerQualifiedName);
                if (forStmt.Increment is null)
                    return bodyCompletion;

                List<SemanticScope> reentryScopes = [];
                if (bodyCompletion.FallsThrough)
                    reentryScopes.Add(iterationScope);
                reentryScopes.AddRange(bodyCompletion.ContinueScopes);

                CompletionResult incrementedCompletion = new();
                foreach (SemanticScope reentryScope in reentryScopes)
                {
                    SemanticScope incrementScope = CreateFlowSnapshotScope(reentryScope);
                    CompletionResult incrementCompletion = VisitStatement(forStmt.Increment, incrementScope, containerQualifiedName);
                    incrementedCompletion.AddPropagated(incrementCompletion);
                    if (incrementCompletion.FallsThrough)
                    {
                        incrementedCompletion.SetFallsThrough(true);
                        incrementedCompletion.ContinueScopes.Add(CaptureCompletionScope(incrementScope));
                    }
                }

                incrementedCompletion.BreakScopes.AddRange(bodyCompletion.BreakScopes);
                incrementedCompletion.ReturnFlows.AddRange(bodyCompletion.ReturnFlows);
                incrementedCompletion.ThrowScopes.AddRange(bodyCompletion.ThrowScopes);
                return incrementedCompletion;
            });

        MergeFlowBranches(scope, [loopResult.ExitScope]);
        return loopResult.Completion;
    }

    private CompletionResult VisitForeachStmt(ForeachStmt foreachStmt, SemanticScope scope, string? containerQualifiedName)
    {
        VisitExpr(foreachStmt.Iterable, scope);

        SemanticScope loopScope = new(scope);
        if (foreachStmt.TargetPattern is not null)
        {
            BindPattern(foreachStmt.TargetPattern, loopScope, foreachStmt.DeclareLocal, containerQualifiedName);
        }
        else if (!IsSyntheticGeneratedName(foreachStmt.VarName))
        {
            if (foreachStmt.DeclareLocal)
                DeclareSyntheticAwareLocal(foreachStmt, foreachStmt.VarName, loopScope, containerQualifiedName, "foreach");
            else
                RecordNamedReference(foreachStmt.VarName, foreachStmt.Line, foreachStmt.Col, foreachStmt.OriginFile, loopScope);
        }

        SemanticScope snapshotScope = CreateFlowSnapshotScope(loopScope);
        LoopAnalysisResult loopResult = ComputeLoopFixpoint(
            snapshotScope,
            includeSnapshotState: true,
            iterationScope => VisitLoopBody(foreachStmt.Body, iterationScope, containerQualifiedName));

        MergeFlowBranches(scope, [loopResult.ExitScope]);
        return loopResult.Completion;
    }

    private CompletionResult VisitLoopBody(Stmt body, SemanticScope loopScope, string? containerQualifiedName)
    {
        if (body is BlockStmt bodyBlock)
            return VisitBlock(bodyBlock, loopScope, containerQualifiedName);

        return VisitStatement(body, loopScope, containerQualifiedName);
    }

    private CompletionResult VisitMatchStmt(MatchStmt matchStmt, SemanticScope scope, string? containerQualifiedName)
    {
        VisitExpr(matchStmt.Expression, scope);

        List<SemanticScope> fallthroughScopes = [];
        CompletionResult completion = new();
        foreach (CaseClause clause in matchStmt.Cases)
        {
            SemanticScope branchScope = CreateFlowBranchScope(scope);
            SemanticScope caseScope = new(branchScope);
            BindPattern(clause.Pattern, caseScope, declareBindings: true, containerQualifiedName);
            if (clause.Guard is not null)
                VisitExpr(clause.Guard, caseScope);
            CompletionResult branchCompletion = VisitBlockStatements(clause.Body.Statements, new SemanticScope(caseScope), containerQualifiedName);
            completion.AddPropagated(branchCompletion);
            if (branchCompletion.FallsThrough)
                fallthroughScopes.Add(branchScope);
        }

        if (matchStmt.DefaultCase is not null)
        {
            SemanticScope defaultScope = CreateFlowBranchScope(scope);
            CompletionResult defaultCompletion = VisitBlock(matchStmt.DefaultCase, defaultScope, containerQualifiedName);
            completion.AddPropagated(defaultCompletion);
            if (defaultCompletion.FallsThrough)
                fallthroughScopes.Add(defaultScope);
        }

        MergeFlowBranches(scope, fallthroughScopes);
        completion.SetFallsThrough(fallthroughScopes.Count > 0);
        return completion;
    }

    private CompletionResult VisitTryStmt(TryStmt tryStmt, SemanticScope scope, string? containerQualifiedName)
    {
        SemanticScope snapshotScope = CreateFlowSnapshotScope(scope);
        SemanticScope tryFlowScope = CreateFlowBranchScope(snapshotScope);
        CompletionResult tryCompletion = VisitBlock(tryStmt.TryBlock, tryFlowScope, containerQualifiedName);

        List<SemanticScope> preFinallyFallthroughScopes = [];
        if (tryCompletion.FallsThrough)
            preFinallyFallthroughScopes.Add(CaptureCompletionScope(tryFlowScope));

        CompletionResult preFinallyCompletion = new();
        preFinallyCompletion.BreakScopes.AddRange(tryCompletion.BreakScopes);
        preFinallyCompletion.ContinueScopes.AddRange(tryCompletion.ContinueScopes);
        preFinallyCompletion.ReturnFlows.AddRange(tryCompletion.ReturnFlows);

        if (tryStmt.CatchBlock is not null && tryCompletion.ThrowScopes.Count > 0)
        {
            foreach (SemanticScope throwScope in tryCompletion.ThrowScopes)
            {
                SemanticScope catchFlowScope = CreateFlowBranchScope(throwScope);
                if (!string.IsNullOrWhiteSpace(tryStmt.CatchIdent))
                    DeclareCatchSymbol(tryStmt.CatchIdent!, tryStmt.CatchBlock.Line, tryStmt.CatchBlock.Col, tryStmt.CatchBlock.OriginFile, catchFlowScope, containerQualifiedName);

                CompletionResult catchCompletion = VisitBlock(tryStmt.CatchBlock, catchFlowScope, containerQualifiedName);
                preFinallyCompletion.AddPropagated(catchCompletion);
                if (catchCompletion.FallsThrough)
                    preFinallyFallthroughScopes.Add(CaptureCompletionScope(catchFlowScope));
            }
        }
        else
        {
            preFinallyCompletion.ThrowScopes.AddRange(tryCompletion.ThrowScopes);
        }

        if (tryStmt.FinallyBlock is null)
        {
            MergeFlowBranches(scope, preFinallyFallthroughScopes);
            preFinallyCompletion.SetFallsThrough(preFinallyFallthroughScopes.Count > 0);
            return preFinallyCompletion;
        }

        CompletionResult completion = new();
        List<SemanticScope> finalFallthroughScopes = [];

        foreach (SemanticScope fallthroughScope in preFinallyFallthroughScopes)
            ApplyFinallyBlock(tryStmt.FinallyBlock, fallthroughScope, containerQualifiedName, completion, finalScope => finalFallthroughScopes.Add(finalScope));

        foreach (SemanticScope breakScope in preFinallyCompletion.BreakScopes)
            ApplyFinallyBlock(tryStmt.FinallyBlock, breakScope, containerQualifiedName, completion, finalScope => completion.BreakScopes.Add(finalScope));

        foreach (SemanticScope continueScope in preFinallyCompletion.ContinueScopes)
            ApplyFinallyBlock(tryStmt.FinallyBlock, continueScope, containerQualifiedName, completion, finalScope => completion.ContinueScopes.Add(finalScope));

        foreach (CapturedReturnFlow returnFlow in preFinallyCompletion.ReturnFlows)
            ApplyFinallyBlock(tryStmt.FinallyBlock, returnFlow, containerQualifiedName, completion, survivingReturnFlow => completion.ReturnFlows.Add(survivingReturnFlow));

        foreach (SemanticScope throwScope in preFinallyCompletion.ThrowScopes)
            ApplyFinallyBlock(tryStmt.FinallyBlock, throwScope, containerQualifiedName, completion, finalScope => completion.ThrowScopes.Add(finalScope));

        MergeFlowBranches(scope, finalFallthroughScopes);
        completion.SetFallsThrough(finalFallthroughScopes.Count > 0);
        return completion;
    }

    private void ApplyFinallyBlock(
        BlockStmt finallyBlock,
        SemanticScope incomingScope,
        string? containerQualifiedName,
        CompletionResult destination,
        Action<SemanticScope> onFallthrough)
    {
        SemanticScope finallyFlowScope = CreateFlowBranchScope(incomingScope);
        CompletionResult finallyCompletion = VisitBlock(finallyBlock, finallyFlowScope, containerQualifiedName);
        destination.AddPropagated(finallyCompletion);
        if (finallyCompletion.FallsThrough)
            onFallthrough(CaptureCompletionScope(finallyFlowScope));
    }

    private void ApplyFinallyBlock(
        BlockStmt finallyBlock,
        CapturedReturnFlow incomingReturn,
        string? containerQualifiedName,
        CompletionResult destination,
        Action<CapturedReturnFlow> onFallthrough)
    {
        SemanticScope finallyFlowScope = CreateFlowBranchScope(incomingReturn.Scope);
        CompletionResult finallyCompletion = VisitBlock(finallyBlock, finallyFlowScope, containerQualifiedName);
        destination.AddPropagated(finallyCompletion);
        if (finallyCompletion.FallsThrough)
            onFallthrough(incomingReturn.WithScope(CaptureCompletionScope(finallyFlowScope)));
    }

    private CompletionResult VisitUsingStmt(UsingStmt usingStmt, SemanticScope scope, string? containerQualifiedName)
    {
        VisitExpr(usingStmt.Resource, scope);

        SemanticScope usingScope = new(scope);
        if (!string.IsNullOrWhiteSpace(usingStmt.BindingName))
        {
            string? valueTypeQualifiedName = TryInferValueTypeQualifiedName(usingStmt.Resource, scope);
            DeclareUsingBindingSymbol(usingStmt, usingScope, containerQualifiedName, valueTypeQualifiedName);
            usingScope.SetResolvedCallTargetIds(usingStmt.BindingName!, TryResolveCallableSymbols(usingStmt.Resource, scope).Select(static symbol => symbol.Id!).Where(static id => !string.IsNullOrWhiteSpace(id)));
            usingScope.SetResolvedAccessPaths(usingStmt.BindingName!, TryResolveContainerAccessPaths(usingStmt.Resource, scope));
        }

        return VisitBlockStatements(usingStmt.Body.Statements, usingScope, containerQualifiedName);
    }

    private void VisitExpr(Expr expr, SemanticScope scope)
    {
        switch (expr)
        {
            case NullExpr:
            case NumberExpr:
            case StringExpr:
            case CharExpr:
            case BoolExpr:
                return;

            case VarExpr varExpr:
                RecordNamedReference(varExpr.Name, varExpr.Line, varExpr.Col, varExpr.OriginFile, scope);
                return;

            case BinaryExpr binaryExpr:
                VisitExpr(binaryExpr.Left, scope);
                VisitExpr(binaryExpr.Right, scope);
                return;

            case UnaryExpr unaryExpr:
                VisitExpr(unaryExpr.Right, scope);
                return;

            case PrefixExpr prefixExpr:
                if (prefixExpr.Target is not null)
                    VisitExpr(prefixExpr.Target, scope);
                return;

            case PostfixExpr postfixExpr:
                if (postfixExpr.Target is not null)
                    VisitExpr(postfixExpr.Target, scope);
                return;

            case ArrayExpr arrayExpr:
                string arrayAccessPath = GetOrCreateAccessPath(arrayExpr, null);
                for (int i = 0; i < arrayExpr.Elements.Count; i++)
                {
                    Expr item = arrayExpr.Elements[i];
                    VisitExpr(item, scope);
                    TrackAssignedMemberBinding(arrayAccessPath, i.ToString(CultureInfo.InvariantCulture), item, scope);
                }
                return;

            case DictExpr dictExpr:
                string dictAccessPath = GetOrCreateAccessPath(dictExpr, null);
                foreach ((Expr Key, Expr Value) pair in dictExpr.Pairs)
                {
                    VisitExpr(pair.Key, scope);
                    VisitExpr(pair.Value, scope);

                    if (TryGetIndexMemberName(pair.Key, out string? memberName))
                        TrackAssignedMemberBinding(dictAccessPath, memberName!, pair.Value, scope);
                }
                return;

            case IndexExpr indexExpr:
                if (TryRecordQualifiedTypePath(indexExpr, scope))
                    return;
                if (indexExpr.Target is not null)
                    VisitExpr(indexExpr.Target, scope);
                if (indexExpr.Target is not null &&
                    TryGetIndexMemberName(indexExpr.Index, out string? completionMemberName))
                {
                    RecordCompletionTarget(indexExpr.Target, completionMemberName!, scope, indexExpr.OriginFile);
                }
                if (indexExpr.Target is not null && TryGetIndexMemberName(indexExpr.Index, out string? indexMemberName))
                    RecordQualifiedMemberReference(indexExpr.Target, indexMemberName!, indexExpr.Line, indexExpr.Col, indexExpr.OriginFile, scope);
                if (indexExpr.Index is not null)
                    VisitExpr(indexExpr.Index, scope);
                return;

            case SliceExpr sliceExpr:
                if (sliceExpr.Target is not null)
                    VisitExpr(sliceExpr.Target, scope);
                if (sliceExpr.Start is not null)
                    VisitExpr(sliceExpr.Start, scope);
                if (sliceExpr.End is not null)
                    VisitExpr(sliceExpr.End, scope);
                return;

            case TryUnwrapExpr tryUnwrapExpr:
                if (tryUnwrapExpr.Inner is not null)
                    VisitExpr(tryUnwrapExpr.Inner, scope);
                return;

            case MethodCallExpr methodCallExpr:
                VisitExpr(methodCallExpr.Target, scope);
                RecordQualifiedMemberReference(methodCallExpr.Target, methodCallExpr.Method, methodCallExpr.Line, methodCallExpr.Col, methodCallExpr.OriginFile, scope);
                foreach (Expr arg in methodCallExpr.Args)
                    VisitExpr(arg, scope);
                return;

            case NewExpr newExpr:
                RecordTypeReference(newExpr.ClassName, newExpr.Line, newExpr.Col, newExpr.OriginFile, scope);
                foreach (Expr arg in newExpr.Args)
                    VisitExpr(arg, scope);
                foreach ((string _, Expr Value) init in newExpr.Initializers)
                    VisitExpr(init.Value, scope);
                return;

            case GetFieldExpr getFieldExpr:
                VisitExpr(getFieldExpr.Target, scope);
                RecordCompletionTarget(getFieldExpr.Target, getFieldExpr.Field, scope, getFieldExpr.OriginFile);
                RecordQualifiedMemberReference(getFieldExpr.Target, getFieldExpr.Field, getFieldExpr.Line, getFieldExpr.Col, getFieldExpr.OriginFile, scope);
                return;

            case OutExpr outExpr:
                VisitBlock(outExpr.Body, scope, null);
                return;

            case ConditionalExpr conditionalExpr:
                VisitExpr(conditionalExpr.Condition, scope);
                VisitExpr(conditionalExpr.ThenExpr, scope);
                VisitExpr(conditionalExpr.ElseExpr, scope);
                return;

            case MatchExpr matchExpr:
                VisitExpr(matchExpr.Scrutinee, scope);
                foreach (CaseExprArm arm in matchExpr.Arms)
                {
                    SemanticScope armScope = new(scope);
                    BindPattern(arm.Pattern, armScope, declareBindings: true, null);
                    if (arm.Guard is not null)
                        VisitExpr(arm.Guard, armScope);
                    VisitExpr(arm.Body, armScope);
                }
                if (matchExpr.DefaultArm is not null)
                    VisitExpr(matchExpr.DefaultArm, scope);
                return;

            case AwaitExpr awaitExpr:
                VisitExpr(awaitExpr.Inner, scope);
                return;

            case FuncExpr funcExpr:
                VisitFunctionExpr(funcExpr, scope);
                return;

            case NamedArgExpr namedArgExpr:
                VisitExpr(namedArgExpr.Value, scope);
                return;

            case SpreadArgExpr spreadArgExpr:
                VisitExpr(spreadArgExpr.Value, scope);
                return;

            case CallExpr callExpr:
                if (callExpr.Target is not null)
                {
                    VisitExpr(callExpr.Target, scope);
                    RecordCallSite(callExpr.Target, scope);
                    TryRecordCallableOccurrence(callExpr.Target, scope);
                }
                foreach (Expr arg in callExpr.Args)
                    VisitExpr(arg, scope);
                return;

            case ObjectInitExpr objectInitExpr:
                VisitExpr(objectInitExpr.Target, scope);
                foreach ((string _, Expr Value) init in objectInitExpr.Inits)
                    VisitExpr(init.Value, scope);
                return;
        }
    }

    private void RecordCompletionTarget(Expr expr, string memberName, SemanticScope scope, string originFile)
    {
        if (IsFlowOnlyPass)
            return;

        string uri = _sources.ToDocumentUri(originFile, _documentUri);
        if (!string.Equals(uri, _documentUri, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            return;

        string? sourceText = _sources.GetSourceText(originFile);
        if (sourceText is null)
            return;

        sourceText = NormalizeSourceText(sourceText);
        if (!TryGetExpressionEnd(expr, out PositionInfo targetEnd) ||
            !TryFindMemberAccessAnchor(sourceText, targetEnd, memberName, out PositionInfo anchor))
        {
            return;
        }

        IReadOnlyList<CfgsCompletionItem> items = GetCompletionItemsForExpr(expr, scope);
        if (items.Count == 0)
            return;

        string key = $"{uri}|{anchor.Line}:{anchor.Character}";
        if (!_completionItemsByAnchorKey.TryGetValue(key, out Dictionary<string, CfgsCompletionItem>? itemsByLabel))
        {
            itemsByLabel = new Dictionary<string, CfgsCompletionItem>(StringComparer.Ordinal);
            _completionItemsByAnchorKey[key] = itemsByLabel;
        }

        foreach (CfgsCompletionItem item in items)
        {
            if (!itemsByLabel.ContainsKey(item.Label))
                itemsByLabel[item.Label] = item;
        }
    }

    private static bool TryFindMemberAccessAnchor(string sourceText, PositionInfo targetEnd, string memberName, out PositionInfo anchor)
    {
        anchor = default;
        int absoluteIndex = TryGetAbsoluteIndex(sourceText, targetEnd.Line, targetEnd.Character);
        if (absoluteIndex < 0)
            return false;

        SkipWhitespace(sourceText, ref absoluteIndex);
        if (absoluteIndex >= sourceText.Length || sourceText[absoluteIndex] != '.')
            return false;

        absoluteIndex++;
        SkipWhitespace(sourceText, ref absoluteIndex);
        if (absoluteIndex < 0 || absoluteIndex + memberName.Length > sourceText.Length)
            return false;

        if (!string.Equals(sourceText.Substring(absoluteIndex, memberName.Length), memberName, StringComparison.Ordinal))
            return false;

        if (!TryGetLineCharacter(sourceText, absoluteIndex, out int line, out int character))
            return false;

        anchor = new PositionInfo(line, character);
        return true;
    }

    private IReadOnlyList<CfgsCompletionItem> GetCompletionItemsForExpr(Expr expr, SemanticScope scope)
    {
        IReadOnlyList<string> containerPaths = TryResolveContainerAccessPaths(expr, scope);
        if (containerPaths.Count == 0)
            return [];

        bool preferInstanceMembers = ShouldPreferInstanceMembers(expr, scope);
        Dictionary<string, CfgsCompletionItem> items = new(StringComparer.Ordinal);
        foreach (string containerPath in containerPaths)
        {
            foreach (CfgsCompletionItem item in GetCompletionItemsForPath(containerPath, preferInstanceMembers))
            {
                if (!items.ContainsKey(item.Label))
                    items[item.Label] = item;
            }
        }

        return items.Values
            .OrderBy(static item => item.Label, StringComparer.Ordinal)
            .ToList();
    }

    private IReadOnlyList<CfgsCompletionItem> GetCompletionItemsForPath(string containerPath, bool preferInstanceMembers)
    {
        string cacheKey = $"{(preferInstanceMembers ? 'i' : 's')}|{containerPath}";
        if (_completionItemsByPathMode.TryGetValue(cacheKey, out IReadOnlyList<CfgsCompletionItem>? cached))
            return cached;

        Dictionary<string, CfgsCompletionItem> items = new(StringComparer.Ordinal);
        CollectCompletionItemsForPath(containerPath, preferInstanceMembers, items, new HashSet<string>(StringComparer.Ordinal));
        IReadOnlyList<CfgsCompletionItem> computed = items.Values
            .OrderBy(static item => item.Label, StringComparer.Ordinal)
            .ToList();
        _completionItemsByPathMode[cacheKey] = computed;
        return computed;
    }

    private bool ShouldPreferInstanceMembers(Expr expr, SemanticScope scope)
    {
        if (expr is VarExpr varExpr && scope.ResolveContextualQualifier(varExpr.Name) is not null)
            return !string.Equals(varExpr.Name, "type", StringComparison.Ordinal);

        return !TryResolveExprSymbol(expr, scope, out CfgsSymbol? symbol) ||
               symbol!.Kind is not (3 or 5 or 10 or 11);
    }

    private void CollectCompletionItemsForPath(
        string containerPath,
        bool preferInstanceMembers,
        Dictionary<string, CfgsCompletionItem> items,
        HashSet<string> seenPaths)
    {
        if (!seenPaths.Add(containerPath))
            return;

        AddDynamicCompletionItems(containerPath, items);

        if (TryResolveByExactName(containerPath, out CfgsSymbol? exact))
        {
            switch (exact!.Kind)
            {
                case 5:
                    (Dictionary<string, ClassMemberBinding> instanceMembers, Dictionary<string, ClassMemberBinding> staticMembers) = BuildAccessibleMembers(containerPath);
                    AddClassMemberCompletions(preferInstanceMembers ? instanceMembers : staticMembers, items);
                    return;

                case 3:
                case 10:
                case 11:
                    AddDirectQualifiedChildItems(containerPath, items);
                    return;
            }
        }

        if (_containerBaseTypeByPath.TryGetValue(containerPath, out string? baseType) &&
            !string.IsNullOrWhiteSpace(baseType))
        {
            CollectCompletionItemsForPath(baseType!, preferInstanceMembers: true, items, seenPaths);
            return;
        }

        AddDirectQualifiedChildItems(containerPath, items);
    }

    private void AddDynamicCompletionItems(string containerPath, Dictionary<string, CfgsCompletionItem> items)
    {
        string prefix = containerPath + ".";
        HashSet<string> memberNames = new(StringComparer.Ordinal);

        foreach (string qualifiedName in _memberSymbolAliasesByQualifiedName.Keys)
        {
            if (TryExtractDirectMemberName(qualifiedName, prefix, out string? memberName))
                memberNames.Add(memberName!);
        }

        foreach (string qualifiedName in _memberTypesByQualifiedName.Keys)
        {
            if (TryExtractDirectMemberName(qualifiedName, prefix, out string? memberName))
                memberNames.Add(memberName!);
        }

        foreach (string qualifiedName in _memberAccessPathsByQualifiedName.Keys)
        {
            if (TryExtractDirectMemberName(qualifiedName, prefix, out string? memberName))
                memberNames.Add(memberName!);
        }

        foreach (string memberName in memberNames)
        {
            if (items.ContainsKey(memberName))
                continue;

            string qualified = prefix + memberName;
            if (_memberSymbolAliasesByQualifiedName.TryGetValue(qualified, out CfgsSymbol? aliasedSymbol))
            {
                items[memberName] = CreateCompletionItem(memberName, aliasedSymbol!);
                continue;
            }

            if (_memberTypesByQualifiedName.TryGetValue(qualified, out string? valueTypeQualifiedName) &&
                !string.IsNullOrWhiteSpace(valueTypeQualifiedName) &&
                TryResolveByExactName(valueTypeQualifiedName!, out CfgsSymbol? valueTypeSymbol))
            {
                items[memberName] = CreateCompletionItem(memberName, valueTypeSymbol!);
                continue;
            }

            if (_memberAccessPathsByQualifiedName.TryGetValue(qualified, out string? accessPath) &&
                !string.IsNullOrWhiteSpace(accessPath) &&
                TryResolveByExactName(accessPath!, out CfgsSymbol? accessPathSymbol))
            {
                items[memberName] = CreateCompletionItem(memberName, accessPathSymbol!);
                continue;
            }

            items[memberName] = new CfgsCompletionItem(memberName, 6, "member");
        }
    }

    private void AddClassMemberCompletions(
        Dictionary<string, ClassMemberBinding> members,
        Dictionary<string, CfgsCompletionItem> items)
    {
        foreach ((string name, ClassMemberBinding binding) in members)
        {
            if (binding.Symbol.IsSynthetic || items.ContainsKey(name))
                continue;

            items[name] = CreateCompletionItem(name, binding.Symbol);
        }
    }

    private void AddDirectQualifiedChildItems(string containerPath, Dictionary<string, CfgsCompletionItem> items)
    {
        string prefix = containerPath + ".";
        foreach (CfgsSymbol symbol in _symbols)
        {
            if (symbol.IsSynthetic ||
                string.IsNullOrWhiteSpace(symbol.QualifiedName) ||
                !TryExtractDirectMemberName(symbol.QualifiedName, prefix, out string? memberName) ||
                items.ContainsKey(memberName!))
            {
                continue;
            }

            items[memberName!] = CreateCompletionItem(memberName!, symbol);
        }
    }

    private static bool TryExtractDirectMemberName(string qualifiedName, string prefix, out string? memberName)
    {
        memberName = null;
        if (!qualifiedName.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        string remainder = qualifiedName[prefix.Length..];
        if (string.IsNullOrWhiteSpace(remainder) ||
            remainder.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        memberName = remainder;
        return true;
    }

    private static CfgsCompletionItem CreateCompletionItem(string label, CfgsSymbol symbol)
        => new(label, ToCompletionKind(symbol.Kind), symbol.Detail);

    private void InvalidateCompletionItems(string containerPath)
    {
        _completionItemsByPathMode.Remove($"i|{containerPath}");
        _completionItemsByPathMode.Remove($"s|{containerPath}");
    }

    private static int ToCompletionKind(int symbolKind)
        => symbolKind switch
        {
            5 => 7,
            11 => 8,
            6 => 2,
            10 => 13,
            12 => 3,
            13 => 6,
            14 => 21,
            22 => 20,
            _ => 1
        };

    private bool TryGetCompletionTarget(Expr expr, out string uri, out PositionInfo end)
    {
        uri = _sources.ToDocumentUri(expr.OriginFile, _documentUri);
        return TryGetExpressionEnd(expr, out end);
    }

    private bool TryGetExpressionEnd(Expr expr, out PositionInfo end)
    {
        string? sourceText = _sources.GetSourceText(expr.OriginFile);
        if (sourceText is null)
        {
            end = default;
            return false;
        }

        sourceText = NormalizeSourceText(sourceText);

        switch (expr)
        {
            case VarExpr varExpr:
                end = new PositionInfo(Math.Max(varExpr.Line - 1, 0), Math.Max(varExpr.Col - 1, 0) + varExpr.Name.Length);
                return true;

            case ArrayExpr arrayExpr:
                return TryFindMatchingDelimiterEnd(sourceText, Math.Max(arrayExpr.Line - 1, 0), Math.Max(arrayExpr.Col - 1, 0), '[', ']', out end);

            case DictExpr dictExpr:
                return TryFindMatchingDelimiterEnd(sourceText, Math.Max(dictExpr.Line - 1, 0), Math.Max(dictExpr.Col - 1, 0), '{', '}', out end);

            case IndexExpr indexExpr when indexExpr.Target is not null:
                if (!TryGetExpressionEnd(indexExpr.Target, out PositionInfo targetEnd))
                {
                    end = default;
                    return false;
                }

                int absoluteIndex = TryGetAbsoluteIndex(sourceText, targetEnd.Line, targetEnd.Character);
                if (absoluteIndex >= 0)
                {
                    SkipWhitespace(sourceText, ref absoluteIndex);
                    if (absoluteIndex < sourceText.Length && sourceText[absoluteIndex] == '[')
                    {
                        if (!TryGetLineCharacter(sourceText, absoluteIndex, out int indexLine, out int indexCharacter))
                        {
                            end = default;
                            return false;
                        }

                        return TryFindMatchingDelimiterEnd(sourceText, indexLine, indexCharacter, '[', ']', out end);
                    }
                }

                if (TryGetIndexMemberName(indexExpr.Index, out string? memberName) &&
                    TryFindMemberAccessAnchor(sourceText, targetEnd, memberName!, out PositionInfo memberStart))
                {
                    end = new PositionInfo(memberStart.Line, memberStart.Character + memberName!.Length);
                    return true;
                }

                end = default;
                return false;

            case CallExpr callExpr when callExpr.Target is not null:
                if (TryGetExpressionEnd(callExpr.Target, out PositionInfo callTargetEnd) &&
                    TryFindTrailingBlockEnd(sourceText, callTargetEnd, '(', ')', out end))
                {
                    return true;
                }

                end = default;
                return false;

            case NewExpr newExpr:
                return TryFindNewExpressionEnd(newExpr, sourceText, out end);

            case ObjectInitExpr objectInitExpr:
                if (!TryGetExpressionEnd(objectInitExpr.Target, out PositionInfo objectInitTargetEnd))
                {
                    end = default;
                    return false;
                }

                if (TryFindTrailingBlockEnd(sourceText, objectInitTargetEnd, '{', '}', out PositionInfo objectEnd))
                {
                    end = objectEnd;
                    return true;
                }

                end = objectInitTargetEnd;
                return true;

            case GetFieldExpr getFieldExpr:
                if (TryGetExpressionEnd(getFieldExpr.Target, out PositionInfo fieldTargetEnd) &&
                    TryFindMemberAccessAnchor(sourceText, fieldTargetEnd, getFieldExpr.Field, out PositionInfo fieldStart))
                {
                    end = new PositionInfo(fieldStart.Line, fieldStart.Character + getFieldExpr.Field.Length);
                    return true;
                }

                end = default;
                return false;

            default:
                end = default;
                return false;
        }
    }

    private static bool TryFindNewExpressionEnd(NewExpr newExpr, string sourceText, out PositionInfo end)
    {
        end = default;
        int absoluteStart = TryGetAbsoluteIndex(sourceText, Math.Max(newExpr.Line - 1, 0), Math.Max(newExpr.Col - 1, 0));
        if (absoluteStart < 0 || absoluteStart >= sourceText.Length)
            return false;

        int cursor = absoluteStart;
        if (!TryConsumeExactText(sourceText, ref cursor, "new"))
            return false;

        SkipWhitespace(sourceText, ref cursor);
        while (cursor < sourceText.Length && IsQualifiedIdentifierChar(sourceText[cursor]))
            cursor++;

        SkipWhitespace(sourceText, ref cursor);
        if (cursor < sourceText.Length && sourceText[cursor] == '(')
        {
            if (!TryGetLineCharacter(sourceText, cursor, out int openParenLine, out int openParenCharacter) ||
                !TryFindMatchingDelimiterEnd(sourceText, openParenLine, openParenCharacter, '(', ')', out PositionInfo afterArgs))
            {
                return false;
            }

            cursor = TryGetAbsoluteIndex(sourceText, afterArgs.Line, afterArgs.Character);
            if (cursor < 0)
                return false;
        }

        SkipWhitespace(sourceText, ref cursor);
        if (newExpr.Initializers.Count > 0 && cursor < sourceText.Length && sourceText[cursor] == '{')
        {
            if (!TryGetLineCharacter(sourceText, cursor, out int openBraceLine, out int openBraceCharacter) ||
                !TryFindMatchingDelimiterEnd(sourceText, openBraceLine, openBraceCharacter, '{', '}', out end))
            {
                return false;
            }

            return true;
        }

        if (!TryGetLineCharacter(sourceText, cursor, out int endLine, out int endCharacter))
            return false;

        end = new PositionInfo(endLine, endCharacter);
        return true;
    }

    private static bool TryFindTrailingBlockEnd(string sourceText, PositionInfo start, char openChar, char closeChar, out PositionInfo end)
    {
        end = default;
        int absoluteIndex = TryGetAbsoluteIndex(sourceText, start.Line, start.Character);
        if (absoluteIndex < 0)
            return false;

        SkipWhitespace(sourceText, ref absoluteIndex);
        if (absoluteIndex >= sourceText.Length || sourceText[absoluteIndex] != openChar)
            return false;

        if (!TryGetLineCharacter(sourceText, absoluteIndex, out int line, out int character))
            return false;

        return TryFindMatchingDelimiterEnd(sourceText, line, character, openChar, closeChar, out end);
    }

    private static bool TryFindMatchingDelimiterEnd(
        string sourceText,
        int startLine,
        int startCharacter,
        char openChar,
        char closeChar,
        out PositionInfo end)
    {
        end = default;
        int absoluteStart = TryGetAbsoluteIndex(sourceText, startLine, startCharacter);
        if (absoluteStart < 0)
            return false;

        int cursor = absoluteStart;
        SkipWhitespace(sourceText, ref cursor);
        if (cursor >= sourceText.Length || sourceText[cursor] != openChar)
            return false;

        int depth = 0;
        bool inString = false;
        bool escape = false;
        char stringDelimiter = '\0';

        for (int i = cursor; i < sourceText.Length; i++)
        {
            char current = sourceText[i];
            if (inString)
            {
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (current == '\\')
                {
                    escape = true;
                    continue;
                }

                if (current == stringDelimiter)
                    inString = false;

                continue;
            }

            if (current is '"' or '\'')
            {
                inString = true;
                stringDelimiter = current;
                continue;
            }

            if (current == openChar)
            {
                depth++;
                continue;
            }

            if (current != closeChar)
                continue;

            depth--;
            if (depth == 0 && TryGetLineCharacter(sourceText, i + 1, out int endLine, out int endCharacter))
            {
                end = new PositionInfo(endLine, endCharacter);
                return true;
            }
        }

        return false;
    }

    private static bool TryConsumeExactText(string sourceText, ref int cursor, string value)
    {
        if (cursor < 0 || cursor + value.Length > sourceText.Length)
            return false;

        if (!string.Equals(sourceText.Substring(cursor, value.Length), value, StringComparison.Ordinal))
            return false;

        cursor += value.Length;
        return true;
    }

    private static void SkipWhitespace(string sourceText, ref int cursor)
    {
        while (cursor < sourceText.Length && char.IsWhiteSpace(sourceText[cursor]))
            cursor++;
    }

    private static bool IsQualifiedIdentifierChar(char ch)
        => char.IsLetterOrDigit(ch) || ch == '_' || ch == '.';

    private static int TryGetAbsoluteIndex(string sourceText, int line, int character)
    {
        if (line < 0 || character < 0)
            return -1;

        int currentLine = 0;
        int currentCharacter = 0;
        for (int i = 0; i < sourceText.Length; i++)
        {
            if (currentLine == line && currentCharacter == character)
                return i;

            if (sourceText[i] == '\n')
            {
                currentLine++;
                currentCharacter = 0;
            }
            else
            {
                currentCharacter++;
            }
        }

        return currentLine == line && currentCharacter == character ? sourceText.Length : -1;
    }

    private static bool TryGetLineCharacter(string sourceText, int absoluteIndex, out int line, out int character)
    {
        line = 0;
        character = 0;

        if (absoluteIndex < 0 || absoluteIndex > sourceText.Length)
            return false;

        for (int i = 0; i < absoluteIndex; i++)
        {
            if (sourceText[i] == '\n')
            {
                line++;
                character = 0;
            }
            else
            {
                character++;
            }
        }

        return true;
    }

    private static string NormalizeSourceText(string sourceText)
        => sourceText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private void VisitFunctionExpr(FuncExpr funcExpr, SemanticScope scope)
    {
        SemanticScope functionScope = new(scope, inheritReceiverContext: false);
        DeclareParameters(funcExpr.Parameters, funcExpr.RestParameter, functionScope, funcExpr.OriginFile, funcExpr.Line, funcExpr.Col);
        VisitBlockStatements(funcExpr.Body.Statements, functionScope, null);
    }

    private FunctionFlowContext? BeginFunctionFlow(CfgsSymbol? functionSymbol)
    {
        if (functionSymbol is null || string.IsNullOrWhiteSpace(functionSymbol.Id))
            return null;

        FunctionFlowContext context = new(functionSymbol.Id!);
        _functionContexts.Push(context);
        return context;
    }

    private void EndFunctionFlow(FunctionFlowContext? context)
    {
        if (context is null)
            return;

        FunctionFlowContext popped = _functionContexts.Pop();
        if (!ReferenceEquals(popped, context))
            throw new InvalidOperationException("Function flow stack out of sync.");

        _functionReturnCallTargetIdsBySymbolId[context.SymbolId] = context.ReturnCallTargetIds;
        _functionReturnAccessPathsBySymbolId[context.SymbolId] = context.ReturnAccessPaths;
    }

    private void RecordReturnCompletions(CompletionResult completion)
    {
        if (_functionContexts.Count == 0)
            return;

        FunctionFlowContext context = _functionContexts.Peek();
        foreach (CapturedReturnFlow returnFlow in completion.ReturnFlows)
        {
            context.ReturnCallTargetIds.UnionWith(returnFlow.CallTargetIds);
            context.ReturnAccessPaths.UnionWith(returnFlow.AccessPaths);
        }
    }

    private CapturedReturnFlow CaptureReturnFlow(Expr? expr, SemanticScope scope)
    {
        HashSet<string> callTargetIds = new(StringComparer.Ordinal);
        HashSet<string> accessPaths = new(StringComparer.Ordinal);
        if (expr is not null)
        {
            foreach (CfgsSymbol callable in TryResolveCallableSymbols(expr, scope))
            {
                if (!string.IsNullOrWhiteSpace(callable.Id))
                    callTargetIds.Add(callable.Id!);
            }

            foreach (string accessPath in TryResolveContainerAccessPaths(expr, scope))
                accessPaths.Add(accessPath);
        }

        return new CapturedReturnFlow(CaptureCompletionScope(scope), callTargetIds, accessPaths);
    }

    private void BindPattern(MatchPattern pattern, SemanticScope scope, bool declareBindings, string? containerQualifiedName)
    {
        switch (pattern)
        {
            case WildcardMatchPattern:
                return;

            case BindingMatchPattern binding:
                if (declareBindings)
                {
                    bool synthetic = IsSyntheticGeneratedName(binding.Name);
                    DeclareBindingSymbol(binding, scope, containerQualifiedName, synthetic);
                }
                else
                {
                    RecordNamedReference(binding.Name, binding.Line, binding.Col, binding.OriginFile, scope);
                }
                return;

            case ValueMatchPattern valuePattern:
                VisitExpr(valuePattern.Value, scope);
                return;

            case ArrayMatchPattern arrayPattern:
                foreach (MatchPattern element in arrayPattern.Elements)
                    BindPattern(element, scope, declareBindings, containerQualifiedName);
                return;

            case DictMatchPattern dictPattern:
                foreach ((string _, MatchPattern childPattern) in dictPattern.Entries)
                    BindPattern(childPattern, scope, declareBindings, containerQualifiedName);
                return;
        }
    }

    private void RecordNamedReference(string name, int line, int column, string originFile, SemanticScope scope)
    {
        if (IsSyntheticGeneratedName(name))
            return;

        if (scope.TryResolve(name, out CfgsSymbol? symbol))
            AddOccurrence(symbol!, line, column, originFile, name, isDeclaration: false);
    }

    private void RecordTypeReference(string typeName, int line, int column, string originFile, SemanticScope scope)
    {
        if (TryResolveTypeSymbol(typeName, scope, out CfgsSymbol? symbol))
            AddOccurrence(symbol!, line, column, originFile, typeName, isDeclaration: false);
    }

    private void RecordQualifiedMemberReference(Expr target, string memberName, int line, int column, string originFile, SemanticScope scope)
    {
        foreach (CfgsSymbol symbol in ResolveMemberSymbols(TryResolveContainerAccessPaths(target, scope), memberName))
            AddOccurrence(symbol, line, column, originFile, memberName, isDeclaration: false);
    }

    private bool TryResolveByExactName(string qualifiedName, out CfgsSymbol? symbol)
    {
        symbol = _symbols
            .Where(candidate => candidate.Id is not null && string.Equals(candidate.QualifiedName, qualifiedName, StringComparison.Ordinal))
            .OrderByDescending(candidate => candidate.QualifiedName.Count(static ch => ch == '.'))
            .FirstOrDefault();

        return symbol is not null;
    }

    private bool TryResolveTypeSymbol(string typeName, SemanticScope scope, out CfgsSymbol? symbol)
    {
        symbol = null;

        if (TryResolveByExactName(typeName, out CfgsSymbol? exact) && exact!.Kind is 5 or 10 or 11)
        {
            symbol = exact;
            return true;
        }

        if (scope.TryResolve(typeName, out CfgsSymbol? scoped) && scoped!.Kind is 5 or 10 or 11)
        {
            symbol = scoped;
            return true;
        }

        return false;
    }

    private bool TryResolveExprSymbol(Expr expr, SemanticScope scope, out CfgsSymbol? symbol)
    {
        symbol = TryResolveExprSymbols(expr, scope).FirstOrDefault();
        return symbol is not null;
    }

    private IReadOnlyList<CfgsSymbol> TryResolveExprSymbols(Expr expr, SemanticScope scope)
    {
        switch (expr)
        {
            case VarExpr varExpr:
                List<CfgsSymbol> varSymbols = [];
                if (scope.TryResolve(varExpr.Name, out CfgsSymbol? scopedSymbol))
                    varSymbols.Add(scopedSymbol!);

                if (scope.ResolveContextualQualifier(varExpr.Name) is string contextual &&
                    TryResolveByExactName(contextual, out CfgsSymbol? contextualSymbol))
                {
                    varSymbols.Add(contextualSymbol!);
                }

                return DistinctSymbols(varSymbols);

            case IndexExpr indexExpr
                when indexExpr.Target is not null
                && TryGetIndexMemberName(indexExpr.Index, out string? indexMemberName):
                return ResolveMemberSymbols(TryResolveContainerAccessPaths(indexExpr.Target, scope), indexMemberName!);

            case GetFieldExpr getFieldExpr:
                return ResolveMemberSymbols(TryResolveContainerAccessPaths(getFieldExpr.Target, scope), getFieldExpr.Field);

            default:
                return [];
        }
    }

    private CfgsSymbol? TryResolveCallableSymbol(Expr? expr, SemanticScope scope)
    {
        IReadOnlyList<CfgsSymbol> symbols = TryResolveCallableSymbols(expr, scope);
        return symbols.Count == 1 ? symbols[0] : null;
    }

    private IReadOnlyList<CfgsSymbol> TryResolveCallableSymbols(Expr? expr, SemanticScope scope)
    {
        if (expr is null)
            return [];

        if (expr is CallExpr callExpr)
        {
            List<CfgsSymbol> returnedCallables = [];
            foreach (CfgsSymbol callee in TryResolveCallableSymbols(callExpr.Target, scope))
            {
                if (string.IsNullOrWhiteSpace(callee.Id) ||
                    !_functionReturnCallTargetIdsBySymbolId.TryGetValue(callee.Id!, out HashSet<string>? returnIds))
                {
                    continue;
                }

                foreach (string returnId in returnIds)
                {
                    if (_symbolsById.TryGetValue(returnId, out CfgsSymbol? returnedSymbol))
                    {
                        CfgsSymbol? resolved = ResolveCallTargetSymbol(returnedSymbol);
                        if (resolved is not null)
                            returnedCallables.Add(resolved);
                    }
                }
            }

            return DistinctSymbols(returnedCallables);
        }

        if (expr is VarExpr varExpr &&
            scope.TryGetResolvedCallTargetIds(varExpr.Name, out IReadOnlyCollection<string> resolvedCallTargetIds))
        {
            List<CfgsSymbol> scopedCallTargets = [];
            foreach (string resolvedCallTargetId in resolvedCallTargetIds)
            {
                if (_symbolsById.TryGetValue(resolvedCallTargetId, out CfgsSymbol? scopedCallTarget))
                {
                    CfgsSymbol? resolved = ResolveCallTargetSymbol(scopedCallTarget);
                    if (resolved is not null)
                        scopedCallTargets.Add(resolved);
                }
            }

            return DistinctSymbols(scopedCallTargets);
        }

        List<CfgsSymbol> callables = [];
        foreach (CfgsSymbol symbol in TryResolveExprSymbols(expr, scope))
        {
            CfgsSymbol? resolved = ResolveCallTargetSymbol(symbol);
            if (resolved is not null)
                callables.Add(resolved);
        }

        return DistinctSymbols(callables);
    }

    private void TryRecordCallableOccurrence(Expr expr, SemanticScope scope)
    {
        if (IsFlowOnlyPass)
            return;

        IReadOnlyList<CfgsSymbol> callableSymbols = TryResolveCallableSymbols(expr, scope);
        if (callableSymbols.Count == 0)
            return;

        HashSet<string> directCallTargetIds = new(StringComparer.Ordinal);
        foreach (CfgsSymbol directSymbol in TryResolveExprSymbols(expr, scope))
        {
            CfgsSymbol? directCallTarget = ResolveCallTargetSymbol(directSymbol);
            if (!string.IsNullOrWhiteSpace(directCallTarget?.Id))
                directCallTargetIds.Add(directCallTarget.Id!);
        }

        if (!TryGetCallableOccurrenceAnchor(expr, out int line, out int column, out string? originFile, out string? token))
            return;

        foreach (CfgsSymbol callableSymbol in callableSymbols)
        {
            if (string.IsNullOrWhiteSpace(callableSymbol.Id) || directCallTargetIds.Contains(callableSymbol.Id!))
                continue;

            AddOccurrence(callableSymbol, line, column, originFile!, token!, isDeclaration: false);
        }
    }

    private void RecordCallSite(Expr expr, SemanticScope scope)
    {
        if (IsFlowOnlyPass)
            return;

        IReadOnlyList<string> symbolIds = TryResolveCallableSymbols(expr, scope)
            .Select(static symbol => symbol.Id)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (symbolIds.Count == 0 ||
            !TryGetCallableOccurrenceAnchor(expr, out int line, out int column, out string? originFile, out string? token))
        {
            return;
        }

        string uri = _sources.ToDocumentUri(originFile!, _documentUri);
        string? source = _sources.GetSourceText(originFile!);
        RangeInfo range = TextLocator.CreateRange(source, line, column, token);
        string key = $"{uri}|{range.Start.Line}:{range.Start.Character}:{range.End.Line}:{range.End.Character}";
        if (!_callSiteSymbolIdsByKey.TryGetValue(key, out HashSet<string>? ids))
        {
            ids = new HashSet<string>(StringComparer.Ordinal);
            _callSiteSymbolIdsByKey[key] = ids;
        }

        ids.UnionWith(symbolIds);
    }

    private static bool TryGetCallableOccurrenceAnchor(Expr expr, out int line, out int column, out string? originFile, out string? token)
    {
        switch (expr)
        {
            case VarExpr varExpr:
                line = varExpr.Line;
                column = varExpr.Col;
                originFile = varExpr.OriginFile;
                token = varExpr.Name;
                return true;

            case GetFieldExpr getFieldExpr:
                line = getFieldExpr.Line;
                column = getFieldExpr.Col;
                originFile = getFieldExpr.OriginFile;
                token = getFieldExpr.Field;
                return true;

            case IndexExpr indexExpr when TryGetIndexMemberName(indexExpr.Index, out string? indexMemberName):
                line = indexExpr.Line;
                column = indexExpr.Col;
                originFile = indexExpr.OriginFile;
                token = indexMemberName;
                return true;

            default:
                line = 0;
                column = 0;
                originFile = null;
                token = null;
                return false;
        }
    }

    private CfgsSymbol? ResolveCallTargetSymbol(CfgsSymbol? symbol)
    {
        if (symbol is null)
            return null;

        HashSet<string> seen = new(StringComparer.Ordinal);
        CfgsSymbol current = symbol;

        while (!string.IsNullOrWhiteSpace(current.CallTargetSymbolId) &&
               seen.Add(current.Id ?? current.QualifiedName) &&
               _symbolsById.TryGetValue(current.CallTargetSymbolId!, out CfgsSymbol? next))
        {
            current = next;
        }

        return current.Kind is 5 or 6 or 12 ? current : null;
    }

    private string? TryResolveContainerAccessPath(Expr? expr, SemanticScope scope)
    {
        IReadOnlyList<string> accessPaths = TryResolveContainerAccessPaths(expr, scope);
        return accessPaths.Count == 1 ? accessPaths[0] : null;
    }

    private IReadOnlyList<string> TryResolveContainerAccessPaths(Expr? expr, SemanticScope scope)
    {
        switch (expr)
        {
            case null:
                return [];

            case NewExpr newExpr:
                return [GetOrCreateAccessPath(newExpr, TryResolveNamedContainer(newExpr.ClassName, scope))];

            case ArrayExpr arrayExpr:
                return [GetOrCreateAccessPath(arrayExpr, null)];

            case DictExpr dictExpr:
                return [GetOrCreateAccessPath(dictExpr, null)];

            case ObjectInitExpr objectInitExpr:
                return DistinctValues(
                    TryResolveContainerAccessPaths(objectInitExpr.Target, scope)
                        .Concat([GetOrCreateAccessPath(objectInitExpr, TryInferValueTypeQualifiedName(objectInitExpr.Target, scope))]));

            case CallExpr callExpr:
                List<string> returnedAccessPaths = [];
                foreach (CfgsSymbol callee in TryResolveCallableSymbols(callExpr.Target, scope))
                {
                    if (string.IsNullOrWhiteSpace(callee.Id) ||
                        !_functionReturnAccessPathsBySymbolId.TryGetValue(callee.Id!, out HashSet<string>? accessPaths))
                    {
                        continue;
                    }

                    returnedAccessPaths.AddRange(accessPaths);
                }

                return DistinctValues(returnedAccessPaths);

            case VarExpr varExpr:
                List<string> varPaths = [];
                if (scope.ResolveContextualQualifier(varExpr.Name) is string contextual)
                    varPaths.Add(contextual);

                if (scope.TryGetResolvedAccessPaths(varExpr.Name, out IReadOnlyCollection<string> resolvedAccessPaths))
                    varPaths.AddRange(resolvedAccessPaths);
                else if (scope.TryGetResolvedValueType(varExpr.Name, out string? resolvedType) &&
                         !string.IsNullOrWhiteSpace(resolvedType))
                    varPaths.Add(resolvedType!);
                else if (TryResolveNamedContainer(varExpr.Name, scope) is string namedContainer)
                    varPaths.Add(namedContainer);

                return DistinctValues(varPaths);

            case IndexExpr indexExpr
                when indexExpr.Target is not null
                && TryGetIndexMemberName(indexExpr.Index, out string? indexMemberName):
                List<string> indexPaths = [];
                foreach (string targetPath in TryResolveContainerAccessPaths(indexExpr.Target, scope))
                {
                    string? accessPath = TryResolveMemberAccessPath(targetPath, indexMemberName!);
                    if (!string.IsNullOrWhiteSpace(accessPath))
                        indexPaths.Add(accessPath!);

                    string? valueType = TryResolveMemberValueType(targetPath, indexMemberName!);
                    if (!string.IsNullOrWhiteSpace(valueType))
                        indexPaths.Add(valueType!);
                }

                return DistinctValues(indexPaths);

            case GetFieldExpr getFieldExpr:
                List<string> fieldPaths = [];
                foreach (string targetPath in TryResolveContainerAccessPaths(getFieldExpr.Target, scope))
                {
                    string? accessPath = TryResolveMemberAccessPath(targetPath, getFieldExpr.Field);
                    if (!string.IsNullOrWhiteSpace(accessPath))
                        fieldPaths.Add(accessPath!);

                    string? valueType = TryResolveMemberValueType(targetPath, getFieldExpr.Field);
                    if (!string.IsNullOrWhiteSpace(valueType))
                        fieldPaths.Add(valueType!);
                }

                return DistinctValues(fieldPaths);

            default:
                return [];
        }
    }

    private string GetOrCreateAccessPath(object nodeKey, string? baseTypeQualifiedName)
    {
        if (_accessPathsByNode.TryGetValue(nodeKey, out string? existing))
        {
            RememberContainerBaseType(existing, baseTypeQualifiedName);
            return existing;
        }

        string accessPath = $"obj_{++_nextAccessPathId}";
        _accessPathsByNode[nodeKey] = accessPath;
        RememberContainerBaseType(accessPath, baseTypeQualifiedName);
        return accessPath;
    }

    private void RememberContainerBaseType(string accessPath, string? baseTypeQualifiedName)
    {
        if (!_containerBaseTypeByPath.ContainsKey(accessPath))
        {
            _containerBaseTypeByPath[accessPath] = string.IsNullOrWhiteSpace(baseTypeQualifiedName)
                ? null
                : baseTypeQualifiedName;
            InvalidateCompletionItems(accessPath);
            return;
        }

        if (!string.IsNullOrWhiteSpace(baseTypeQualifiedName))
        {
            _containerBaseTypeByPath[accessPath] = baseTypeQualifiedName;
            InvalidateCompletionItems(accessPath);
        }
    }

    private string? TryResolveMemberAccessPath(string containerPath, string memberName)
        => TryResolveMemberAccessPath(containerPath, memberName, new HashSet<string>(StringComparer.Ordinal));

    private string? TryResolveMemberAccessPath(string containerPath, string memberName, HashSet<string> seen)
    {
        if (!seen.Add(containerPath))
            return null;

        string qualified = $"{containerPath}.{memberName}";
        if (_memberAccessPathsByQualifiedName.TryGetValue(qualified, out string? accessPath))
            return accessPath;

        if (_containerBaseTypeByPath.TryGetValue(containerPath, out string? baseType) &&
            !string.IsNullOrWhiteSpace(baseType))
        {
            return TryResolveMemberAccessPath(baseType!, memberName, seen);
        }

        return null;
    }

    private bool TryResolveMemberSymbol(string containerPath, string memberName, out CfgsSymbol? symbol)
        => TryResolveMemberSymbol(containerPath, memberName, new HashSet<string>(StringComparer.Ordinal), out symbol);

    private bool TryResolveMemberSymbol(string containerPath, string memberName, HashSet<string> seen, out CfgsSymbol? symbol)
    {
        if (!seen.Add(containerPath))
        {
            symbol = null;
            return false;
        }

        string qualified = $"{containerPath}.{memberName}";
        if (_memberSymbolAliasesByQualifiedName.TryGetValue(qualified, out symbol) ||
            TryResolveByExactName(qualified, out symbol))
        {
            return true;
        }

        if (_containerBaseTypeByPath.TryGetValue(containerPath, out string? baseType) &&
            !string.IsNullOrWhiteSpace(baseType))
        {
            return TryResolveMemberSymbol(baseType!, memberName, seen, out symbol);
        }

        symbol = null;
        return false;
    }

    private IReadOnlyList<CfgsSymbol> ResolveMemberSymbols(IEnumerable<string> containerPaths, string memberName)
    {
        List<CfgsSymbol> symbols = [];
        foreach (string containerPath in containerPaths)
        {
            if (TryResolveMemberSymbol(containerPath, memberName, out CfgsSymbol? symbol))
                symbols.Add(symbol!);
        }

        return DistinctSymbols(symbols);
    }

    private static IReadOnlyList<CfgsSymbol> DistinctSymbols(IEnumerable<CfgsSymbol> symbols)
        => symbols
            .Where(static symbol => symbol is not null)
            .GroupBy(static symbol => symbol.Id ?? symbol.QualifiedName, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToList();

    private static IReadOnlyList<string> DistinctValues(IEnumerable<string> values)
        => values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private bool TryRecordQualifiedTypePath(Expr expr, SemanticScope scope)
    {
        string? typePath = TextLocator.TryExtractQualifiedPath(expr);
        if (string.IsNullOrWhiteSpace(typePath))
            return false;

        if (!TryResolveTypeSymbol(typePath, scope, out CfgsSymbol? symbol))
            return false;

        AddOccurrence(symbol!, expr.Line, expr.Col, expr.OriginFile, typePath, isDeclaration: false);
        return true;
    }

    private bool TryExtractQualifiedAccess(Expr expr, SemanticScope scope, out string? path)
    {
        IReadOnlyList<string> paths = TryResolveContainerAccessPaths(expr, scope);
        path = paths.Count == 1 ? paths[0] : null;
        return path is not null;
    }

    private void TrackMemberValueType(string containerQualifiedName, string memberName, Expr? value, SemanticScope scope)
    {
        string? valueTypeQualifiedName = TryInferValueTypeQualifiedName(value, scope);
        if (string.IsNullOrWhiteSpace(valueTypeQualifiedName))
            return;

        _memberTypesByQualifiedName[$"{containerQualifiedName}.{memberName}"] = valueTypeQualifiedName!;
    }

    private void TrackAssignedMemberBinding(Expr target, Expr value, SemanticScope scope)
    {
        switch (target)
        {
            case IndexExpr indexExpr
                when indexExpr.Target is not null
                && TryGetIndexMemberName(indexExpr.Index, out string? indexMemberName)
                && TryExtractQualifiedAccess(indexExpr.Target, scope, out string? targetPath):
                TrackAssignedMemberBinding(targetPath!, indexMemberName!, value, scope);
                return;

            case GetFieldExpr getFieldExpr
                when TryExtractQualifiedAccess(getFieldExpr.Target, scope, out string? fieldTargetPath):
                TrackAssignedMemberBinding(fieldTargetPath!, getFieldExpr.Field, value, scope);
                return;
        }
    }

    private void TrackAssignedMemberBinding(string containerPath, string memberName, Expr value, SemanticScope scope)
    {
        string qualified = $"{containerPath}.{memberName}";
        InvalidateCompletionItems(containerPath);

        string? valueTypeQualifiedName = TryInferValueTypeQualifiedName(value, scope);
        if (!string.IsNullOrWhiteSpace(valueTypeQualifiedName))
            _memberTypesByQualifiedName[qualified] = valueTypeQualifiedName!;

        string? accessPath = TryResolveContainerAccessPath(value, scope);
        if (!string.IsNullOrWhiteSpace(accessPath))
            _memberAccessPathsByQualifiedName[qualified] = accessPath!;
        else
            _memberAccessPathsByQualifiedName.Remove(qualified);

        CfgsSymbol? callableSymbol = TryResolveCallableSymbol(value, scope);
        if (callableSymbol is not null)
        {
            _memberSymbolAliasesByQualifiedName[qualified] = callableSymbol;
            return;
        }

        if (TryResolveExprSymbol(value, scope, out CfgsSymbol? valueSymbol))
        {
            _memberSymbolAliasesByQualifiedName[qualified] = valueSymbol!;
            return;
        }

        _memberSymbolAliasesByQualifiedName.Remove(qualified);
    }

    private static bool TryGetIndexMemberName(Expr? indexExpr, out string? memberName)
    {
        switch (indexExpr)
        {
            case StringExpr stringIndex:
                memberName = stringIndex.Value;
                return true;

            case NumberExpr numberIndex:
                memberName = TryFormatLiteralIndex(numberIndex.Value);
                return !string.IsNullOrWhiteSpace(memberName);

            default:
                memberName = null;
                return false;
        }
    }

    private static string? TryFormatLiteralIndex(dynamic? value)
    {
        if (value is null)
            return null;

        if (value is string stringValue)
            return stringValue;

        if (value is IFormattable formattable)
            return formattable.ToString(null, CultureInfo.InvariantCulture);

        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private string? TryInferValueTypeQualifiedName(Expr? expr, SemanticScope scope)
    {
        switch (expr)
        {
            case null:
                return null;

            case NewExpr newExpr:
                return TryResolveNamedContainer(newExpr.ClassName, scope);

            case ObjectInitExpr objectInitExpr:
                return TryInferValueTypeQualifiedName(objectInitExpr.Target, scope);

            case VarExpr varExpr:
                if (scope.ResolveContextualQualifier(varExpr.Name) is string contextual)
                    return contextual;

                if (scope.TryGetResolvedValueType(varExpr.Name, out string? resolvedType))
                    return resolvedType;

                return TryResolveNamedContainer(varExpr.Name, scope);

            case IndexExpr indexExpr
                when indexExpr.Target is not null
                && indexExpr.Index is StringExpr stringIndex
                && TryExtractQualifiedAccess(indexExpr.Target, scope, out string? targetPath):
                return TryResolveMemberValueType(targetPath!, stringIndex.Value);

            case GetFieldExpr getFieldExpr
                when TryExtractQualifiedAccess(getFieldExpr.Target, scope, out string? fieldTargetPath):
                return TryResolveMemberValueType(fieldTargetPath!, getFieldExpr.Field);

            default:
                return null;
        }
    }

    private string? TryResolveMemberValueType(string containerPath, string memberName)
        => TryResolveMemberValueType(containerPath, memberName, new HashSet<string>(StringComparer.Ordinal));

    private string? TryResolveMemberValueType(string containerPath, string memberName, HashSet<string> seen)
    {
        if (!seen.Add(containerPath))
            return null;

        string qualified = $"{containerPath}.{memberName}";
        if (_memberAccessPathsByQualifiedName.TryGetValue(qualified, out string? accessPath) &&
            !string.IsNullOrWhiteSpace(accessPath) &&
            _containerBaseTypeByPath.TryGetValue(accessPath!, out string? accessBaseType) &&
            !string.IsNullOrWhiteSpace(accessBaseType))
        {
            return accessBaseType;
        }

        if (_memberTypesByQualifiedName.TryGetValue(qualified, out string? memberType))
            return memberType;

        if (_memberSymbolAliasesByQualifiedName.TryGetValue(qualified, out CfgsSymbol? aliasedSymbol))
        {
            if (!string.IsNullOrWhiteSpace(aliasedSymbol.ValueTypeQualifiedName))
                return aliasedSymbol.ValueTypeQualifiedName;

            if (aliasedSymbol.Kind is 5 or 10 or 11)
                return aliasedSymbol.QualifiedName;
        }

        if (TryResolveByExactName(qualified, out CfgsSymbol? symbol))
        {
            if (!string.IsNullOrWhiteSpace(symbol!.ValueTypeQualifiedName))
                return symbol.ValueTypeQualifiedName;

            if (symbol.Kind is 5 or 10 or 11)
                return symbol.QualifiedName;
        }

        if (_containerBaseTypeByPath.TryGetValue(containerPath, out string? baseType) &&
            !string.IsNullOrWhiteSpace(baseType))
        {
            return TryResolveMemberValueType(baseType!, memberName, seen);
        }

        return null;
    }

    private void RegisterClassMember(string classQualifiedName, string memberName, CfgsSymbol symbol, bool isStatic, string? valueTypeQualifiedName)
    {
        Dictionary<string, Dictionary<string, ClassMemberBinding>> owner = isStatic ? _staticMembersByType : _instanceMembersByType;
        if (!owner.TryGetValue(classQualifiedName, out Dictionary<string, ClassMemberBinding>? members))
        {
            members = new Dictionary<string, ClassMemberBinding>(StringComparer.Ordinal);
            owner[classQualifiedName] = members;
        }

        members[memberName] = new ClassMemberBinding(symbol, valueTypeQualifiedName);
    }

    private SemanticScope CreateImplicitMemberScope(SemanticScope parentScope, string classQualifiedName, bool isStaticMethod)
    {
        SemanticScope memberScope = new(parentScope);
        (Dictionary<string, ClassMemberBinding> instanceMembers, Dictionary<string, ClassMemberBinding> staticMembers) = BuildAccessibleMembers(classQualifiedName);

        if (isStaticMethod)
        {
            foreach ((string name, ClassMemberBinding binding) in staticMembers)
                memberScope.Declare(name, binding.Symbol, binding.ValueTypeQualifiedName);

            return memberScope;
        }

        HashSet<string> memberNames = new(instanceMembers.Keys, StringComparer.Ordinal);
        memberNames.UnionWith(staticMembers.Keys);

        foreach (string name in memberNames)
        {
            ClassMemberBinding? instanceBinding = instanceMembers.GetValueOrDefault(name);
            ClassMemberBinding? staticBinding = staticMembers.GetValueOrDefault(name);
            bool hasInstance = instanceBinding is not null;
            bool hasStatic = staticBinding is not null;
            if (hasInstance && hasStatic)
                continue;

            ClassMemberBinding binding = hasInstance ? instanceBinding! : staticBinding!;
            memberScope.Declare(name, binding.Symbol, binding.ValueTypeQualifiedName);
        }

        return memberScope;
    }

    private (Dictionary<string, ClassMemberBinding> InstanceMembers, Dictionary<string, ClassMemberBinding> StaticMembers) BuildAccessibleMembers(string classQualifiedName)
    {
        Dictionary<string, ClassMemberBinding> instanceMembers = new(StringComparer.Ordinal);
        Dictionary<string, ClassMemberBinding> staticMembers = new(StringComparer.Ordinal);

        if (_baseTypeByType.TryGetValue(classQualifiedName, out string? baseQualifiedName) &&
            !string.IsNullOrWhiteSpace(baseQualifiedName))
        {
            (Dictionary<string, ClassMemberBinding> baseInstanceMembers, Dictionary<string, ClassMemberBinding> baseStaticMembers) = BuildAccessibleMembers(baseQualifiedName!);

            foreach ((string name, ClassMemberBinding binding) in baseInstanceMembers)
                instanceMembers[name] = binding;

            foreach ((string name, ClassMemberBinding binding) in baseStaticMembers)
                staticMembers[name] = binding;
        }

        if (_instanceMembersByType.TryGetValue(classQualifiedName, out Dictionary<string, ClassMemberBinding>? declaredInstanceMembers))
        {
            foreach ((string name, ClassMemberBinding binding) in declaredInstanceMembers)
                instanceMembers[name] = binding;
        }

        if (_staticMembersByType.TryGetValue(classQualifiedName, out Dictionary<string, ClassMemberBinding>? declaredStaticMembers))
        {
            foreach ((string name, ClassMemberBinding binding) in declaredStaticMembers)
                staticMembers[name] = binding;
        }

        return (instanceMembers, staticMembers);
    }

    private string? TryResolveNamedContainer(string name, SemanticScope scope)
    {
        if (TryResolveTypeSymbol(name, scope, out CfgsSymbol? typeSymbol))
            return typeSymbol!.QualifiedName;

        if (TryResolveByExactName(name, out CfgsSymbol? exact) &&
            string.Equals(exact!.QualifiedName, name, StringComparison.Ordinal))
            return exact!.QualifiedName;

        if (scope.TryResolve(name, out CfgsSymbol? symbol) &&
            string.Equals(symbol!.QualifiedName, name, StringComparison.Ordinal))
            return symbol.QualifiedName;

        return null;
    }

    private void DeclareParameters(IEnumerable<string> parameters, string? restParameter, SemanticScope scope, string originFile, int line, int column)
    {
        HashSet<string> parameterNames = new(parameters, StringComparer.Ordinal);
        foreach (string parameter in parameterNames)
        {
            bool isRest = string.Equals(restParameter, parameter, StringComparison.Ordinal);
            bool synthetic = IsSyntheticGeneratedName(parameter);
            DeclareLocalName(parameter, line, column, originFile, scope, synthetic, isRest ? "rest parameter" : "parameter");
        }
    }

    private void DeclareCatchSymbol(string name, int line, int column, string originFile, SemanticScope scope, string? containerQualifiedName)
    {
        bool synthetic = IsSyntheticGeneratedName(name);
        string qualifiedName = CreateLocalQualifiedName(containerQualifiedName, name);
        CfgsSymbol symbol = CreateSymbol(
            new object(),
            name,
            qualifiedName,
            13,
            originFile,
            line,
            column,
            "catch variable",
            $"catch {name}",
            [],
            synthetic,
            addOccurrence: !synthetic,
            signature: null);

        scope.Declare(name, symbol, valueTypeQualifiedName: null);
    }

    private void DeclareUsingBindingSymbol(UsingStmt usingStmt, SemanticScope scope, string? containerQualifiedName, string? valueTypeQualifiedName)
    {
        if (string.IsNullOrWhiteSpace(usingStmt.BindingName))
            return;

        bool synthetic = IsSyntheticGeneratedName(usingStmt.BindingName!);
        string qualifiedName = CreateLocalQualifiedName(containerQualifiedName, usingStmt.BindingName!);
        int kind = usingStmt.BindingIsConst ? 14 : 13;
        string detail = usingStmt.BindingIsConst ? "using const" : "using var";
        CfgsSymbol symbol = CreateSymbol(
            new object(),
            usingStmt.BindingName!,
            qualifiedName,
            kind,
            usingStmt.OriginFile,
            usingStmt.Line,
            usingStmt.Col,
            detail,
            $"{detail} {usingStmt.BindingName}",
            [],
            synthetic,
            addOccurrence: !synthetic,
            signature: null,
            valueTypeQualifiedName);

        scope.Declare(usingStmt.BindingName!, symbol, valueTypeQualifiedName);
    }

    private void DeclareSyntheticAwareLocal(Node node, string name, SemanticScope scope, string? containerQualifiedName, string detailPrefix)
    {
        bool synthetic = IsSyntheticGeneratedName(name);
        string qualifiedName = CreateLocalQualifiedName(containerQualifiedName, name);
        CfgsSymbol symbol = CreateSymbol(
            node,
            name,
            qualifiedName,
            13,
            node.OriginFile,
            node.Line,
            node.Col,
            $"{detailPrefix} variable",
            $"{detailPrefix} {name}",
            [],
            synthetic,
            addOccurrence: !synthetic,
            signature: null);

        scope.Declare(name, symbol, valueTypeQualifiedName: null);
    }

    private void DeclareLocalName(string name, int line, int column, string originFile, SemanticScope scope, bool synthetic, string detailPrefix)
    {
        object key = new object();
        string qualifiedName = CreateLocalQualifiedName(null, name);
        CfgsSymbol symbol = CreateSymbol(
            key,
            name,
            qualifiedName,
            13,
            originFile,
            line,
            column,
            detailPrefix,
            $"{detailPrefix} {name}",
            [],
            synthetic,
            addOccurrence: !synthetic,
            signature: null);

        scope.Declare(name, symbol, valueTypeQualifiedName: null);
    }

    private CfgsSymbol DeclareBindingSymbol(BindingMatchPattern binding, SemanticScope scope, string? containerQualifiedName, bool synthetic)
    {
        string qualifiedName = CreateLocalQualifiedName(containerQualifiedName, binding.Name);
        CfgsSymbol symbol = CreateSymbol(
            binding,
            binding.Name,
            qualifiedName,
            13,
            binding.OriginFile,
            binding.Line,
            binding.Col,
            "binding",
            $"binding {binding.Name}",
            [],
            synthetic,
            addOccurrence: !synthetic,
            signature: null);

        scope.Declare(binding.Name, symbol, valueTypeQualifiedName: null);
        return symbol;
    }

    private CfgsSymbol DeclareFunctionSymbol(FuncDeclStmt func, SemanticScope scope, string? containerQualifiedName, bool isMethod, bool synthetic)
    {
        string label = SignatureDisplay.BuildFunctionLabel(func.Name, func.IsAsync, func.Parameters, func.ParameterSpecs, func.RestParameter);
        IReadOnlyList<string> parameterDisplay = SignatureDisplay.GetParameterDisplayList(func.Parameters, func.ParameterSpecs, func.RestParameter);
        CfgsSignature signature = new(
            label,
            parameterDisplay,
            func.RestParameter,
            func.MinArgs);

        CfgsSymbol symbol = CreateSymbol(
            func,
            func.Name,
            Qualify(containerQualifiedName, func.Name),
            isMethod ? 6 : 12,
            func.OriginFile,
            func.Line,
            func.Col,
            label,
            label,
            [],
            synthetic,
            addOccurrence: !synthetic,
            signature);

        scope.Declare(func.Name, symbol, valueTypeQualifiedName: null);
        return symbol;
    }

    private CfgsSymbol DeclareInterfaceMethodSymbol(InterfaceMethodDecl method, string? containerQualifiedName, bool synthetic)
    {
        string label = BuildInterfaceMethodLabel(method);
        CfgsSignature signature = new(
            label,
            method.Parameters.ToList(),
            method.RestParameter,
            method.MinArgs);

        return CreateSymbol(
            method,
            method.Name,
            Qualify(containerQualifiedName, method.Name),
            6,
            method.OriginFile,
            method.Line,
            method.Col,
            label,
            label,
            [],
            synthetic,
            addOccurrence: !synthetic,
            signature);
    }

    private CfgsSymbol DeclareClassSymbol(ClassDeclStmt classDecl, SemanticScope scope, string? containerQualifiedName, bool synthetic)
    {
        string label = BuildClassLabel(classDecl);

        CfgsSignature signature = new(
            $"{classDecl.Name}({string.Join(", ", classDecl.Parameters)})",
            classDecl.Parameters.ToList(),
            null,
            classDecl.Parameters.Count);

        CfgsSymbol symbol = CreateSymbol(
            classDecl,
            classDecl.Name,
            Qualify(containerQualifiedName, classDecl.Name),
            5,
            classDecl.OriginFile,
            classDecl.Line,
            classDecl.Col,
            label,
            label,
            [],
            synthetic,
            addOccurrence: !synthetic,
            signature);

        scope.Declare(classDecl.Name, symbol, symbol.QualifiedName);
        return symbol;
    }

    private CfgsSymbol DeclareInterfaceSymbol(InterfaceDeclStmt interfaceDecl, SemanticScope scope, string? containerQualifiedName, bool synthetic)
    {
        string label = BuildInterfaceLabel(interfaceDecl);
        CfgsSymbol symbol = CreateSymbol(
            interfaceDecl,
            interfaceDecl.Name,
            Qualify(containerQualifiedName, interfaceDecl.Name),
            11,
            interfaceDecl.OriginFile,
            interfaceDecl.Line,
            interfaceDecl.Col,
            label,
            label,
            [],
            synthetic,
            addOccurrence: !synthetic,
            signature: null);

        scope.Declare(interfaceDecl.Name, symbol, symbol.QualifiedName);
        return symbol;
    }

    private CfgsSymbol DeclareEnumSymbol(EnumDeclStmt enumDecl, SemanticScope scope, string? containerQualifiedName, bool synthetic)
    {
        string label = $"enum {enumDecl.Name}";
        CfgsSymbol symbol = CreateSymbol(
            enumDecl,
            enumDecl.Name,
            Qualify(containerQualifiedName, enumDecl.Name),
            10,
            enumDecl.OriginFile,
            enumDecl.Line,
            enumDecl.Col,
            label,
            label,
            [],
            synthetic,
            addOccurrence: !synthetic,
            signature: null);

        scope.Declare(enumDecl.Name, symbol, symbol.QualifiedName);
        return symbol;
    }

    private void DeclareEnumMemberSymbol(EnumMemberNode member, string containerQualifiedName)
    {
        CreateSymbol(
            member,
            member.Name,
            Qualify(containerQualifiedName, member.Name),
            22,
            member.OriginFile,
            member.Line,
            member.Col,
            $"enum member {member.Name}",
            $"enum member {member.Name}",
            [],
            synthetic: false,
            addOccurrence: true,
            signature: null);
    }

    private CfgsSymbol DeclareClassFieldSymbol(ClassDeclStmt classDecl, string fieldName, string containerQualifiedName, bool isStatic, bool isConst, CfgsSignature? signature, string? valueTypeQualifiedName, string? callTargetSymbolId)
    {
        string detail = isStatic
            ? (isConst ? $"static const {fieldName}" : $"static var {fieldName}")
            : (isConst ? $"const {fieldName}" : $"var {fieldName}");

        return CreateSymbol(
            (containerQualifiedName, fieldName, isStatic, isConst),
            fieldName,
            Qualify(containerQualifiedName, fieldName),
            isConst ? 14 : 13,
            classDecl.OriginFile,
            classDecl.Line,
            classDecl.Col,
            detail,
            detail,
            [],
            synthetic: false,
            addOccurrence: true,
            signature,
            valueTypeQualifiedName,
            callTargetSymbolId);
    }

    private void DeclareValueSymbol(Node node, SemanticScope scope, string? containerQualifiedName, int kind, string prefix, bool synthetic, CfgsSignature? signature, string? valueTypeQualifiedName, string? callTargetSymbolId)
    {
        string name = node switch
        {
            VarDecl varDecl => varDecl.Name,
            ConstDecl constDecl => constDecl.Name,
            _ => throw new ArgumentOutOfRangeException(nameof(node))
        };

        (int line, int column) = AdjustDeclarationAnchor(node.OriginFile, node.Line, node.Col, prefix, name);
        string qualifiedName = Qualify(containerQualifiedName, name);
        CfgsSymbol symbol = CreateSymbol(
            node,
            name,
            qualifiedName,
            kind,
            node.OriginFile,
            line,
            column,
            $"{prefix} {name}",
            $"{prefix} {name}",
            [],
            synthetic,
            addOccurrence: !synthetic,
            signature,
            valueTypeQualifiedName,
            callTargetSymbolId);

        scope.Declare(name, symbol, valueTypeQualifiedName);
    }

    private (int Line, int Column) AdjustDeclarationAnchor(string originFile, int fallbackLine, int fallbackColumn, string keyword, string name)
    {
        string? source = _sources.GetSourceText(originFile);
        if (source is null)
            return (fallbackLine, fallbackColumn);

        string[] lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        int expectedIndex = Math.Max(fallbackLine - 1, 0);
        int bestIndex = -1;
        int bestDistance = int.MaxValue;

        for (int i = Math.Max(0, expectedIndex - 2); i <= Math.Min(lines.Length - 1, expectedIndex + 2); i++)
        {
            string lineText = lines[i];
            if (!lineText.Contains(name, StringComparison.Ordinal) || !lineText.Contains(keyword, StringComparison.Ordinal))
                continue;

            int keywordIndex = lineText.IndexOf(keyword, StringComparison.Ordinal);
            int nameIndex = lineText.IndexOf(name, keywordIndex + keyword.Length, StringComparison.Ordinal);
            if (keywordIndex < 0 || nameIndex < 0)
                continue;

            int distance = Math.Abs(i - expectedIndex);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return bestIndex >= 0 ? (bestIndex + 1, 1) : (fallbackLine, fallbackColumn);
    }

    private CfgsSignature? TryGetFunctionSignature(Expr? expr, SemanticScope scope)
    {
        if (expr is not FuncExpr funcExpr)
        {
            CfgsSymbol? callableSymbol = TryResolveCallableSymbol(expr, scope);
            return callableSymbol?.Signature;
        }

        string label = SignatureDisplay.BuildFunctionLabel(string.Empty, funcExpr.IsAsync, funcExpr.Parameters, funcExpr.ParameterSpecs, funcExpr.RestParameter, includeName: false);
        IReadOnlyList<string> parameterDisplay = SignatureDisplay.GetParameterDisplayList(funcExpr.Parameters, funcExpr.ParameterSpecs, funcExpr.RestParameter);
        return new CfgsSignature(label, parameterDisplay, funcExpr.RestParameter, funcExpr.MinArgs);
    }

    private CfgsSymbol CreateSymbol(
        object nodeKey,
        string name,
        string qualifiedName,
        int kind,
        string originFile,
        int line,
        int column,
        string detail,
        string hoverHeader,
        IReadOnlyList<CfgsSymbol> children,
        bool synthetic,
        bool addOccurrence,
        CfgsSignature? signature,
        string? valueTypeQualifiedName = null,
        string? callTargetSymbolId = null)
    {
        if (_symbolsByNode.TryGetValue(nodeKey, out CfgsSymbol? existing))
            return existing;

        string? symbolId = IsFlowOnlyPass ? null : $"sym_{_nextSymbolId++}";
        string? source = _sources.GetSourceText(originFile);
        RangeInfo selectionRange = TextLocator.CreateRange(source, line, column, name);
        RangeInfo fullRange = CreateSymbolRange(nodeKey, source, selectionRange);
        string uri = _sources.ToDocumentUri(originFile, _documentUri);

        CfgsSymbol symbol = new(
            name,
            qualifiedName,
            kind,
            uri,
            originFile,
            fullRange,
            selectionRange,
            detail,
            CreateHoverText(hoverHeader, uri),
            children,
            synthetic,
            symbolId,
            signature,
            valueTypeQualifiedName,
            callTargetSymbolId);

        if (IsFlowOnlyPass)
            return symbol;

        _symbolsByNode[nodeKey] = symbol;
        _symbols.Add(symbol);
        _symbolsById[symbolId!] = symbol;

        if (addOccurrence)
            _occurrences.Add(new CfgsResolvedOccurrence(uri, selectionRange, symbolId!, IsDeclaration: true));

        return symbol;
    }

    private static RangeInfo CreateSymbolRange(object nodeKey, string? source, RangeInfo selectionRange)
    {
        return nodeKey switch
        {
            FuncDeclStmt func => CreateBlockRange(source, selectionRange, func.Body),
            FuncExpr funcExpr => CreateBlockRange(source, selectionRange, funcExpr.Body),
            _ => selectionRange
        };
    }

    private static RangeInfo CreateBlockRange(string? source, RangeInfo selectionRange, BlockStmt body)
    {
        PositionInfo start = new(selectionRange.Start.Line, 0);
        if (string.IsNullOrWhiteSpace(source))
            return new RangeInfo(start, selectionRange.End);

        string[] lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        (int Line, int Character)? blockEnd = TryFindBlockEnd(lines, Math.Max(body.Line - 1, 0), Math.Max(body.Col - 1, 0));
        if (blockEnd is null)
            return new RangeInfo(start, selectionRange.End);

        return new RangeInfo(start, new PositionInfo(blockEnd.Value.Line, blockEnd.Value.Character));
    }

    private static (int Line, int Character)? TryFindBlockEnd(string[] lines, int startLine, int startCharacter)
    {
        bool foundOpeningBrace = false;
        int depth = 0;

        for (int lineIndex = Math.Max(startLine, 0); lineIndex < lines.Length; lineIndex++)
        {
            string lineText = lines[lineIndex];
            int charIndex = lineIndex == startLine
                ? Math.Min(Math.Max(startCharacter, 0), lineText.Length)
                : 0;

            for (; charIndex < lineText.Length; charIndex++)
            {
                char current = lineText[charIndex];
                if (current == '{')
                {
                    foundOpeningBrace = true;
                    depth++;
                    continue;
                }

                if (current != '}' || !foundOpeningBrace)
                    continue;

                depth--;
                if (depth == 0)
                    return (lineIndex, charIndex + 1);
            }
        }

        return null;
    }

    private void AddOccurrence(CfgsSymbol symbol, int line, int column, string originFile, string token, bool isDeclaration)
    {
        if (IsFlowOnlyPass || symbol.Id is null)
            return;

        string uri = _sources.ToDocumentUri(originFile, _documentUri);
        string? source = _sources.GetSourceText(originFile);
        RangeInfo range = TextLocator.CreateRange(source, line, column, token);
        _occurrences.Add(new CfgsResolvedOccurrence(uri, range, symbol.Id, isDeclaration));
    }

    private void FinalizeCallSites()
    {
        foreach ((string key, HashSet<string> symbolIds) in _callSiteSymbolIdsByKey)
        {
            string[] parts = key.Split('|', 2, StringSplitOptions.None);
            string uri = parts[0];
            string[] rangeParts = parts[1].Split(':');
            RangeInfo range = new(
                new PositionInfo(int.Parse(rangeParts[0]), int.Parse(rangeParts[1])),
                new PositionInfo(int.Parse(rangeParts[2]), int.Parse(rangeParts[3])));
            _callSites.Add(new CfgsResolvedCallSite(uri, range, symbolIds.OrderBy(static id => id, StringComparer.Ordinal).ToList()));
        }
    }

    private void FinalizeCompletionTargets()
    {
        foreach ((string key, Dictionary<string, CfgsCompletionItem> itemsByLabel) in _completionItemsByAnchorKey)
        {
            string[] parts = key.Split('|', 2, StringSplitOptions.None);
            string uri = parts[0];
            string[] anchorParts = parts[1].Split(':');
            PositionInfo end = new(int.Parse(anchorParts[0]), int.Parse(anchorParts[1]));
            IReadOnlyList<CfgsCompletionItem> items = itemsByLabel.Values
                .OrderBy(static item => item.Label, StringComparer.Ordinal)
                .ToList();
            _completionTargets.Add(new CfgsResolvedCompletionTarget(uri, end, items));
        }
    }

    private static string CreateHoverText(string header, string uri)
    {
        StringBuilder sb = new();
        sb.AppendLine("```cfgs");
        sb.AppendLine(header);
        sb.AppendLine("```");
        sb.AppendLine();
        sb.Append("Defined in `");
        sb.Append(uri);
        sb.Append('`');
        return sb.ToString();
    }

    private static string Qualify(string? containerQualifiedName, string name)
        => string.IsNullOrWhiteSpace(containerQualifiedName) ? name : $"{containerQualifiedName}.{name}";

    private static string BuildClassLabel(ClassDeclStmt classDecl)
    {
        StringBuilder sb = new();
        sb.Append($"class {classDecl.Name}({string.Join(", ", classDecl.Parameters)})");

        List<string> inheritedTypes = [];
        if (!string.IsNullOrWhiteSpace(classDecl.BaseName))
            inheritedTypes.Add(classDecl.BaseName);
        inheritedTypes.AddRange(classDecl.ImplementedInterfaces.Where(static iface => !string.IsNullOrWhiteSpace(iface)));

        if (inheritedTypes.Count > 0)
            sb.Append($" : {string.Join(", ", inheritedTypes)}");

        return sb.ToString();
    }

    private static string BuildInterfaceLabel(InterfaceDeclStmt interfaceDecl)
    {
        StringBuilder sb = new();
        sb.Append($"interface {interfaceDecl.Name}");
        if (interfaceDecl.BaseInterfaces.Count > 0)
            sb.Append($" : {string.Join(", ", interfaceDecl.BaseInterfaces)}");

        return sb.ToString();
    }

    private static string BuildInterfaceMethodLabel(InterfaceMethodDecl method)
    {
        string prefix = method.IsAsync ? "async func" : "func";
        return $"{prefix} {method.Name}({string.Join(", ", method.Parameters)})";
    }

    private string? ResolveBaseQualifiedName(ClassDeclStmt classDecl, string? containerQualifiedName)
    {
        if (string.IsNullOrWhiteSpace(classDecl.BaseName))
            return null;

        if (classDecl.BaseName.Contains('.', StringComparison.Ordinal))
            return classDecl.BaseName;

        string candidate = Qualify(containerQualifiedName, classDecl.BaseName);
        return TryResolveByExactName(candidate, out _) ? candidate : classDecl.BaseName;
    }

    private static string CreateLocalQualifiedName(string? containerQualifiedName, string name)
        => string.IsNullOrWhiteSpace(containerQualifiedName)
            ? $"{name}#{Guid.NewGuid():N}"
            : $"{containerQualifiedName}.{name}#{Guid.NewGuid():N}";

    private static bool IsSyntheticGeneratedName(string name)
        => name.StartsWith("__", StringComparison.Ordinal);

    private static bool TryExtractNamespaceInfo(BlockStmt block, out string? namespacePath, out string? tempName)
    {
        namespacePath = null;
        tempName = null;

        if (block.Statements.FirstOrDefault() is not VarDecl tempDecl)
            return false;

        tempName = tempDecl.Name;
        namespacePath = TextLocator.TryExtractQualifiedPath(tempDecl.Value);
        return !string.IsNullOrWhiteSpace(namespacePath);
    }

    private static IEnumerable<Stmt> EnumerateNamespaceBodyStatements(BlockStmt block, string tempName)
    {
        foreach (Stmt stmt in block.Statements.Skip(1))
        {
            if (stmt is AssignExprStmt assign &&
                assign.Target is IndexExpr target &&
                target.Target is VarExpr varExpr &&
                string.Equals(varExpr.Name, tempName, StringComparison.Ordinal) &&
                assign.Value is VarExpr)
            {
                continue;
            }

            yield return stmt;
        }
    }

    private sealed record ClassMemberBinding(CfgsSymbol Symbol, string? ValueTypeQualifiedName);

    private sealed class FunctionFlowContext
    {
        public FunctionFlowContext(string symbolId)
        {
            SymbolId = symbolId;
        }

        public string SymbolId { get; }

        public HashSet<string> ReturnCallTargetIds { get; } = new(StringComparer.Ordinal);

        public HashSet<string> ReturnAccessPaths { get; } = new(StringComparer.Ordinal);
    }

    private sealed class CapturedReturnFlow
    {
        public CapturedReturnFlow(SemanticScope scope, IEnumerable<string> callTargetIds, IEnumerable<string> accessPaths)
        {
            Scope = scope;
            CallTargetIds = callTargetIds.ToHashSet(StringComparer.Ordinal);
            AccessPaths = accessPaths.ToHashSet(StringComparer.Ordinal);
        }

        public SemanticScope Scope { get; }

        public HashSet<string> CallTargetIds { get; }

        public HashSet<string> AccessPaths { get; }

        public CapturedReturnFlow WithScope(SemanticScope scope)
            => new(scope, CallTargetIds, AccessPaths);
    }

    private sealed class CompletionResult
    {
        public bool FallsThrough { get; private set; }

        public List<SemanticScope> BreakScopes { get; } = [];

        public List<SemanticScope> ContinueScopes { get; } = [];

        public List<CapturedReturnFlow> ReturnFlows { get; } = [];

        public List<SemanticScope> ThrowScopes { get; } = [];

        public static CompletionResult Fallthrough()
            => new() { FallsThrough = true };

        public static CompletionResult WithBreak(SemanticScope scope)
            => new() { FallsThrough = false, BreakScopes = { scope } };

        public static CompletionResult WithContinue(SemanticScope scope)
            => new() { FallsThrough = false, ContinueScopes = { scope } };

        public static CompletionResult WithReturn(CapturedReturnFlow returnFlow)
            => new() { FallsThrough = false, ReturnFlows = { returnFlow } };

        public static CompletionResult WithThrow(SemanticScope scope)
            => new() { FallsThrough = false, ThrowScopes = { scope } };

        public void AddPropagated(CompletionResult other)
        {
            BreakScopes.AddRange(other.BreakScopes);
            ContinueScopes.AddRange(other.ContinueScopes);
            ReturnFlows.AddRange(other.ReturnFlows);
            ThrowScopes.AddRange(other.ThrowScopes);
        }

        public void SetFallsThrough(bool fallsThrough)
            => FallsThrough = fallsThrough;
    }

    private sealed class LoopAnalysisResult
    {
        public SemanticScope ExitScope { get; init; } = null!;

        public CompletionResult Completion { get; init; } = null!;
    }

    private sealed class SemanticScope
    {
        private readonly Dictionary<string, CfgsSymbol> _symbols = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string?> _resolvedValueTypes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string?> _resolvedCallTargetIds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string?> _resolvedAccessPaths = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _possibleCallTargetIds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _possibleAccessPaths = new(StringComparer.Ordinal);

        public SemanticScope(
            SemanticScope? parent,
            string? currentClassQualifiedName = null,
            string? currentBaseQualifiedName = null,
            string? currentOuterClassQualifiedName = null,
            bool? allowThis = null,
            bool? allowType = null,
            bool? allowSuper = null,
            bool? allowOuter = null,
            bool inheritReceiverContext = true,
            bool delayParentFlowMutations = false)
        {
            Parent = parent;
            DelayParentFlowMutations = delayParentFlowMutations;

            if (inheritReceiverContext && parent is not null)
            {
                CurrentClassQualifiedName = currentClassQualifiedName ?? parent.CurrentClassQualifiedName;
                CurrentBaseQualifiedName = currentBaseQualifiedName ?? parent.CurrentBaseQualifiedName;
                CurrentOuterClassQualifiedName = currentOuterClassQualifiedName ?? parent.CurrentOuterClassQualifiedName;
                AllowThis = allowThis ?? parent.AllowThis;
                AllowType = allowType ?? parent.AllowType;
                AllowSuper = allowSuper ?? parent.AllowSuper;
                AllowOuter = allowOuter ?? parent.AllowOuter;
                return;
            }

            CurrentClassQualifiedName = currentClassQualifiedName;
            CurrentBaseQualifiedName = currentBaseQualifiedName;
            CurrentOuterClassQualifiedName = currentOuterClassQualifiedName;
            AllowThis = allowThis ?? false;
            AllowType = allowType ?? false;
            AllowSuper = allowSuper ?? false;
            AllowOuter = allowOuter ?? false;
        }

        public SemanticScope? Parent { get; }

        public bool DelayParentFlowMutations { get; }

        public string? CurrentClassQualifiedName { get; }

        public string? CurrentBaseQualifiedName { get; }

        public string? CurrentOuterClassQualifiedName { get; }

        public bool AllowThis { get; }

        public bool AllowType { get; }

        public bool AllowSuper { get; }

        public bool AllowOuter { get; }

        public void Declare(string name, CfgsSymbol symbol, string? valueTypeQualifiedName)
        {
            _symbols[name] = symbol;
            SetResolvedValueType(name, valueTypeQualifiedName);

            if (symbol.Kind is 13 or 14 || !string.IsNullOrWhiteSpace(symbol.CallTargetSymbolId))
                SetResolvedCallTargetId(name, symbol.CallTargetSymbolId);

            if (symbol.Kind is 13 or 14)
                SetResolvedAccessPath(name, null);
        }

        public bool TryResolve(string name, out CfgsSymbol? symbol)
        {
            if (_symbols.TryGetValue(name, out symbol))
                return true;

            return Parent is not null && Parent.TryResolve(name, out symbol);
        }

        public bool TryGetResolvedValueType(string name, out string? valueTypeQualifiedName)
        {
            if (_resolvedValueTypes.TryGetValue(name, out valueTypeQualifiedName))
                return true;

            return Parent is not null && Parent.TryGetResolvedValueType(name, out valueTypeQualifiedName);
        }

        public bool TryGetResolvedCallTargetId(string name, out string? callTargetSymbolId)
        {
            if (_resolvedCallTargetIds.TryGetValue(name, out callTargetSymbolId))
                return true;

            return Parent is not null && Parent.TryGetResolvedCallTargetId(name, out callTargetSymbolId);
        }

        public bool TryGetResolvedAccessPath(string name, out string? accessPath)
        {
            if (_resolvedAccessPaths.TryGetValue(name, out accessPath))
                return true;

            return Parent is not null && Parent.TryGetResolvedAccessPath(name, out accessPath);
        }

        public bool TryGetResolvedCallTargetIds(string name, out IReadOnlyCollection<string> callTargetIds)
        {
            if (_possibleCallTargetIds.TryGetValue(name, out HashSet<string>? resolved))
            {
                callTargetIds = resolved;
                return true;
            }

            if (Parent is not null)
                return Parent.TryGetResolvedCallTargetIds(name, out callTargetIds);

            callTargetIds = Array.Empty<string>();
            return false;
        }

        public bool TryGetResolvedAccessPaths(string name, out IReadOnlyCollection<string> accessPaths)
        {
            if (_possibleAccessPaths.TryGetValue(name, out HashSet<string>? resolved))
            {
                accessPaths = resolved;
                return true;
            }

            if (Parent is not null)
                return Parent.TryGetResolvedAccessPaths(name, out accessPaths);

            accessPaths = Array.Empty<string>();
            return false;
        }

        public void SetResolvedValueType(string name, string? valueTypeQualifiedName)
        {
            if (ShouldStoreFlowLocally(name))
            {
                _resolvedValueTypes[name] = string.IsNullOrWhiteSpace(valueTypeQualifiedName) ? null : valueTypeQualifiedName!;
                return;
            }

            Parent!.SetResolvedValueType(name, valueTypeQualifiedName);
        }

        public void SetResolvedCallTargetId(string name, string? callTargetSymbolId)
            => SetResolvedCallTargetIds(
                name,
                string.IsNullOrWhiteSpace(callTargetSymbolId)
                    ? Array.Empty<string>()
                    : new[] { callTargetSymbolId! });

        public void SetResolvedCallTargetIds(string name, IEnumerable<string> callTargetSymbolIds)
        {
            HashSet<string> normalized = NormalizeValues(callTargetSymbolIds);
            if (ShouldStoreFlowLocally(name))
            {
                _possibleCallTargetIds[name] = normalized;
                _resolvedCallTargetIds[name] = normalized.Count == 1 ? normalized.First() : null;
                return;
            }

            Parent!.SetResolvedCallTargetIds(name, normalized);
        }

        public void SetResolvedAccessPath(string name, string? accessPath)
            => SetResolvedAccessPaths(
                name,
                string.IsNullOrWhiteSpace(accessPath)
                    ? Array.Empty<string>()
                    : new[] { accessPath! });

        public void SetResolvedAccessPaths(string name, IEnumerable<string> accessPaths)
        {
            HashSet<string> normalized = NormalizeValues(accessPaths);
            if (ShouldStoreFlowLocally(name))
            {
                _possibleAccessPaths[name] = normalized;
                _resolvedAccessPaths[name] = normalized.Count == 1 ? normalized.First() : null;
                return;
            }

            Parent!.SetResolvedAccessPaths(name, normalized);
        }

        public IReadOnlyCollection<string> GetResolvedCallTargetIds(string name)
            => TryGetResolvedCallTargetIds(name, out IReadOnlyCollection<string> ids) ? ids : Array.Empty<string>();

        public IReadOnlyCollection<string> GetResolvedAccessPaths(string name)
            => TryGetResolvedAccessPaths(name, out IReadOnlyCollection<string> paths) ? paths : Array.Empty<string>();

        public string? GetResolvedValueTypeOrNull(string name)
            => TryGetResolvedValueType(name, out string? valueType) ? valueType : null;

        public bool HasLocalValueTypeEntry(string name)
            => _resolvedValueTypes.ContainsKey(name);

        public string? GetLocalValueType(string name)
            => _resolvedValueTypes.TryGetValue(name, out string? valueType) ? valueType : null;

        public bool HasLocalCallTargetEntry(string name)
            => _possibleCallTargetIds.ContainsKey(name);

        public IReadOnlyCollection<string> GetLocalCallTargetIds(string name)
            => _possibleCallTargetIds.TryGetValue(name, out HashSet<string>? values) ? values : Array.Empty<string>();

        public bool HasLocalAccessPathEntry(string name)
            => _possibleAccessPaths.ContainsKey(name);

        public IReadOnlyCollection<string> GetLocalAccessPaths(string name)
            => _possibleAccessPaths.TryGetValue(name, out HashSet<string>? values) ? values : Array.Empty<string>();

        public IReadOnlyCollection<string> GetLocalFlowNames()
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            names.UnionWith(_resolvedValueTypes.Keys);
            names.UnionWith(_possibleCallTargetIds.Keys);
            names.UnionWith(_possibleAccessPaths.Keys);
            return names;
        }

        public IReadOnlyCollection<string> GetVisibleNames()
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            names.UnionWith(_symbols.Keys);
            names.UnionWith(_resolvedValueTypes.Keys);
            names.UnionWith(_possibleCallTargetIds.Keys);
            names.UnionWith(_possibleAccessPaths.Keys);

            if (Parent is not null)
                names.UnionWith(Parent.GetVisibleNames());

            return names;
        }

        private bool ShouldStoreFlowLocally(string name)
            => _symbols.ContainsKey(name) ||
               Parent is null ||
               !Parent.TryResolve(name, out _) ||
               DelayParentFlowMutations;

        private static HashSet<string> NormalizeValues(IEnumerable<string> values)
            => values
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.Ordinal);

        public string? ResolveContextualQualifier(string name)
            => name switch
            {
                "this" when AllowThis => CurrentClassQualifiedName,
                "type" when AllowType => CurrentClassQualifiedName,
                "super" when AllowSuper => CurrentBaseQualifiedName,
                "outer" when AllowOuter => CurrentOuterClassQualifiedName,
                _ => null
            };
    }

    private sealed class SemanticSourceResolver
    {
        private readonly string _documentUri;
        private readonly string _documentOrigin;
        private readonly Dictionary<string, string> _sourceCache = OperatingSystem.IsWindows()
            ? new(StringComparer.OrdinalIgnoreCase)
            : new(StringComparer.Ordinal);

        public SemanticSourceResolver(string documentUri, string documentOrigin, string documentText, IReadOnlyDictionary<string, string>? openDocumentSources)
        {
            _documentUri = documentUri;
            _documentOrigin = NormalizeOrigin(documentOrigin);
            _sourceCache[_documentOrigin] = documentText;
            _sourceCache[_documentUri] = documentText;
            RegisterOverlaySources(openDocumentSources);
        }

        public string? GetSourceText(string? origin)
        {
            if (string.IsNullOrWhiteSpace(origin))
                return null;

            string normalized = NormalizeOrigin(origin);
            if (_sourceCache.TryGetValue(normalized, out string? cached))
                return cached;

            if (File.Exists(normalized))
            {
                string source = File.ReadAllText(normalized);
                _sourceCache[normalized] = source;
                return source;
            }

            return null;
        }

        public string ToDocumentUri(string? origin, string fallbackDocumentUri)
        {
            if (string.IsNullOrWhiteSpace(origin))
                return fallbackDocumentUri;

            string normalized = NormalizeOrigin(origin);
            if (string.Equals(normalized, _documentOrigin, GetPathComparison()))
                return _documentUri;

            if (Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri))
                return uri.IsFile ? new Uri(Path.GetFullPath(uri.LocalPath)).AbsoluteUri : uri.AbsoluteUri;

            if (Path.IsPathFullyQualified(origin) || File.Exists(origin))
                return new Uri(Path.GetFullPath(origin)).AbsoluteUri;

            return fallbackDocumentUri;
        }

        private static string NormalizeOrigin(string origin)
        {
            if (Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri) && uri.IsFile)
                return Path.GetFullPath(uri.LocalPath);

            if (Path.IsPathFullyQualified(origin))
                return Path.GetFullPath(origin);

            return origin;
        }

        private void RegisterOverlaySources(IReadOnlyDictionary<string, string>? openDocumentSources)
        {
            if (openDocumentSources is null)
                return;

            foreach ((string key, string value) in openDocumentSources)
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                _sourceCache[NormalizeOrigin(key)] = value;
            }
        }

        private static StringComparison GetPathComparison()
            => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();

        public new bool Equals(object? x, object? y)
            => ReferenceEquals(x, y);

        public int GetHashCode(object obj)
            => RuntimeHelpers.GetHashCode(obj);
    }
}

internal static class SignatureLocator
{
    public static SignatureRequest? TryFindSignatureRequest(string sourceText, int line, int character)
    {
        int absoluteIndex = TryGetAbsoluteIndex(sourceText, line, character);
        if (absoluteIndex < 0)
            return null;

        int openParenIndex = FindCallOpenParen(sourceText, absoluteIndex);
        if (openParenIndex < 0)
            return null;

        int argumentIndex = CountArguments(sourceText, openParenIndex + 1, absoluteIndex);
        if (!TryExtractTarget(sourceText, openParenIndex, out int targetIndex))
            return null;

        if (!TryGetLineCharacter(sourceText, targetIndex, out int targetLine, out int targetCharacter))
            return null;

        return new SignatureRequest(targetLine, targetCharacter, argumentIndex);
    }

    private static int FindCallOpenParen(string sourceText, int cursorIndex)
    {
        int parenDepth = 0;
        int bracketDepth = 0;
        int braceDepth = 0;

        for (int i = cursorIndex - 1; i >= 0; i--)
        {
            char ch = sourceText[i];
            switch (ch)
            {
                case ')':
                    parenDepth++;
                    break;
                case ']':
                    bracketDepth++;
                    break;
                case '}':
                    braceDepth++;
                    break;
                case '(':
                    if (parenDepth == 0 && bracketDepth == 0 && braceDepth == 0)
                        return i;
                    parenDepth--;
                    break;
                case '[':
                    if (bracketDepth > 0)
                        bracketDepth--;
                    break;
                case '{':
                    if (braceDepth > 0)
                        braceDepth--;
                    break;
            }
        }

        return -1;
    }

    private static int CountArguments(string sourceText, int startIndex, int cursorIndex)
    {
        int commas = 0;
        int parenDepth = 0;
        int bracketDepth = 0;
        int braceDepth = 0;

        for (int i = startIndex; i < cursorIndex && i < sourceText.Length; i++)
        {
            char ch = sourceText[i];
            switch (ch)
            {
                case '(':
                    parenDepth++;
                    break;
                case ')':
                    if (parenDepth > 0)
                        parenDepth--;
                    break;
                case '[':
                    bracketDepth++;
                    break;
                case ']':
                    if (bracketDepth > 0)
                        bracketDepth--;
                    break;
                case '{':
                    braceDepth++;
                    break;
                case '}':
                    if (braceDepth > 0)
                        braceDepth--;
                    break;
                case ',':
                    if (parenDepth == 0 && bracketDepth == 0 && braceDepth == 0)
                        commas++;
                    break;
            }
        }

        return commas;
    }

    private static bool TryExtractTarget(string sourceText, int openParenIndex, out int targetIndex)
    {
        targetIndex = -1;
        int end = openParenIndex - 1;
        while (end >= 0 && char.IsWhiteSpace(sourceText[end]))
            end--;

        if (end < 0)
            return false;

        targetIndex = end;
        while (targetIndex >= 0 && !(char.IsLetterOrDigit(sourceText[targetIndex]) || sourceText[targetIndex] == '_'))
            targetIndex--;

        return targetIndex >= 0;
    }

    private static int TryGetAbsoluteIndex(string sourceText, int line, int character)
    {
        if (line < 0 || character < 0)
            return -1;

        int currentLine = 0;
        int currentCharacter = 0;
        for (int i = 0; i < sourceText.Length; i++)
        {
            if (currentLine == line && currentCharacter == character)
                return i;

            if (sourceText[i] == '\n')
            {
                currentLine++;
                currentCharacter = 0;
            }
            else
            {
                currentCharacter++;
            }
        }

        return currentLine == line && currentCharacter == character ? sourceText.Length : -1;
    }

    private static bool TryGetLineCharacter(string sourceText, int absoluteIndex, out int line, out int character)
    {
        line = 0;
        character = 0;

        if (absoluteIndex < 0 || absoluteIndex > sourceText.Length)
            return false;

        for (int i = 0; i < absoluteIndex; i++)
        {
            if (sourceText[i] == '\n')
            {
                line++;
                character = 0;
            }
            else
            {
                character++;
            }
        }

        return true;
    }

    public static int TryFindCallArgPositions(string sourceText, int line, int endCharacter)
    {
        int absoluteIndex = TryGetAbsoluteIndex(sourceText, line, endCharacter);
        if (absoluteIndex < 0 || absoluteIndex >= sourceText.Length)
            return -1;

        for (int searchIndex = absoluteIndex; searchIndex < sourceText.Length; searchIndex++)
        {
            char current = sourceText[searchIndex];
            if (current == '(')
                return searchIndex;

            if (char.IsWhiteSpace(current) ||
                current is '"' or '\'' or '.' or '[' or ']')
            {
                continue;
            }

            if (current is ';' or '{' or '}')
                return -1;
        }

        return -1;
    }

    public static IReadOnlyList<(PositionInfo Position, int ArgIndex)> GetArgumentPositions(string sourceText, int line, int openParenAbsoluteIndex)
    {
        List<(PositionInfo, int)> positions = [];
        int argIndex = 0;
        int parenDepth = 0;
        int bracketDepth = 0;
        int braceDepth = 0;
        int start = openParenAbsoluteIndex + 1;

        while (start < sourceText.Length && char.IsWhiteSpace(sourceText[start]))
            start++;

        if (start < sourceText.Length && sourceText[start] == ')')
            return positions;

        if (TryGetLineCharacter(sourceText, start, out int firstLine, out int firstChar))
            positions.Add((new PositionInfo(firstLine, firstChar), argIndex));

        for (int i = start; i < sourceText.Length; i++)
        {
            char ch = sourceText[i];
            switch (ch)
            {
                case '(':
                    parenDepth++;
                    break;
                case ')':
                    if (parenDepth == 0)
                        return positions;
                    parenDepth--;
                    break;
                case '[':
                    bracketDepth++;
                    break;
                case ']':
                    if (bracketDepth > 0)
                        bracketDepth--;
                    break;
                case '{':
                    braceDepth++;
                    break;
                case '}':
                    if (braceDepth > 0)
                        braceDepth--;
                    break;
                case ',':
                    if (parenDepth == 0 && bracketDepth == 0 && braceDepth == 0)
                    {
                        argIndex++;
                        int argStart = i + 1;
                        while (argStart < sourceText.Length && char.IsWhiteSpace(sourceText[argStart]))
                            argStart++;
                        if (TryGetLineCharacter(sourceText, argStart, out int argLine, out int argChar))
                            positions.Add((new PositionInfo(argLine, argChar), argIndex));
                    }
                    break;
            }
        }

        return positions;
    }
}
