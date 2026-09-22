using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore;
using CFGS_VM.VMCore.Codegen;
using CFGS_VM.VMCore.Extensions;

namespace CFGS_VM.Analytic.Semantics
{
    internal sealed class MemberUsageValidator(Compiler compiler)
    {
        private static readonly HashSet<string> EmptyLocals = new(StringComparer.Ordinal);
        private readonly Compiler _compiler = compiler;
        private readonly MemberAccessRules _memberAccessRules = new();
        private readonly NamespaceAccessRules _namespaceAccessRules = new();
        private readonly Stack<HashSet<string>> _localVarsStack = new();
        private readonly Stack<string> _namespacePathStack = new();
        private ClassDeclStmt? _currentClassDecl;
        private bool _currentMethodIsStatic;
        private ReceiverContextKind _receiverContext;
        private bool _insidePropertyAccessor;
        private bool _currentPropertyHasBackingField;
        private bool _suppressNamespaceValueCheck;

        private ISet<string> CurrentLocals => _localVarsStack.Count > 0 ? _localVarsStack.Peek() : EmptyLocals;
        private string? CurrentNamespacePath => _namespacePathStack.Count > 0 ? _namespacePathStack.Peek() : null;

        public void Validate(IEnumerable<Stmt> statements)
        {
            _namespaceAccessRules.Build(statements);
            foreach (Stmt stmt in statements)
                ValidateStatement(stmt);
        }

