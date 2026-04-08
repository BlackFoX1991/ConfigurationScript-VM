using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore.Extensions;

namespace CFGS_VM.Analytic.Semantics
{
    internal sealed class StatementShapeValidator
    {
        public void Validate(IEnumerable<Stmt> statements)
        {
            foreach (Stmt stmt in statements)
                ValidateStatement(stmt, insideFunction: false, insideAsyncFunction: false);
        }

        private void ValidateStatement(Stmt stmt, bool insideFunction, bool insideAsyncFunction)
        {
            switch (stmt)
            {
                case ExportStmt exportStmt:
                    ValidateStatement(exportStmt.Inner, insideFunction, insideAsyncFunction);
                    return;

                case EmptyStmt:
                case BreakStmt:
                case ContinueStmt:
                case DeleteVarStmt:
                case InterfaceDeclStmt:
                case EnumDeclStmt:
                    return;

                case ExprStmt exprStmt:
                    ValidateExpr(exprStmt.Expression, insideFunction, insideAsyncFunction);
                    return;

                case VarDecl varDecl:
                    if (varDecl.Value is not null)
                        ValidateExpr(varDecl.Value, insideFunction, insideAsyncFunction);
                    return;

                case ConstDecl constDecl:
                    ValidateExpr(constDecl.Value, insideFunction, insideAsyncFunction);
                    return;

                case DestructureDeclStmt destructureDecl:
                    ValidatePattern(destructureDecl.Pattern, insideFunction, insideAsyncFunction);
                    ValidateExpr(destructureDecl.Value, insideFunction, insideAsyncFunction);
                    return;

                case DestructureAssignStmt destructureAssign:
                    ValidatePattern(destructureAssign.Pattern, insideFunction, insideAsyncFunction);
                    ValidateExpr(destructureAssign.Value, insideFunction, insideAsyncFunction);
                    return;

                case AssignStmt assignStmt:
                    ValidateExpr(assignStmt.Value, insideFunction, insideAsyncFunction);
                    return;

                case AssignIndexExprStmt assignIndexExpr:
                    ValidateIndexExpression(assignIndexExpr.Target, insideFunction, insideAsyncFunction);
                    ValidateExpr(assignIndexExpr.Value, insideFunction, insideAsyncFunction);
                    return;

                case AssignExprStmt assignExpr:
                    ValidateStoreTarget(assignExpr.Target);
                    ValidateExpr(assignExpr.Target, insideFunction, insideAsyncFunction);
                    ValidateExpr(assignExpr.Value, insideFunction, insideAsyncFunction);
                    return;

                case CompoundAssignStmt compoundAssign:
                    ValidateStoreTarget(compoundAssign.Target);
                    ValidateExpr(compoundAssign.Target, insideFunction, insideAsyncFunction);
                    ValidateExpr(compoundAssign.Value, insideFunction, insideAsyncFunction);
                    return;

                case SliceSetStmt sliceSet:
                    ValidateExpr(sliceSet.Slice, insideFunction, insideAsyncFunction);
                    ValidateExpr(sliceSet.Value, insideFunction, insideAsyncFunction);
                    return;

                case PushStmt pushStmt:
                    if (pushStmt.Target is not VarExpr && pushStmt.Target is not IndexExpr)
                    {
                        throw new CompilerException(
                            "invalid use of 'push' []",
                            pushStmt.Line,
                            pushStmt.Col,
                            pushStmt.OriginFile);
                    }

                    ValidateExpr(pushStmt.Target, insideFunction, insideAsyncFunction);
                    ValidateExpr(pushStmt.Value, insideFunction, insideAsyncFunction);
                    return;

                case DeleteIndexStmt deleteIndex:
                    ValidateExpr(deleteIndex.Index, insideFunction, insideAsyncFunction);
                    return;

                case DeleteExprStmt deleteExpr:
                    ValidateDeleteExpr(deleteExpr);
                    ValidateExpr(deleteExpr.Target, insideFunction, insideAsyncFunction);
                    return;

                case DeleteAllStmt deleteAll:
                    if (deleteAll.Target is not VarExpr && deleteAll.Target is not IndexExpr && deleteAll.Target is not SliceExpr)
                    {
                        throw new CompilerException(
                            "invalid use of 'delete'",
                            deleteAll.Line,
                            deleteAll.Col,
                            deleteAll.OriginFile);
                    }

                    ValidateExpr(deleteAll.Target, insideFunction, insideAsyncFunction);
                    return;

                case ClassDeclStmt classDecl:
                    ValidateClassDecl(classDecl, insideFunction, insideAsyncFunction);
                    return;

                case FuncDeclStmt funcDecl:
                    ValidateFunctionDecl(funcDecl);
                    return;

                case IfStmt ifStmt:
                    ValidateExpr(ifStmt.Condition, insideFunction, insideAsyncFunction);
                    ValidateStatement(ifStmt.ThenBlock, insideFunction, insideAsyncFunction);
                    if (ifStmt.ElseBranch is not null)
                        ValidateStatement(ifStmt.ElseBranch, insideFunction, insideAsyncFunction);
                    return;

                case WhileStmt whileStmt:
                    ValidateExpr(whileStmt.Condition, insideFunction, insideAsyncFunction);
                    ValidateStatement(whileStmt.Body, insideFunction, insideAsyncFunction);
                    return;

                case DoWhileStmt doWhileStmt:
                    ValidateStatement(doWhileStmt.Body, insideFunction, insideAsyncFunction);
                    ValidateExpr(doWhileStmt.Condition, insideFunction, insideAsyncFunction);
                    return;

                case ForStmt forStmt:
                    if (forStmt.Init is not null)
                        ValidateStatement(forStmt.Init, insideFunction, insideAsyncFunction);
                    if (forStmt.Condition is not null)
                        ValidateExpr(forStmt.Condition, insideFunction, insideAsyncFunction);
                    if (forStmt.Increment is not null)
                        ValidateStatement(forStmt.Increment, insideFunction, insideAsyncFunction);
                    ValidateStatement(forStmt.Body, insideFunction, insideAsyncFunction);
                    return;

                case ForeachStmt foreachStmt:
                    if (foreachStmt.TargetPattern is not null)
                        ValidatePattern(foreachStmt.TargetPattern, insideFunction, insideAsyncFunction);
                    ValidateExpr(foreachStmt.Iterable, insideFunction, insideAsyncFunction);
                    ValidateStatement(foreachStmt.Body, insideFunction, insideAsyncFunction);
                    return;

                case MatchStmt matchStmt:
                    ValidateExpr(matchStmt.Expression, insideFunction, insideAsyncFunction);
                    foreach (CaseClause clause in matchStmt.Cases)
                    {
                        ValidatePattern(clause.Pattern, insideFunction, insideAsyncFunction);
                        if (clause.Guard is not null)
                            ValidateExpr(clause.Guard, insideFunction, insideAsyncFunction);
                        ValidateStatement(clause.Body, insideFunction, insideAsyncFunction);
                    }

                    if (matchStmt.DefaultCase is not null)
                        ValidateStatement(matchStmt.DefaultCase, insideFunction, insideAsyncFunction);
                    return;

                case TryStmt tryStmt:
                    ValidateStatement(tryStmt.TryBlock, insideFunction, insideAsyncFunction);
                    if (tryStmt.CatchBlock is not null)
                        ValidateStatement(tryStmt.CatchBlock, insideFunction, insideAsyncFunction);
                    if (tryStmt.FinallyBlock is not null)
                        ValidateStatement(tryStmt.FinallyBlock, insideFunction, insideAsyncFunction);
                    return;

                case ThrowStmt throwStmt:
                    ValidateExpr(throwStmt.Value, insideFunction, insideAsyncFunction);
                    return;

                case ReturnStmt returnStmt:
                    if (returnStmt.Value is not null)
                        ValidateExpr(returnStmt.Value, insideFunction, insideAsyncFunction);
                    return;

                case YieldStmt yieldStmt:
                    ValidateYield(yieldStmt, insideFunction, insideAsyncFunction);
                    return;

                case SetFieldStmt setField:
                    ValidateExpr(setField.Target, insideFunction, insideAsyncFunction);
                    ValidateExpr(setField.Value, insideFunction, insideAsyncFunction);
                    return;

                case UsingStmt usingStmt:
                    ValidateExpr(usingStmt.Resource, insideFunction, insideAsyncFunction);
                    ValidateStatement(usingStmt.Body, insideFunction, insideAsyncFunction);
                    return;

                case BlockStmt block:
                    foreach (Stmt inner in block.Statements)
                        ValidateStatement(inner, insideFunction, insideAsyncFunction);
                    return;
            }
        }

