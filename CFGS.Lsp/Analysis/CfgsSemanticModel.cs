using System.Runtime.CompilerServices;
using System.Text;
using CFGS_VM.Analytic.Tree;

namespace CFGS.Lsp;

internal sealed record CfgsSemanticModel(
    IReadOnlyList<CfgsSymbol> Symbols,
    IReadOnlyDictionary<string, CfgsSymbol> SymbolsById,
    IReadOnlyList<CfgsResolvedOccurrence> Occurrences);

internal sealed record CfgsResolvedOccurrence(
    string Uri,
    RangeInfo Range,
    string SymbolId,
    bool IsDeclaration);

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
    private readonly SemanticSourceResolver _sources;
    private readonly string _documentUri;
    private readonly Dictionary<object, CfgsSymbol> _symbolsByNode = new(ReferenceEqualityComparer.Instance);
    private readonly List<CfgsSymbol> _symbols = [];
    private readonly Dictionary<string, CfgsSymbol> _symbolsById = new(StringComparer.Ordinal);
    private readonly List<CfgsResolvedOccurrence> _occurrences = [];
    private readonly Dictionary<string, string> _memberTypesByQualifiedName = new(StringComparer.Ordinal);
    private int _nextSymbolId;

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

        return new CfgsSemanticModel(_symbols, _symbolsById, _occurrences);
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

    private void VisitStatement(Stmt stmt, SemanticScope scope, string? containerQualifiedName)
    {
        if (stmt is ExportStmt export)
        {
            VisitStatement(export.Inner, scope, containerQualifiedName);
            return;
        }

        if (stmt is BlockStmt block && block.IsNamespaceScope)
        {
            VisitNamespaceScope(block, scope);
            return;
        }

        switch (stmt)
        {
            case EmptyStmt:
            case BreakStmt:
            case ContinueStmt:
            case YieldStmt:
                return;

            case ExprStmt exprStmt:
                VisitExpr(exprStmt.Expression, scope);
                return;

            case VarDecl varDecl:
                VisitVarDecl(varDecl, scope, containerQualifiedName);
                return;

            case ConstDecl constDecl:
                VisitConstDecl(constDecl, scope, containerQualifiedName);
                return;

            case DestructureDeclStmt destructureDecl:
                VisitExpr(destructureDecl.Value, scope);
                BindPattern(destructureDecl.Pattern, scope, declareBindings: true, containerQualifiedName);
                return;

            case DestructureAssignStmt destructureAssign:
                VisitExpr(destructureAssign.Value, scope);
                BindPattern(destructureAssign.Pattern, scope, declareBindings: false, containerQualifiedName);
                return;

            case AssignStmt assignStmt:
                RecordNamedReference(assignStmt.Name, assignStmt.Line, assignStmt.Col, assignStmt.OriginFile, scope);
                VisitExpr(assignStmt.Value, scope);
                scope.SetResolvedValueType(assignStmt.Name, TryInferValueTypeQualifiedName(assignStmt.Value, scope));
                return;

            case AssignIndexExprStmt assignIndexExpr:
                VisitExpr(assignIndexExpr.Target, scope);
                VisitExpr(assignIndexExpr.Value, scope);
                return;

            case AssignExprStmt assignExpr:
                VisitExpr(assignExpr.Target, scope);
                VisitExpr(assignExpr.Value, scope);
                if (assignExpr.Target is VarExpr targetVar)
                    scope.SetResolvedValueType(targetVar.Name, TryInferValueTypeQualifiedName(assignExpr.Value, scope));
                return;

            case CompoundAssignStmt compoundAssign:
                VisitExpr(compoundAssign.Target, scope);
                VisitExpr(compoundAssign.Value, scope);
                return;

            case SliceSetStmt sliceSet:
                VisitExpr(sliceSet.Slice, scope);
                VisitExpr(sliceSet.Value, scope);
                return;

            case PushStmt pushStmt:
                VisitExpr(pushStmt.Target, scope);
                VisitExpr(pushStmt.Value, scope);
                return;

            case DeleteVarStmt deleteVar:
                RecordNamedReference(deleteVar.Name, deleteVar.Line, deleteVar.Col, deleteVar.OriginFile, scope);
                return;

            case DeleteIndexStmt deleteIndex:
                RecordNamedReference(deleteIndex.Name, deleteIndex.Line, deleteIndex.Col, deleteIndex.OriginFile, scope);
                VisitExpr(deleteIndex.Index, scope);
                return;

            case DeleteAllStmt deleteAll:
                VisitExpr(deleteAll.Target, scope);
                return;

            case DeleteExprStmt deleteExpr:
                VisitExpr(deleteExpr.Target, scope);
                return;

            case FuncDeclStmt func:
                VisitFunctionDecl(func, scope);
                return;

            case ClassDeclStmt @class:
                VisitClassDecl(@class, scope, containerQualifiedName);
                return;

            case InterfaceDeclStmt @interface:
                VisitInterfaceDecl(@interface, scope, containerQualifiedName);
                return;

            case EnumDeclStmt @enum:
                VisitEnumDecl(@enum, scope, containerQualifiedName);
                return;

            case IfStmt ifStmt:
                VisitExpr(ifStmt.Condition, scope);
                VisitBlock(ifStmt.ThenBlock, scope, containerQualifiedName);
                if (ifStmt.ElseBranch is not null)
                    VisitElseBranch(ifStmt.ElseBranch, scope, containerQualifiedName);
                return;

            case WhileStmt whileStmt:
                VisitExpr(whileStmt.Condition, scope);
                VisitBlock(whileStmt.Body, scope, containerQualifiedName);
                return;

            case DoWhileStmt doWhileStmt:
                VisitLoopBody(doWhileStmt.Body, scope, containerQualifiedName);
                VisitExpr(doWhileStmt.Condition, scope);
                return;

            case ForStmt forStmt:
                VisitForStmt(forStmt, scope, containerQualifiedName);
                return;

            case ForeachStmt foreachStmt:
                VisitForeachStmt(foreachStmt, scope, containerQualifiedName);
                return;

            case MatchStmt matchStmt:
                VisitMatchStmt(matchStmt, scope, containerQualifiedName);
                return;

            case TryStmt tryStmt:
                VisitTryStmt(tryStmt, scope, containerQualifiedName);
                return;

            case ThrowStmt throwStmt:
                VisitExpr(throwStmt.Value, scope);
                return;

            case ReturnStmt returnStmt:
                if (returnStmt.Value is not null)
                    VisitExpr(returnStmt.Value, scope);
                return;

            case SetFieldStmt setField:
                VisitExpr(setField.Target, scope);
                RecordQualifiedMemberReference(setField.Target, setField.Field, setField.Line, setField.Col, setField.OriginFile, scope);
                VisitExpr(setField.Value, scope);
                return;

            default:
                return;
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
        DeclareValueSymbol(varDecl, scope, containerQualifiedName, 13, "var", synthetic, TryGetFunctionSignature(varDecl.Value), valueTypeQualifiedName);
    }

    private void VisitConstDecl(ConstDecl constDecl, SemanticScope scope, string? containerQualifiedName)
    {
        VisitExpr(constDecl.Value, scope);
        bool synthetic = IsSyntheticGeneratedName(constDecl.Name);
        string? valueTypeQualifiedName = TryInferValueTypeQualifiedName(constDecl.Value, scope);
        DeclareValueSymbol(constDecl, scope, containerQualifiedName, 14, "const", synthetic, TryGetFunctionSignature(constDecl.Value), valueTypeQualifiedName);
    }

    private void VisitFunctionDecl(FuncDeclStmt func, SemanticScope scope)
    {
        SemanticScope functionScope = new(scope);
        DeclareParameters(func.Parameters, func.RestParameter, functionScope, func.OriginFile, func.Line, func.Col);
        VisitBlockStatements(func.Body.Statements, functionScope, null);
    }

    private void VisitClassDecl(ClassDeclStmt classDecl, SemanticScope scope, string? containerQualifiedName)
    {
        CfgsSymbol classSymbol = DeclareClassSymbol(classDecl, scope, containerQualifiedName, synthetic: false);
        string qualifiedClassName = classSymbol.QualifiedName;
        string? baseQualifiedName = ResolveBaseQualifiedName(classDecl, containerQualifiedName);

        if (!string.IsNullOrWhiteSpace(classDecl.BaseName))
            RecordTypeReference(classDecl.BaseName, classDecl.Line, classDecl.Col, classDecl.OriginFile, scope);

        foreach (string interfaceName in classDecl.ImplementedInterfaces)
            RecordTypeReference(interfaceName, classDecl.Line, classDecl.Col, classDecl.OriginFile, scope);

        foreach (FuncDeclStmt method in classDecl.Methods)
            DeclareFunctionSymbol(method, scope, qualifiedClassName, isMethod: true, synthetic: false);

        foreach (FuncDeclStmt staticMethod in classDecl.StaticMethods)
            DeclareFunctionSymbol(staticMethod, scope, qualifiedClassName, isMethod: true, synthetic: false);

        foreach (ClassDeclStmt nestedClass in classDecl.NestedClasses)
            DeclareClassSymbol(nestedClass, scope, qualifiedClassName, synthetic: false);

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

        foreach (FuncDeclStmt staticMethod in classDecl.StaticMethods)
            VisitMethodBody(staticMethod, scope, qualifiedClassName, baseQualifiedName);

        foreach (FuncDeclStmt method in classDecl.Methods)
            VisitMethodBody(method, scope, qualifiedClassName, baseQualifiedName);

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

    private void VisitMethodBody(FuncDeclStmt method, SemanticScope parentScope, string classQualifiedName, string? baseQualifiedName)
    {
        SemanticScope methodScope = new(parentScope, classQualifiedName, baseQualifiedName);
        DeclareParameters(method.Parameters, method.RestParameter, methodScope, method.OriginFile, method.Line, method.Col);
        VisitBlockStatements(method.Body.Statements, methodScope, null);
    }

    private void VisitEnumDecl(EnumDeclStmt enumDecl, SemanticScope scope, string? containerQualifiedName)
    {
        CfgsSymbol enumSymbol = DeclareEnumSymbol(enumDecl, scope, containerQualifiedName, synthetic: false);
        foreach (EnumMemberNode member in enumDecl.Members)
            DeclareEnumMemberSymbol(member, enumSymbol.QualifiedName);
    }

    private void VisitElseBranch(Stmt elseBranch, SemanticScope scope, string? containerQualifiedName)
    {
        if (elseBranch is BlockStmt elseBlock)
        {
            VisitBlock(elseBlock, scope, containerQualifiedName);
            return;
        }

        SemanticScope elseScope = new(scope);
        VisitStatement(elseBranch, elseScope, containerQualifiedName);
    }

    private void VisitBlock(BlockStmt block, SemanticScope parentScope, string? containerQualifiedName)
    {
        SemanticScope blockScope = new(parentScope);
        VisitBlockStatements(block.Statements, blockScope, containerQualifiedName);
    }

    private void VisitBlockStatements(IEnumerable<Stmt> statements, SemanticScope scope, string? containerQualifiedName)
    {
        PredeclareStatements(statements, scope, containerQualifiedName);
        foreach (Stmt stmt in statements)
            VisitStatement(stmt, scope, containerQualifiedName);
    }

    private void VisitForStmt(ForStmt forStmt, SemanticScope scope, string? containerQualifiedName)
    {
        SemanticScope loopScope = new(scope);

        if (forStmt.Init is not null)
            VisitStatement(forStmt.Init, loopScope, containerQualifiedName);

        if (forStmt.Condition is not null)
            VisitExpr(forStmt.Condition, loopScope);

        if (forStmt.Increment is not null)
            VisitStatement(forStmt.Increment, loopScope, containerQualifiedName);

        VisitBlockStatements(forStmt.Body.Statements, new SemanticScope(loopScope), containerQualifiedName);
    }

    private void VisitForeachStmt(ForeachStmt foreachStmt, SemanticScope scope, string? containerQualifiedName)
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

        VisitLoopBody(foreachStmt.Body, loopScope, containerQualifiedName);
    }

    private void VisitLoopBody(Stmt body, SemanticScope loopScope, string? containerQualifiedName)
    {
        if (body is BlockStmt bodyBlock)
        {
            VisitBlock(bodyBlock, loopScope, containerQualifiedName);
            return;
        }

        VisitStatement(body, loopScope, containerQualifiedName);
    }

    private void VisitMatchStmt(MatchStmt matchStmt, SemanticScope scope, string? containerQualifiedName)
    {
        VisitExpr(matchStmt.Expression, scope);

        foreach (CaseClause clause in matchStmt.Cases)
        {
            SemanticScope caseScope = new(scope);
            BindPattern(clause.Pattern, caseScope, declareBindings: true, containerQualifiedName);
            if (clause.Guard is not null)
                VisitExpr(clause.Guard, caseScope);
            VisitBlockStatements(clause.Body.Statements, new SemanticScope(caseScope), containerQualifiedName);
        }

        if (matchStmt.DefaultCase is not null)
            VisitBlock(matchStmt.DefaultCase, scope, containerQualifiedName);
    }

    private void VisitTryStmt(TryStmt tryStmt, SemanticScope scope, string? containerQualifiedName)
    {
        VisitBlock(tryStmt.TryBlock, scope, containerQualifiedName);

        if (tryStmt.CatchBlock is not null)
        {
            SemanticScope catchScope = new(scope);
            if (!string.IsNullOrWhiteSpace(tryStmt.CatchIdent))
                DeclareCatchSymbol(tryStmt.CatchIdent!, tryStmt.CatchBlock.Line, tryStmt.CatchBlock.Col, tryStmt.CatchBlock.OriginFile, catchScope, containerQualifiedName);
            VisitBlockStatements(tryStmt.CatchBlock.Statements, new SemanticScope(catchScope), containerQualifiedName);
        }

        if (tryStmt.FinallyBlock is not null)
            VisitBlock(tryStmt.FinallyBlock, scope, containerQualifiedName);
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
                foreach (Expr item in arrayExpr.Elements)
                    VisitExpr(item, scope);
                return;

            case DictExpr dictExpr:
                foreach ((Expr Key, Expr Value) pair in dictExpr.Pairs)
                {
                    VisitExpr(pair.Key, scope);
                    VisitExpr(pair.Value, scope);
                }
                return;

            case IndexExpr indexExpr:
                if (TryRecordQualifiedTypePath(indexExpr, scope))
                    return;
                if (indexExpr.Target is not null)
                    VisitExpr(indexExpr.Target, scope);
                if (indexExpr.Target is not null && indexExpr.Index is StringExpr stringIndex)
                    RecordQualifiedMemberReference(indexExpr.Target, stringIndex.Value, indexExpr.Line, indexExpr.Col, indexExpr.OriginFile, scope);
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
                    VisitExpr(callExpr.Target, scope);
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

    private void VisitFunctionExpr(FuncExpr funcExpr, SemanticScope scope)
    {
        SemanticScope functionScope = new(scope);
        DeclareParameters(funcExpr.Parameters, funcExpr.RestParameter, functionScope, funcExpr.OriginFile, funcExpr.Line, funcExpr.Col);
        VisitBlockStatements(funcExpr.Body.Statements, functionScope, null);
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
        if (!TryExtractQualifiedAccess(target, scope, out string? path))
            return;

        string qualified = $"{path}.{memberName}";
        if (TryResolveByExactName(qualified, out CfgsSymbol? symbol))
            AddOccurrence(symbol!, line, column, originFile, memberName, isDeclaration: false);
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
        switch (expr)
        {
            case VarExpr varExpr:
                path = scope.ResolveContextualQualifier(varExpr.Name);
                if (path is not null)
                    return true;

                if (scope.TryGetResolvedValueType(varExpr.Name, out string? resolvedType))
                {
                    path = resolvedType;
                    return true;
                }

                path = TryResolveNamedContainer(varExpr.Name, scope);
                return path is not null;

            case IndexExpr indexExpr
                when indexExpr.Target is not null
                && indexExpr.Index is StringExpr stringIndex
                && TryExtractQualifiedAccess(indexExpr.Target, scope, out string? targetPath):
                path = TryResolveMemberValueType(targetPath!, stringIndex.Value);
                return path is not null;

            case GetFieldExpr getFieldExpr when TryExtractQualifiedAccess(getFieldExpr.Target, scope, out string? targetPath):
                path = TryResolveMemberValueType(targetPath!, getFieldExpr.Field);
                return path is not null;

            default:
                path = null;
                return false;
        }
    }

    private void TrackMemberValueType(string containerQualifiedName, string memberName, Expr? value, SemanticScope scope)
    {
        string? valueTypeQualifiedName = TryInferValueTypeQualifiedName(value, scope);
        if (string.IsNullOrWhiteSpace(valueTypeQualifiedName))
            return;

        _memberTypesByQualifiedName[$"{containerQualifiedName}.{memberName}"] = valueTypeQualifiedName!;
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
    {
        string qualified = $"{containerPath}.{memberName}";
        if (_memberTypesByQualifiedName.TryGetValue(qualified, out string? memberType))
            return memberType;

        if (TryResolveByExactName(qualified, out CfgsSymbol? symbol) && symbol!.Kind is 5 or 10 or 11)
            return symbol.QualifiedName;

        return null;
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
        string prefix = func.IsAsync ? "async func" : "func";
        string label = $"{prefix} {func.Name}({string.Join(", ", func.Parameters)})";
        CfgsSignature signature = new(
            label,
            func.Parameters.ToList(),
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

    private void DeclareValueSymbol(Node node, SemanticScope scope, string? containerQualifiedName, int kind, string prefix, bool synthetic, CfgsSignature? signature, string? valueTypeQualifiedName)
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
            signature);

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

    private CfgsSignature? TryGetFunctionSignature(Expr? expr)
    {
        if (expr is not FuncExpr funcExpr)
            return null;

        string prefix = funcExpr.IsAsync ? "async func" : "func";
        string label = $"{prefix} ({string.Join(", ", funcExpr.Parameters)})";
        return new CfgsSignature(label, funcExpr.Parameters.ToList(), funcExpr.RestParameter, funcExpr.MinArgs);
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
        CfgsSignature? signature)
    {
        if (_symbolsByNode.TryGetValue(nodeKey, out CfgsSymbol? existing))
            return existing;

        string symbolId = $"sym_{_nextSymbolId++}";
        string? source = _sources.GetSourceText(originFile);
        RangeInfo selectionRange = TextLocator.CreateRange(source, line, column, name);
        string uri = _sources.ToDocumentUri(originFile, _documentUri);

        CfgsSymbol symbol = new(
            name,
            qualifiedName,
            kind,
            uri,
            originFile,
            selectionRange,
            selectionRange,
            detail,
            CreateHoverText(hoverHeader, uri),
            children,
            synthetic,
            symbolId,
            signature);

        _symbolsByNode[nodeKey] = symbol;
        _symbols.Add(symbol);
        _symbolsById[symbolId] = symbol;

        if (addOccurrence)
            _occurrences.Add(new CfgsResolvedOccurrence(uri, selectionRange, symbolId, IsDeclaration: true));

        return symbol;
    }

    private void AddOccurrence(CfgsSymbol symbol, int line, int column, string originFile, string token, bool isDeclaration)
    {
        if (symbol.Id is null)
            return;

        string uri = _sources.ToDocumentUri(originFile, _documentUri);
        string? source = _sources.GetSourceText(originFile);
        RangeInfo range = TextLocator.CreateRange(source, line, column, token);
        _occurrences.Add(new CfgsResolvedOccurrence(uri, range, symbol.Id, isDeclaration));
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

    private sealed class SemanticScope
    {
        private readonly Dictionary<string, CfgsSymbol> _symbols = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _resolvedValueTypes = new(StringComparer.Ordinal);

        public SemanticScope(SemanticScope? parent, string? currentClassQualifiedName = null, string? currentBaseQualifiedName = null)
        {
            Parent = parent;
            CurrentClassQualifiedName = currentClassQualifiedName ?? parent?.CurrentClassQualifiedName;
            CurrentBaseQualifiedName = currentBaseQualifiedName ?? parent?.CurrentBaseQualifiedName;
        }

        public SemanticScope? Parent { get; }

        public string? CurrentClassQualifiedName { get; }

        public string? CurrentBaseQualifiedName { get; }

        public void Declare(string name, CfgsSymbol symbol, string? valueTypeQualifiedName)
        {
            _symbols[name] = symbol;
            SetResolvedValueType(name, valueTypeQualifiedName);
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

        public void SetResolvedValueType(string name, string? valueTypeQualifiedName)
        {
            if (string.IsNullOrWhiteSpace(valueTypeQualifiedName))
            {
                if (_symbols.ContainsKey(name) || Parent is null || !Parent.TryResolve(name, out _))
                    _resolvedValueTypes.Remove(name);
                else
                    Parent.SetResolvedValueType(name, null);
                return;
            }

            if (_symbols.ContainsKey(name) || Parent is null || !Parent.TryResolve(name, out _))
            {
                _resolvedValueTypes[name] = valueTypeQualifiedName!;
                return;
            }

            Parent.SetResolvedValueType(name, valueTypeQualifiedName);
        }

        public string? ResolveContextualQualifier(string name)
            => name switch
            {
                "this" => CurrentClassQualifiedName,
                "type" => CurrentClassQualifiedName,
                "super" => CurrentBaseQualifiedName,
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

        int searchStart = absoluteIndex;
        while (searchStart < sourceText.Length && char.IsWhiteSpace(sourceText[searchStart]))
            searchStart++;

        if (searchStart < sourceText.Length && sourceText[searchStart] == '(')
            return searchStart;

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