        private void ValidateStatement(Stmt stmt)
        {
            switch (stmt)
            {
                case ExportStmt exportStmt:
                    ValidateStatement(exportStmt.Inner);
                    return;

                case EmptyStmt:
                case BreakStmt:
                case ContinueStmt:
                case DeleteVarStmt:
                case EnumDeclStmt:
                    return;

                case InterfaceDeclStmt interfaceDecl:
                    ValidateInterfaceDecl(interfaceDecl);
                    return;

                case ExprStmt exprStmt:
                    ValidateExpr(exprStmt.Expression);
                    return;

                case VarDecl varDecl:
                    if (varDecl.Value is not null)
                    {
                        bool suppress = varDecl.IsSyntheticNamespaceRoot ||
                            varDecl.Name.StartsWith("__ns_scope_", StringComparison.Ordinal);
                        WithNamespaceValueCheckSuppressed(suppress, () => ValidateExpr(varDecl.Value));
                    }
                    AddLocal(varDecl.Name, varDecl);
                    return;

                case ConstDecl constDecl:
                    ValidateExpr(constDecl.Value);
                    AddLocal(constDecl.Name, constDecl);
                    return;

                case DestructureDeclStmt destructureDecl:
                    ValidatePattern(destructureDecl.Pattern);
                    ValidateExpr(destructureDecl.Value);
                    AddPatternBindings(destructureDecl.Pattern);
                    return;

                case DestructureAssignStmt destructureAssign:
                    ValidatePattern(destructureAssign.Pattern);
                    ValidateExpr(destructureAssign.Value);
                    return;

                case AssignStmt assignStmt:
                    if (_insidePropertyAccessor && IsPropertyBackingFieldIdentifier(assignStmt.Name))
                    {
                        ValidatePropertyBackingFieldUsage(assignStmt.Name, assignStmt);
                        ValidateExpr(assignStmt.Value);
                        return;
                    }

                    ValidateReceiverAssignment(assignStmt.Name, assignStmt);
                    ValidateImplicitMemberReference(assignStmt.Name, assignStmt);
                    ValidateExpr(assignStmt.Value);
                    return;

                case AssignIndexExprStmt assignIndexExpr:
                    ValidateStoreTarget(assignIndexExpr.Target);
                    ValidateExpr(assignIndexExpr.Value);
                    return;

                case AssignExprStmt assignExpr:
                    WithNamespaceValueCheckSuppressed(assignExpr.IsSyntheticNamespacePath, () =>
                    {
                        ValidateStoreTarget(assignExpr.Target);
                        ValidateExpr(assignExpr.Value);
                    });
                    return;

                case CompoundAssignStmt compoundAssign:
                    ValidateStoreTarget(compoundAssign.Target);
                    ValidateExpr(compoundAssign.Value);
                    return;

                case SliceSetStmt sliceSet:
                    ValidateExpr(sliceSet.Slice);
                    ValidateExpr(sliceSet.Value);
                    return;

                case PushStmt pushStmt:
                    ValidateExpr(pushStmt.Target);
                    ValidateExpr(pushStmt.Value);
                    return;

                case DeleteIndexStmt deleteIndex:
                    ValidateExpr(deleteIndex.Index);
                    return;

                case DeleteExprStmt deleteExpr:
                    ValidateExpr(deleteExpr.Target);
                    return;

                case DeleteAllStmt deleteAll:
                    ValidateExpr(deleteAll.Target);
                    return;

                case ClassDeclStmt classDecl:
                    ValidateClassDecl(classDecl);
                    return;

                case FuncDeclStmt funcDecl:
                    ValidateFunctionDecl(funcDecl);
                    AddLocal(funcDecl.Name, funcDecl);
                    return;

                case IfStmt ifStmt:
                    ValidateExpr(ifStmt.Condition);
                    ValidateStatement(ifStmt.ThenBlock);
                    if (ifStmt.ElseBranch is not null)
                        ValidateStatement(ifStmt.ElseBranch);
                    return;

                case WhileStmt whileStmt:
                    ValidateExpr(whileStmt.Condition);
                    ValidateStatement(whileStmt.Body);
                    return;

                case DoWhileStmt doWhileStmt:
                    ValidateStatement(doWhileStmt.Body);
                    ValidateExpr(doWhileStmt.Condition);
                    return;

                case ForStmt forStmt:
                    if (forStmt.Init is not null)
                        ValidateStatement(forStmt.Init);
                    if (forStmt.Condition is not null)
                        ValidateExpr(forStmt.Condition);
                    if (forStmt.Increment is not null)
                        ValidateStatement(forStmt.Increment);
                    ValidateStatement(forStmt.Body);
                    return;

                case ForeachStmt foreachStmt:
                    ValidateExpr(foreachStmt.Iterable);
                    AddLocal(foreachStmt.VarName, foreachStmt);
                    if (foreachStmt.DeclareLocal && foreachStmt.TargetPattern is not null)
                        AddPatternBindings(foreachStmt.TargetPattern);
                    ValidateStatement(foreachStmt.Body);
                    return;

                case MatchStmt matchStmt:
                    ValidateExpr(matchStmt.Expression);
                    foreach (CaseClause clause in matchStmt.Cases)
                    {
                        ValidatePattern(clause.Pattern);
                        WithPatternBindings(clause.Pattern, () =>
                        {
                            if (clause.Guard is not null)
                                ValidateExpr(clause.Guard);
                            ValidateStatement(clause.Body);
                        });
                    }

                    if (matchStmt.DefaultCase is not null)
                        ValidateStatement(matchStmt.DefaultCase);
                    return;

                case TryStmt tryStmt:
                    ValidateStatement(tryStmt.TryBlock);
                    if (tryStmt.CatchBlock is not null)
                        ValidateStatement(tryStmt.CatchBlock);
                    if (tryStmt.FinallyBlock is not null)
                        ValidateStatement(tryStmt.FinallyBlock);
                    return;

                case ThrowStmt throwStmt:
                    ValidateExpr(throwStmt.Value);
                    return;

                case ReturnStmt returnStmt:
                    if (returnStmt.Value is not null)
                        ValidateExpr(returnStmt.Value);
                    return;

                case YieldStmt:
                    return;

                case SetFieldStmt setField:
                    ValidateExpr(setField.Target);
                    ValidateExpr(setField.Value);
                    return;

                case UsingStmt usingStmt:
                    ValidateExpr(usingStmt.Resource);
                    if (!string.IsNullOrWhiteSpace(usingStmt.BindingName))
                        AddLocal(usingStmt.BindingName, usingStmt);
                    ValidateStatement(usingStmt.Body);
                    return;

                case BlockStmt block:
                    if (Compiler.TryGetNamespaceScopePath(block, out string namespacePath))
                    {
                        _namespacePathStack.Push(namespacePath);
                        try
                        {
                            foreach (Stmt inner in block.Statements)
                                ValidateStatement(inner);
                        }
                        finally
                        {
                            _namespacePathStack.Pop();
                        }

                        return;
                    }

                    foreach (Stmt inner in block.Statements)
                        ValidateStatement(inner);
                    return;
            }
        }