        private void ValidateClassDecl(ClassDeclStmt classDecl, bool insideFunction, bool insideAsyncFunction)
        {
            foreach (Expr baseCtorArg in classDecl.BaseCtorArgs)
                ValidateExpr(baseCtorArg, insideFunction, insideAsyncFunction);

            foreach (Expr? fieldValue in classDecl.Fields.Values)
            {
                if (fieldValue is not null)
                    ValidateExpr(fieldValue, insideFunction, insideAsyncFunction);
            }

            foreach (Expr? staticFieldValue in classDecl.StaticFields.Values)
            {
                if (staticFieldValue is not null)
                    ValidateExpr(staticFieldValue, insideFunction, insideAsyncFunction);
            }

            foreach (FuncDeclStmt method in classDecl.Methods)
                ValidateFunctionDecl(method);

            foreach (FuncDeclStmt staticMethod in classDecl.StaticMethods)
                ValidateFunctionDecl(staticMethod);

            foreach (ClassDeclStmt nestedClass in classDecl.NestedClasses)
                ValidateClassDecl(nestedClass, insideFunction, insideAsyncFunction);
        }

        private void ValidateFunctionDecl(FuncDeclStmt funcDecl)
        {
            foreach (FunctionParameterSpec parameter in funcDecl.ParameterSpecs)
            {
                if (parameter.DestructurePattern is not null)
                    ValidatePattern(parameter.DestructurePattern, insideFunction: true, insideAsyncFunction: funcDecl.IsAsync);
                if (parameter.DefaultValue is not null)
                    ValidateExpr(parameter.DefaultValue, insideFunction: true, insideAsyncFunction: funcDecl.IsAsync);
            }

            ValidateStatement(funcDecl.Body, insideFunction: true, insideAsyncFunction: funcDecl.IsAsync);
        }