        private void ValidateClassDecl(ClassDeclStmt classDecl)
        {
            ClassDeclStmt? previousClassDecl = _currentClassDecl;
            bool previousMethodIsStatic = _currentMethodIsStatic;
            ReceiverContextKind previousReceiverContext = _receiverContext;

            _currentClassDecl = null;
            _currentMethodIsStatic = false;
            _receiverContext = ReceiverContextKind.None;

            try
            {
                ValidateConstructorParameterSpecs(classDecl);

                _namespaceAccessRules.ValidateQualifiedTypeAccess(
                    classDecl.BaseName,
                    CurrentNamespacePath,
                    classDecl,
                    NamespaceAccessRules.NamespaceSymbolKind.Class);

                foreach (string interfaceName in classDecl.ImplementedInterfaces)
                {
                    _namespaceAccessRules.ValidateQualifiedTypeAccess(
                        interfaceName,
                        CurrentNamespacePath,
                        classDecl,
                        NamespaceAccessRules.NamespaceSymbolKind.Interface);
                }

                HashSet<string> constructorLocals = BuildConstructorLocalScope(classDecl.GetEffectiveConstructorSignature());
                _localVarsStack.Push(new HashSet<string>(constructorLocals, StringComparer.Ordinal));
                try
                {
                    foreach (Expr baseCtorArg in classDecl.BaseCtorArgs)
                        ValidateExpr(baseCtorArg);

                    foreach (Expr? fieldValue in classDecl.Fields.Values)
                    {
                        if (fieldValue is not null)
                            ValidateExpr(fieldValue);
                    }
                }
                finally
                {
                    _localVarsStack.Pop();
                }

                foreach (Expr? staticFieldValue in classDecl.StaticFields.Values)
                {
                    if (staticFieldValue is not null)
                        ValidateExpr(staticFieldValue);
                }

                foreach (PropertyDeclStmt property in classDecl.Properties)
                    ValidateClassProperty(property, classDecl, isStaticProperty: false, constructorLocals);

                foreach (PropertyDeclStmt staticProperty in classDecl.StaticProperties)
                    ValidateClassProperty(staticProperty, classDecl, isStaticProperty: true);

                foreach (FuncDeclStmt method in classDecl.Methods)
                    ValidateClassMethod(method, classDecl, isStaticMethod: false);

                foreach (FuncDeclStmt staticMethod in classDecl.StaticMethods)
                    ValidateClassMethod(staticMethod, classDecl, isStaticMethod: true);

                foreach (ClassDeclStmt nestedClass in classDecl.NestedClasses)
                    ValidateClassDecl(nestedClass);
            }
            finally
            {
                _currentClassDecl = previousClassDecl;
                _currentMethodIsStatic = previousMethodIsStatic;
                _receiverContext = previousReceiverContext;
            }
        }

        private void ValidateInterfaceDecl(InterfaceDeclStmt interfaceDecl)
        {
            foreach (string baseInterface in interfaceDecl.BaseInterfaces)
            {
                _namespaceAccessRules.ValidateQualifiedTypeAccess(
                    baseInterface,
                    CurrentNamespacePath,
                    interfaceDecl,
                    NamespaceAccessRules.NamespaceSymbolKind.Interface);
            }

            foreach (InterfaceMethodDecl method in interfaceDecl.Methods)
                ValidateInterfaceMethodDecl(method);
        }

        private void ValidateClassProperty(PropertyDeclStmt propertyDecl, ClassDeclStmt classDecl, bool isStaticProperty, HashSet<string>? constructorLocals = null)
        {
            ClassDeclStmt? previousClassDecl = _currentClassDecl;
            bool previousMethodIsStatic = _currentMethodIsStatic;
            ReceiverContextKind previousReceiverContext = _receiverContext;
            bool previousInsidePropertyAccessor = _insidePropertyAccessor;
            bool previousPropertyHasBackingField = _currentPropertyHasBackingField;

            _currentClassDecl = classDecl;
            _currentMethodIsStatic = isStaticProperty;
            _receiverContext = isStaticProperty ? ReceiverContextKind.StaticMethod : ReceiverContextKind.InstanceMethod;
            _currentPropertyHasBackingField = propertyDecl.HasAutoStorage;

            try
            {
                if (propertyDecl.Initializer is not null)
                {
                    if (constructorLocals is null)
                    {
                        ValidateExpr(propertyDecl.Initializer);
                    }
                    else
                    {
                        _localVarsStack.Push(new HashSet<string>(constructorLocals, StringComparer.Ordinal));
                        try
                        {
                            ValidateExpr(propertyDecl.Initializer);
                        }
                        finally
                        {
                            _localVarsStack.Pop();
                        }
                    }
                }

                foreach (PropertyAccessorDecl accessor in propertyDecl.Accessors)
                {
                    if (accessor.Body == null)
                        continue;

                    _insidePropertyAccessor = true;
                    HashSet<string> locals = new(StringComparer.Ordinal)
                    {
                        isStaticProperty ? "type" : "this"
                    };

                    if (!string.IsNullOrWhiteSpace(accessor.ValueParameterName))
                    {
                        ValidatePropertyFieldBindingName(accessor.ValueParameterName, accessor);
                        locals.Add(accessor.ValueParameterName);
                    }

                    _localVarsStack.Push(locals);
                    try
                    {
                        ValidateStatement(accessor.Body);
                    }
                    finally
                    {
                        _localVarsStack.Pop();
                        _insidePropertyAccessor = previousInsidePropertyAccessor;
                    }
                }
            }
            finally
            {
                _currentClassDecl = previousClassDecl;
                _currentMethodIsStatic = previousMethodIsStatic;
                _receiverContext = previousReceiverContext;
                _insidePropertyAccessor = previousInsidePropertyAccessor;
                _currentPropertyHasBackingField = previousPropertyHasBackingField;
            }
        }

        private void ValidateClassMethod(FuncDeclStmt funcDecl, ClassDeclStmt classDecl, bool isStaticMethod)
        {
            ClassDeclStmt? previousClassDecl = _currentClassDecl;
            bool previousMethodIsStatic = _currentMethodIsStatic;
            ReceiverContextKind previousReceiverContext = _receiverContext;

            _currentClassDecl = classDecl;
            _currentMethodIsStatic = isStaticMethod;
            _receiverContext = isStaticMethod ? ReceiverContextKind.StaticMethod : ReceiverContextKind.InstanceMethod;

            HashSet<string> locals = new(funcDecl.Parameters, StringComparer.Ordinal);
            locals.Add(isStaticMethod ? "type" : "this");
            _localVarsStack.Push(locals);

            try
            {
                foreach (FunctionParameterSpec parameter in funcDecl.ParameterSpecs)
                {
                    if (parameter.DestructurePattern is not null)
                        ValidatePattern(parameter.DestructurePattern);
                    if (parameter.DefaultValue is not null)
                        ValidateExpr(parameter.DefaultValue);
                }

                ValidateStatement(funcDecl.Body);
            }
            finally
            {
                _localVarsStack.Pop();
                _currentClassDecl = previousClassDecl;
                _currentMethodIsStatic = previousMethodIsStatic;
                _receiverContext = previousReceiverContext;
            }
        }

        private void ValidateFunctionDecl(FuncDeclStmt funcDecl)
        {
            // C#-style lexical capture: a named function declared inside a method inherits the
            // enclosing receiver context (like a local function in C#), so this/type/super/outer
            // stay valid. At genuine top level the inherited context is already None.
            ReceiverContextKind previousReceiverContext = _receiverContext;

            _namespaceAccessRules.ValidateQualifiedTypeAccess(
                funcDecl.ExplicitInterfaceName,
                CurrentNamespacePath,
                funcDecl,
                NamespaceAccessRules.NamespaceSymbolKind.Interface);

            foreach (string parameterName in funcDecl.Parameters)
                ValidatePropertyFieldBindingName(parameterName, funcDecl);

            _localVarsStack.Push(new HashSet<string>(funcDecl.Parameters, StringComparer.Ordinal));
            try
            {
                foreach (FunctionParameterSpec parameter in funcDecl.ParameterSpecs)
                {
                    if (parameter.DestructurePattern is not null)
                        ValidatePattern(parameter.DestructurePattern);
                    if (parameter.DefaultValue is not null)
                        ValidateExpr(parameter.DefaultValue);
                }

                ValidateStatement(funcDecl.Body);
            }
            finally
            {
                _localVarsStack.Pop();
                _receiverContext = previousReceiverContext;
            }
        }

        private void ValidateInterfaceMethodDecl(InterfaceMethodDecl method)
        {
            bool previousMethodIsStatic = _currentMethodIsStatic;
            ReceiverContextKind previousReceiverContext = _receiverContext;
            _currentMethodIsStatic = method.IsStatic;
            _receiverContext = method.IsStatic ? ReceiverContextKind.StaticMethod : ReceiverContextKind.InstanceMethod;

            HashSet<string> locals = new(method.Parameters, StringComparer.Ordinal)
            {
                method.IsStatic ? "type" : "this"
            };

            _localVarsStack.Push(locals);
            try
            {
                foreach (FunctionParameterSpec parameter in method.ParameterSpecs)
                {
                    if (parameter.DestructurePattern is not null)
                        ValidatePattern(parameter.DestructurePattern);
                    if (parameter.DefaultValue is not null)
                        ValidateExpr(parameter.DefaultValue);
                }

                if (method.Body is not null)
                    ValidateStatement(method.Body);
            }
            finally
            {
                _localVarsStack.Pop();
                _currentMethodIsStatic = previousMethodIsStatic;
                _receiverContext = previousReceiverContext;
            }
        }