        private void ValidateExpr(Expr expr, bool insideFunction, bool insideAsyncFunction)
        {
            switch (expr)
            {
                case NullExpr:
                case NumberExpr:
                case StringExpr:
                case CharExpr:
                case BoolExpr:
                case VarExpr:
                    return;

                case BinaryExpr binaryExpr:
                    ValidateExpr(binaryExpr.Left, insideFunction, insideAsyncFunction);
                    ValidateExpr(binaryExpr.Right, insideFunction, insideAsyncFunction);
                    return;

                case UnaryExpr unaryExpr:
                    ValidateExpr(unaryExpr.Right, insideFunction, insideAsyncFunction);
                    return;

                case PrefixExpr prefixExpr:
                    ValidateReadTarget(prefixExpr.Target);
                    if (prefixExpr.Target is not null)
                        ValidateExpr(prefixExpr.Target, insideFunction, insideAsyncFunction);
                    return;

                case PostfixExpr postfixExpr:
                    ValidateReadTarget(postfixExpr.Target);
                    if (postfixExpr.Target is not null)
                        ValidateExpr(postfixExpr.Target, insideFunction, insideAsyncFunction);
                    return;

                case ArrayExpr arrayExpr:
                    foreach (Expr item in arrayExpr.Elements)
                        ValidateExpr(item, insideFunction, insideAsyncFunction);
                    return;

                case DictExpr dictExpr:
                    foreach ((Expr Key, Expr Value) pair in dictExpr.Pairs)
                    {
                        ValidateExpr(pair.Key, insideFunction, insideAsyncFunction);
                        ValidateExpr(pair.Value, insideFunction, insideAsyncFunction);
                    }
                    return;

                case IndexExpr indexExpr:
                    ValidateIndexExpression(indexExpr, insideFunction, insideAsyncFunction);
                    return;

                case SliceExpr sliceExpr:
                    if (sliceExpr.Target is not null)
                        ValidateExpr(sliceExpr.Target, insideFunction, insideAsyncFunction);
                    if (sliceExpr.Start is not null)
                        ValidateExpr(sliceExpr.Start, insideFunction, insideAsyncFunction);
                    if (sliceExpr.End is not null)
                        ValidateExpr(sliceExpr.End, insideFunction, insideAsyncFunction);
                    return;

                case TryUnwrapExpr tryUnwrapExpr:
                    if (tryUnwrapExpr.Inner is not null)
                        ValidateExpr(tryUnwrapExpr.Inner, insideFunction, insideAsyncFunction);
                    return;

                case MethodCallExpr methodCallExpr:
                    ValidateExpr(methodCallExpr.Target, insideFunction, insideAsyncFunction);
                    foreach (Expr arg in methodCallExpr.Args)
                        ValidateExpr(arg, insideFunction, insideAsyncFunction);
                    return;

                case NewExpr newExpr:
                    foreach (Expr arg in newExpr.Args)
                        ValidateExpr(arg, insideFunction, insideAsyncFunction);
                    foreach ((string _, Expr Value) init in newExpr.Initializers)
                        ValidateExpr(init.Value, insideFunction, insideAsyncFunction);
                    return;

                case GetFieldExpr getFieldExpr:
                    ValidateExpr(getFieldExpr.Target, insideFunction, insideAsyncFunction);
                    return;

                case OutExpr outExpr:
                    ValidateStatement(outExpr.Body, insideFunction, insideAsyncFunction);
                    return;

                case ConditionalExpr conditionalExpr:
                    ValidateExpr(conditionalExpr.Condition, insideFunction, insideAsyncFunction);
                    ValidateExpr(conditionalExpr.ThenExpr, insideFunction, insideAsyncFunction);
                    ValidateExpr(conditionalExpr.ElseExpr, insideFunction, insideAsyncFunction);
                    return;

                case MatchExpr matchExpr:
                    ValidateExpr(matchExpr.Scrutinee, insideFunction, insideAsyncFunction);
                    foreach (CaseExprArm arm in matchExpr.Arms)
                    {
                        ValidatePattern(arm.Pattern, insideFunction, insideAsyncFunction);
                        if (arm.Guard is not null)
                            ValidateExpr(arm.Guard, insideFunction, insideAsyncFunction);
                        ValidateExpr(arm.Body, insideFunction, insideAsyncFunction);
                    }

                    if (matchExpr.DefaultArm is not null)
                        ValidateExpr(matchExpr.DefaultArm, insideFunction, insideAsyncFunction);
                    return;

                case AwaitExpr awaitExpr:
                    ValidateExpr(awaitExpr.Inner, insideFunction, insideAsyncFunction);
                    return;

                case FuncExpr funcExpr:
                    foreach (FunctionParameterSpec parameter in funcExpr.ParameterSpecs)
                    {
                        if (parameter.DestructurePattern is not null)
                            ValidatePattern(parameter.DestructurePattern, insideFunction: true, insideAsyncFunction: funcExpr.IsAsync);
                        if (parameter.DefaultValue is not null)
                            ValidateExpr(parameter.DefaultValue, insideFunction: true, insideAsyncFunction: funcExpr.IsAsync);
                    }

                    ValidateStatement(funcExpr.Body, insideFunction: true, insideAsyncFunction: funcExpr.IsAsync);
                    return;

                case NamedArgExpr namedArgExpr:
                    ValidateExpr(namedArgExpr.Value, insideFunction, insideAsyncFunction);
                    return;

                case SpreadArgExpr spreadArgExpr:
                    ValidateExpr(spreadArgExpr.Value, insideFunction, insideAsyncFunction);
                    return;

                case CallExpr callExpr:
                    if (callExpr.Target is not null)
                        ValidateExpr(callExpr.Target, insideFunction, insideAsyncFunction);
                    foreach (Expr arg in callExpr.Args)
                        ValidateExpr(arg, insideFunction, insideAsyncFunction);
                    return;

                case ObjectInitExpr objectInitExpr:
                    ValidateExpr(objectInitExpr.Target, insideFunction, insideAsyncFunction);
                    foreach ((string _, Expr Value) init in objectInitExpr.Inits)
                        ValidateExpr(init.Value, insideFunction, insideAsyncFunction);
                    return;
            }
        }