        private void ValidateExpr(Expr expr, bool asValue = true)
        {
            if (asValue && !_suppressNamespaceValueCheck && _namespaceAccessRules.IsNamespacePathExpr(expr, CurrentLocals))
            {
                throw new CompilerException(
                    "a namespace is not a value; access a member with dot notation (for example App.Core.Member)",
                    expr.Line,
                    expr.Col,
                    expr.OriginFile);
            }

            switch (expr)
            {
                case NullExpr:
                case NumberExpr:
                case StringExpr:
                case CharExpr:
                case BoolExpr:
                    return;

                case InterpolatedStringExpr interpolatedStringExpr:
                    foreach (InterpolatedStringPart part in interpolatedStringExpr.Parts)
                    {
                        if (part is InterpolatedStringExprPart exprPart)
                            ValidateExpr(exprPart.Expression);
                    }
                    return;

                case VarExpr variableExpr:
                    if (_insidePropertyAccessor && IsPropertyBackingFieldIdentifier(variableExpr.Name))
                    {
                        ValidatePropertyBackingFieldUsage(variableExpr.Name, variableExpr);
                        return;
                    }

                    ValidateReceiverUsage(variableExpr.Name, variableExpr);
                    ValidateImplicitMemberReference(variableExpr.Name, variableExpr);
                    return;

                case BinaryExpr binaryExpr:
                    ValidateExpr(binaryExpr.Left);
                    ValidateExpr(binaryExpr.Right);
                    return;

                case UnaryExpr unaryExpr:
                    ValidateExpr(unaryExpr.Right);
                    return;

                case PrefixExpr prefixExpr:
                    ValidateStoreTarget(prefixExpr.Target);
                    return;

                case PostfixExpr postfixExpr:
                    ValidateStoreTarget(postfixExpr.Target);
                    return;

                case ArrayExpr arrayExpr:
                    foreach (Expr item in arrayExpr.Elements)
                        ValidateExpr(item);
                    return;

                case DictExpr dictExpr:
                    foreach ((Expr Key, Expr Value) pair in dictExpr.Pairs)
                    {
                        ValidateExpr(pair.Key);
                        ValidateExpr(pair.Value);
                    }
                    return;

                case IndexExpr indexExpr:
                    if (indexExpr.Target is not null)
                        ValidateExpr(indexExpr.Target, asValue: false);
                    if (indexExpr.Index is not null)
                        ValidateExpr(indexExpr.Index);
                    ValidateExplicitMemberAccess(indexExpr, isStore: false);
                    return;

                case SliceExpr sliceExpr:
                    if (sliceExpr.Target is not null)
                        ValidateExpr(sliceExpr.Target);
                    if (sliceExpr.Start is not null)
                        ValidateExpr(sliceExpr.Start);
                    if (sliceExpr.End is not null)
                        ValidateExpr(sliceExpr.End);
                    return;

                case TryUnwrapExpr tryUnwrapExpr:
                    if (tryUnwrapExpr.Inner is not null)
                        ValidateExpr(tryUnwrapExpr.Inner);
                    return;

                case MethodCallExpr methodCallExpr:
                    ValidateExpr(methodCallExpr.Target, asValue: false);
                    foreach (Expr arg in methodCallExpr.Args)
                        ValidateExpr(arg);
                    return;

                case NewExpr newExpr:
                    _namespaceAccessRules.ValidateQualifiedTypeAccess(
                        newExpr.ClassName,
                        CurrentNamespacePath,
                        newExpr,
                        NamespaceAccessRules.NamespaceSymbolKind.Class);

                    foreach (Expr arg in newExpr.Args)
                        ValidateExpr(arg);
                    ValidateObjectInitializerEntries(newExpr.Initializers);
                    _memberAccessRules.ValidateNewObjectInitializers(_compiler, newExpr, _currentClassDecl);
                    return;

                case GetFieldExpr getFieldExpr:
                    ValidateExpr(getFieldExpr.Target);
                    return;

                case ConditionalExpr conditionalExpr:
                    ValidateExpr(conditionalExpr.Condition);
                    ValidateExpr(conditionalExpr.ThenExpr);
                    ValidateExpr(conditionalExpr.ElseExpr);
                    return;

                case MatchExpr matchExpr:
                    ValidateExpr(matchExpr.Scrutinee);
                    foreach (CaseExprArm arm in matchExpr.Arms)
                    {
                        ValidatePattern(arm.Pattern);
                        WithPatternBindings(arm.Pattern, () =>
                        {
                            if (arm.Guard is not null)
                                ValidateExpr(arm.Guard);
                            ValidateExpr(arm.Body);
                        });
                    }

                    if (matchExpr.DefaultArm is not null)
                        ValidateExpr(matchExpr.DefaultArm);
                    return;

                case AwaitExpr awaitExpr:
                    ValidateExpr(awaitExpr.Inner);
                    return;

                case FuncExpr funcExpr:
                    ValidateFunctionExpr(funcExpr);
                    return;

                case NamedArgExpr namedArgExpr:
                    ValidateExpr(namedArgExpr.Value);
                    return;

                case SpreadArgExpr spreadArgExpr:
                    ValidateExpr(spreadArgExpr.Value);
                    return;

                case CallExpr callExpr:
                    if (callExpr.Target is not null)
                        ValidateExpr(callExpr.Target);
                    foreach (Expr arg in callExpr.Args)
                        ValidateExpr(arg);
                    return;

                case ObjectInitExpr objectInitExpr:
                    ValidateExpr(objectInitExpr.Target);
                    ValidateObjectInitializerEntries(objectInitExpr.Inits);
                    _memberAccessRules.ValidateObjectInitializers(_compiler, objectInitExpr.Target, objectInitExpr.Inits, _currentClassDecl, CurrentLocals);
                    return;
            }
        }

        private void ValidateObjectInitializerEntries(IEnumerable<ObjectInitializerEntry> entries)
        {
            foreach (ObjectInitializerEntry entry in entries)
            {
                switch (entry)
                {
                    case ObjectMemberInitializerEntry member:
                        if (member.Value is not null)
                            ValidateExpr(member.Value);
                        ValidateObjectInitializerEntries(member.NestedInitializers);
                        break;

                    case ObjectIndexerInitializerEntry indexer:
                        ValidateExpr(indexer.Index);
                        ValidateExpr(indexer.Value);
                        break;
                }
            }
        }

        private void ValidateFunctionExpr(FuncExpr funcExpr)
        {
            ReceiverContextKind previousReceiverContext = _receiverContext;
            _receiverContext = DetermineReceiverContext(funcExpr);

            foreach (string parameterName in funcExpr.Parameters)
                ValidatePropertyFieldBindingName(parameterName, funcExpr);

            _localVarsStack.Push(new HashSet<string>(funcExpr.Parameters, StringComparer.Ordinal));
            try
            {
                foreach (FunctionParameterSpec parameter in funcExpr.ParameterSpecs)
                {
                    if (parameter.DestructurePattern is not null)
                        ValidatePattern(parameter.DestructurePattern);
                    if (parameter.DefaultValue is not null)
                        ValidateExpr(parameter.DefaultValue);
                }

                ValidateStatement(funcExpr.Body);
            }
            finally
            {
                _localVarsStack.Pop();
                _receiverContext = previousReceiverContext;
            }
        }

        private void ValidateConstructorParameterSpecs(ClassDeclStmt classDecl)
        {
            if (!classDecl.HasPrimaryConstructorHeader || classDecl.ConstructorParameterSpecs.Count == 0)
                return;

            _localVarsStack.Push(BuildConstructorLocalScope(classDecl.GetPrimaryConstructorSignature()));
            try
            {
                foreach (FunctionParameterSpec parameter in classDecl.ConstructorParameterSpecs)
                {
                    if (parameter.DestructurePattern is not null)
                        ValidatePattern(parameter.DestructurePattern);
                    if (parameter.DefaultValue is not null)
                        ValidateExpr(parameter.DefaultValue);
                }
            }
            finally
            {
                _localVarsStack.Pop();
            }
        }

        private void ValidatePattern(MatchPattern pattern)
        {
            switch (pattern)
            {
                case WildcardMatchPattern:
                case BindingMatchPattern:
                    return;

                case ValueMatchPattern valuePattern:
                    ValidateExpr(valuePattern.Value);
                    return;

                case ArrayMatchPattern arrayPattern:
                    foreach (MatchPattern element in arrayPattern.Elements)
                        ValidatePattern(element);
                    return;

                case DictMatchPattern dictPattern:
                    foreach ((string _, MatchPattern nestedPattern) in dictPattern.Entries)
                        ValidatePattern(nestedPattern);
                    return;

                case OrMatchPattern orPattern:
                    foreach (MatchPattern alternative in orPattern.Alternatives)
                        ValidatePattern(alternative);
                    return;
            }
        }

        private void ValidateStoreTarget(Expr? target)
        {
            switch (target)
            {
                case VarExpr variableExpr:
                    if (_insidePropertyAccessor && IsPropertyBackingFieldIdentifier(variableExpr.Name))
                    {
                        ValidatePropertyBackingFieldUsage(variableExpr.Name, variableExpr);
                        return;
                    }

                    ValidateReceiverAssignment(variableExpr.Name, variableExpr);
                    ValidateImplicitMemberReference(variableExpr.Name, variableExpr);
                    return;

                case IndexExpr indexExpr:
                    if (indexExpr.Target is not null)
                        ValidateExpr(indexExpr.Target, asValue: false);

                    if (indexExpr.Index is not null)
                    {
                        ValidateExpr(indexExpr.Index);
                        ValidateExplicitMemberAccess(indexExpr, isStore: true);
                    }

                    return;

                case SliceExpr sliceExpr:
                    if (sliceExpr.Target is not null)
                        ValidateExpr(sliceExpr.Target);
                    if (sliceExpr.Start is not null)
                        ValidateExpr(sliceExpr.Start);
                    if (sliceExpr.End is not null)
                        ValidateExpr(sliceExpr.End);
                    return;

                default:
                    if (target is not null)
                        ValidateExpr(target);
                    return;
            }
        }