        private void ValidatePattern(MatchPattern pattern, bool insideFunction, bool insideAsyncFunction)
        {
            switch (pattern)
            {
                case WildcardMatchPattern:
                case BindingMatchPattern:
                    return;

                case ValueMatchPattern valuePattern:
                    ValidateExpr(valuePattern.Value, insideFunction, insideAsyncFunction);
                    return;

                case ArrayMatchPattern arrayPattern:
                    foreach (MatchPattern element in arrayPattern.Elements)
                        ValidatePattern(element, insideFunction, insideAsyncFunction);
                    return;

                case DictMatchPattern dictPattern:
                    foreach ((string _, MatchPattern nestedPattern) in dictPattern.Entries)
                        ValidatePattern(nestedPattern, insideFunction, insideAsyncFunction);
                    return;
            }
        }

        private static void ValidateDeleteExpr(DeleteExprStmt deleteExpr)
        {
            if (deleteExpr.Target is SliceExpr or IndexExpr)
                return;

            if (deleteExpr.Target is VarExpr && deleteExpr.DeleteAll)
                return;

            throw new CompilerException(
                "unsupported delete target",
                deleteExpr.Line,
                deleteExpr.Col,
                deleteExpr.OriginFile);
        }

        private static void ValidateYield(YieldStmt yieldStmt, bool insideFunction, bool insideAsyncFunction)
        {
            if (!insideFunction)
            {
                throw new CompilerException(
                    "yield can only be used in function statements",
                    yieldStmt.Line,
                    yieldStmt.Col,
                    yieldStmt.OriginFile);
            }

            if (!insideAsyncFunction)
            {
                throw new CompilerException(
                    "yield can only be used in async function statements",
                    yieldStmt.Line,
                    yieldStmt.Col,
                    yieldStmt.OriginFile);
            }
        }