        private void ValidateExplicitMemberAccess(IndexExpr indexExpr, bool isStore)
        {
            if (indexExpr.Index is not StringExpr memberExpr)
                return;

            string memberName = memberExpr.Value;
            if (string.IsNullOrWhiteSpace(memberName))
                return;

            _namespaceAccessRules.ValidateExplicitMemberAccess(indexExpr, CurrentLocals, CurrentNamespacePath);

            if (!indexExpr.IsDotAccess)
            {
                if (indexExpr.Target is VarExpr bracketReceiver && IsReceiverIdentifier(bracketReceiver.Name))
                {
                    throw new CompilerException(
                        "class members require dot access; bracket access is for indexable values",
                        indexExpr.Line,
                        indexExpr.Col,
                        indexExpr.OriginFile);
                }

                if (indexExpr.Target != null &&
                    _memberAccessRules.TryResolveKnownEnumFromExpr(_compiler, indexExpr.Target, CurrentLocals, out EnumDeclStmt enumDecl))
                {
                    throw new CompilerException(
                        $"enum members require dot access (use {enumDecl.Name}.{memberName}); bracket access is for indexable values such as arrays and dictionaries",
                        indexExpr.Line,
                        indexExpr.Col,
                        indexExpr.OriginFile);
                }

                if (indexExpr.Target != null &&
                    _memberAccessRules.TryResolveKnownClassDeclFromExpr(_compiler, indexExpr.Target, CurrentLocals, out _))
                {
                    throw new CompilerException(
                        "class static members require dot access; bracket access is for indexable values",
                        indexExpr.Line,
                        indexExpr.Col,
                        indexExpr.OriginFile);
                }
            }

            if (indexExpr.Target is VarExpr receiverVar && IsReceiverIdentifier(receiverVar.Name))
            {
                ValidateReceiverUsage(receiverVar.Name, receiverVar);
                if (isStore && Compiler.IsReservedRuntimeMemberName(memberName))
                {
                    throw new CompilerException(
                        $"invalid member assignment '{memberName}': reserved member name",
                        indexExpr.Line,
                        indexExpr.Col,
                        indexExpr.OriginFile);
                }

                if (_currentClassDecl == null)
                    return;

                switch (receiverVar.Name)
                {
                    case "this":
                        _memberAccessRules.ValidateMemberAccessAgainstKnownClass(
                            _compiler,
                            _currentClassDecl,
                            memberName,
                            expectInstance: true,
                            _currentClassDecl,
                            indexExpr,
                            isStore ? MemberAccessRules.MemberAccessKind.Write : MemberAccessRules.MemberAccessKind.Read);
                        return;

                    case "type":
                        _memberAccessRules.ValidateMemberAccessAgainstKnownClass(
                            _compiler,
                            _currentClassDecl,
                            memberName,
                            expectInstance: false,
                            _currentClassDecl,
                            indexExpr,
                            isStore ? MemberAccessRules.MemberAccessKind.Write : MemberAccessRules.MemberAccessKind.Read);
                        return;

                    case "super":
                        if (string.IsNullOrWhiteSpace(_currentClassDecl.BaseName))
                            return;

                        if (_compiler.TryResolveBaseClassDecl(_currentClassDecl, out ClassDeclStmt baseDecl))
                        {
                            bool expectInstance = _receiverContext == ReceiverContextKind.InstanceMethod;
                            _memberAccessRules.ValidateMemberAccessAgainstKnownClass(
                                _compiler,
                                baseDecl,
                                memberName,
                                expectInstance,
                                _currentClassDecl,
                                indexExpr,
                                isStore ? MemberAccessRules.MemberAccessKind.Write : MemberAccessRules.MemberAccessKind.Read);
                        }

                        return;
                }
            }

            if (indexExpr.Target != null &&
                _memberAccessRules.TryResolveKnownClassDeclFromExpr(_compiler, indexExpr.Target, CurrentLocals, out ClassDeclStmt decl))
            {
                if (isStore && Compiler.IsReservedRuntimeMemberName(memberName))
                {
                    throw new CompilerException(
                        $"invalid member assignment '{memberName}': reserved member name",
                        indexExpr.Line,
                        indexExpr.Col,
                        indexExpr.OriginFile);
                }

                _memberAccessRules.ValidateMemberAccessAgainstKnownClass(
                    _compiler,
                    decl,
                    memberName,
                    expectInstance: false,
                    _currentClassDecl,
                    indexExpr,
                    isStore ? MemberAccessRules.MemberAccessKind.Write : MemberAccessRules.MemberAccessKind.Read);
            }
        }

        private void WithNamespaceValueCheckSuppressed(bool suppress, Action action)
        {
            if (!suppress)
            {
                action();
                return;
            }

            bool previous = _suppressNamespaceValueCheck;
            _suppressNamespaceValueCheck = true;
            try
            {
                action();
            }
            finally
            {
                _suppressNamespaceValueCheck = previous;
            }
        }

        private void ValidateImplicitMemberReference(string name, Node node)
        {
            if (_currentClassDecl == null || IsReceiverIdentifier(name) ||
                (_currentPropertyHasBackingField && IsPropertyBackingFieldIdentifier(name)))
                return;

            if (CurrentLocals.Contains(name))
                return;

            (HashSet<string> instanceMembers, HashSet<string> staticMembers) =
                _memberAccessRules.GetOrBuildClassMemberSets(_compiler, _currentClassDecl);

            bool hasInstanceMember = !_currentMethodIsStatic && instanceMembers.Contains(name);
            bool hasStaticMember = staticMembers.Contains(name);

            if (hasInstanceMember && hasStaticMember)
            {
                throw new CompilerException(
                    $"ambiguous member reference '{name}' in class '{_currentClassDecl.Name}': both instance and static members are visible",
                    node.Line,
                    node.Col,
                    node.OriginFile);
            }
        }