        private void ValidateIndexExpression(IndexExpr indexExpr, bool insideFunction, bool insideAsyncFunction)
        {
            if (indexExpr.Target is not null)
                ValidateExpr(indexExpr.Target, insideFunction, insideAsyncFunction);

            if (indexExpr.Index is not null)
                ValidateExpr(indexExpr.Index, insideFunction, insideAsyncFunction);
        }

        private static void ValidateReadTarget(Expr? target)
        {
            if (target is VarExpr)
                return;

            if (target is IndexExpr indexExpr)
            {
                if (indexExpr.Index is null)
                {
                    throw new CompilerException(
                        "empty index '[]' cannot be used for reading",
                        indexExpr.Line,
                        indexExpr.Col,
                        indexExpr.OriginFile);
                }

                return;
            }

            throw new CompilerException(
                "invalid lvalue expression",
                target?.Line ?? -1,
                target?.Col ?? -1,
                target?.OriginFile ?? string.Empty);
        }

        private static void ValidateStoreTarget(Expr? target)
        {
            if (target is VarExpr)
                return;

            if (target is IndexExpr indexExpr && indexExpr.Index is not null)
                return;

            throw new CompilerException(
                "Invalid lvalue for store.",
                target?.Line ?? -1,
                target?.Col ?? -1,
                target?.OriginFile ?? string.Empty);
        }
    }
}