        private void ValidateReceiverUsage(string name, Node node)
        {
            if (!IsReceiverIdentifier(name))
                return;

            switch (name)
            {
                case "this":
                    if (_receiverContext != ReceiverContextKind.InstanceMethod)
                    {
                        throw new CompilerException(
                            "invalid receiver usage 'this': only available in instance methods",
                            node.Line,
                            node.Col,
                            node.OriginFile);
                    }

                    return;

                case "type":
                    if (_receiverContext == ReceiverContextKind.None)
                    {
                        throw new CompilerException(
                            "invalid receiver usage 'type': only available in class methods",
                            node.Line,
                            node.Col,
                            node.OriginFile);
                    }

                    return;

                case "super":
                    if (_receiverContext == ReceiverContextKind.None)
                    {
                        throw new CompilerException(
                            "invalid receiver usage 'super': only available in class methods with a base class",
                            node.Line,
                            node.Col,
                            node.OriginFile);
                    }

                    if (_currentClassDecl == null || string.IsNullOrWhiteSpace(_currentClassDecl.BaseName))
                    {
                        throw new CompilerException(
                            "invalid receiver usage 'super': class has no base class",
                            node.Line,
                            node.Col,
                            node.OriginFile);
                    }

                    return;

                case "outer":
                    if (_receiverContext != ReceiverContextKind.InstanceMethod ||
                        _currentClassDecl == null ||
                        !_currentClassDecl.IsNested)
                    {
                        throw new CompilerException(
                            "invalid receiver usage 'outer': only available in nested instance methods",
                            node.Line,
                            node.Col,
                            node.OriginFile);
                    }

                    return;
            }
        }

        private void ValidateReceiverAssignment(string name, Node node)
        {
            if (!IsReceiverIdentifier(name))
                return;

            ValidateReceiverUsage(name, node);
            throw new CompilerException(
                $"invalid receiver assignment '{name}': receiver identifiers are read-only",
                node.Line,
                node.Col,
                node.OriginFile);
        }

        private static bool IsReceiverIdentifier(string name)
            => name == "this" || name == "type" || name == "super" || name == "outer";

        private static bool IsPropertyBackingFieldIdentifier(string name)
            => name == "field";

        private void ValidatePropertyBackingFieldUsage(string name, Node node)
        {
            if (!IsPropertyBackingFieldIdentifier(name))
                return;

            if (_currentPropertyHasBackingField)
                return;

            throw new CompilerException(
                "invalid backing-field access 'field': current property has no hidden backing storage",
                node.Line,
                node.Col,
                node.OriginFile);
        }

        private ReceiverContextKind DetermineReceiverContext(FuncExpr funcExpr)
        {
            if (_currentClassDecl == null)
                return ReceiverContextKind.None;

            if (funcExpr.Parameters.Count > 0)
            {
                switch (funcExpr.Parameters[0])
                {
                    case "this":
                        return ReceiverContextKind.InstanceMethod;
                    case "type":
                        return ReceiverContextKind.StaticMethod;
                }
            }

            // C#-style lexical capture: a lambda that does not declare its own explicit
            // receiver inherits the enclosing receiver context so this/type/super/outer
            // remain valid inside lambdas defined in methods (the runtime resolves them
            // through the captured method environment).
            return _receiverContext;
        }

        private void WithPatternBindings(MatchPattern pattern, Action action)
        {
            HashSet<string> locals = new(CurrentLocals, StringComparer.Ordinal);
            _localVarsStack.Push(locals);
            try
            {
                AddPatternBindings(pattern);
                action();
            }
            finally
            {
                _localVarsStack.Pop();
            }
        }

        private void AddPatternBindings(MatchPattern pattern)
        {
            foreach (string binding in CollectPatternBindingNames(pattern))
                AddLocal(binding, pattern);
        }

        private void AddLocal(string? name, Node? node = null)
        {
            if (_localVarsStack.Count == 0 || string.IsNullOrWhiteSpace(name))
                return;

            ValidatePropertyFieldBindingName(name, node);
            _localVarsStack.Peek().Add(name);
        }

        private void ValidatePropertyFieldBindingName(string? name, Node? node)
        {
            if (!_currentPropertyHasBackingField || name != "field")
                return;

            throw new CompilerException(
                "invalid local binding 'field': reserved backing-field identifier inside property accessors with hidden storage",
                node?.Line ?? -1,
                node?.Col ?? -1,
                node?.OriginFile ?? string.Empty);
        }

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

        private HashSet<string> BuildConstructorLocalScope(ClassConstructorSignature signature)
        {
            HashSet<string> locals = _localVarsStack.Count > 0
                ? new HashSet<string>(_localVarsStack.Peek(), StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);

            foreach (string parameter in signature.Parameters)
                locals.Add(parameter);

            foreach (FunctionParameterSpec parameter in signature.ParameterSpecs)
            {
                if (parameter.DestructurePattern is null)
                    continue;

                foreach (string binding in CollectPatternBindingNames(parameter.DestructurePattern))
                    locals.Add(binding);
            }

            return locals;
        }
    }
}
