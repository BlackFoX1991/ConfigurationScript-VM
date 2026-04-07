using CFGS_VM.Analytic.Tree;
using CFGS_VM.Analytic.Tokens;

namespace CFGS_VM.Analytic.Lowering
{
    internal sealed class ParamLowerer
    {
        public List<Stmt> Lower(List<Stmt> statements)
            => LowerStatementList(statements);

        private List<Stmt> LowerStatementList(IEnumerable<Stmt> statements)
            => statements.Select(LowerStatement).ToList();

        private Stmt LowerStatement(Stmt stmt)
        {
            return stmt switch
            {
                EmptyStmt => stmt,
                VarDecl v => new VarDecl(v.Name, LowerNullableExpr(v.Value), v.Line, v.Col, v.OriginFile),
                ConstDecl c => new ConstDecl(c.Name, LowerExpr(c.Value), c.Line, c.Col, c.OriginFile),
                DestructureDeclStmt d => new DestructureDeclStmt(LowerPattern(d.Pattern), LowerExpr(d.Value), d.IsConst, d.Line, d.Col, d.OriginFile),
                DestructureAssignStmt d => new DestructureAssignStmt(LowerPattern(d.Pattern), LowerExpr(d.Value), d.Line, d.Col, d.OriginFile),
                ExportStmt e => new ExportStmt(e.Name, LowerStatement(e.Inner), e.Line, e.Col, e.OriginFile),
                AssignStmt a => new AssignStmt(a.Name, LowerExpr(a.Value), a.Line, a.Col, a.OriginFile),
                AssignIndexExprStmt a => new AssignIndexExprStmt((IndexExpr)LowerExpr(a.Target), LowerExpr(a.Value), a.Line, a.Col, a.OriginFile),
                AssignExprStmt a => new AssignExprStmt(LowerExpr(a.Target), LowerExpr(a.Value), a.Line, a.Col, a.OriginFile),
                CompoundAssignStmt a => new CompoundAssignStmt(LowerExpr(a.Target), a.Op, LowerExpr(a.Value), a.Line, a.Col, a.OriginFile),
                SliceSetStmt s => new SliceSetStmt((SliceExpr)LowerExpr(s.Slice), LowerExpr(s.Value), s.Line, s.Col, s.OriginFile),
                ExprStmt e => new ExprStmt(LowerExpr(e.Expression), e.Line, e.Col, e.OriginFile),
                PushStmt p => new PushStmt(LowerExpr(p.Target), LowerExpr(p.Value), p.Line, p.Col, p.OriginFile),
                DeleteVarStmt => stmt,
                DeleteIndexStmt d => new DeleteIndexStmt(d.Name, LowerExpr(d.Index), d.Line, d.Col, d.OriginFile),
                DeleteAllStmt d => new DeleteAllStmt(LowerExpr(d.Target), d.Line, d.Col, d.OriginFile),
                DeleteExprStmt d => new DeleteExprStmt(LowerExpr(d.Target), d.DeleteAll, d.Line, d.Col, d.OriginFile),
                SetFieldStmt s => new SetFieldStmt(LowerExpr(s.Target), s.Field, LowerExpr(s.Value), s.Line, s.Col, s.OriginFile),
                BlockStmt b => LowerBlock(b),
                IfStmt i => new IfStmt(LowerExpr(i.Condition), LowerBlock(i.ThenBlock), i.ElseBranch is null ? null : LowerStatement(i.ElseBranch), i.Line, i.Col, i.OriginFile),
                WhileStmt w => new WhileStmt(LowerExpr(w.Condition), LowerBlock(w.Body), w.Line, w.Col, w.OriginFile),
                DoWhileStmt d => new DoWhileStmt(LowerStatement(d.Body), LowerExpr(d.Condition), d.Line, d.Col, d.OriginFile),
                ForStmt f => new ForStmt(
                    f.Init is null ? null : LowerStatement(f.Init),
                    LowerNullableExpr(f.Condition),
                    f.Increment is null ? null : LowerStatement(f.Increment),
                    LowerBlock(f.Body),
                    f.Line,
                    f.Col,
                    f.OriginFile),
                ForeachStmt f => new ForeachStmt(
                    f.VarName,
                    f.TargetPattern is null ? null : LowerPattern(f.TargetPattern),
                    f.DeclareLocal,
                    LowerExpr(f.Iterable),
                    f.UseIndexValuePair,
                    LowerStatement(f.Body),
                    f.Line,
                    f.Col,
                    f.OriginFile),
                BreakStmt => stmt,
                ContinueStmt => stmt,
                MatchStmt m => new MatchStmt(
                    LowerExpr(m.Expression),
                    m.Cases.Select(LowerCaseClause).ToList(),
                    m.DefaultCase is null ? null : LowerBlock(m.DefaultCase),
                    m.Line,
                    m.Col,
                    m.OriginFile),
                TryStmt t => new TryStmt(
                    LowerBlock(t.TryBlock),
                    t.CatchIdent,
                    t.CatchBlock is null ? null : LowerBlock(t.CatchBlock),
                    t.FinallyBlock is null ? null : LowerBlock(t.FinallyBlock),
                    t.Line,
                    t.Col,
                    t.OriginFile),
                UsingStmt u => new UsingStmt(u.BindingName, u.BindingIsConst, LowerExpr(u.Resource), LowerBlock(u.Body), u.Line, u.Col, u.OriginFile),
                ThrowStmt t => new ThrowStmt(LowerExpr(t.Value), t.Line, t.Col, t.OriginFile),
                YieldStmt => stmt,
                ReturnStmt r => new ReturnStmt(LowerNullableExpr(r.Value), r.Line, r.Col, r.OriginFile),
                FuncDeclStmt f => LowerFunctionDecl(f),
                ClassDeclStmt c => LowerClassDecl(c),
                InterfaceDeclStmt => stmt,
                EnumDeclStmt => stmt,
                NamespaceDeclStmt n => new NamespaceDeclStmt(n.Parts, LowerStatementList(n.BodyStatements), n.Line, n.Col, n.OriginFile),
                NamespaceImportAliasStmt => stmt,
                ImportAliasDeclStmt => stmt,
                _ => stmt
            };
        }

        private FuncDeclStmt LowerFunctionDecl(FuncDeclStmt stmt)
        {
            BlockStmt loweredBody = LowerBlock(stmt.Body);
            List<Stmt> prefix = BuildParamInitializers(stmt.ParameterSpecs);
            if (prefix.Count > 0)
            {
                List<Stmt> combined = new(prefix.Count + loweredBody.Statements.Count);
                combined.AddRange(prefix);
                combined.AddRange(loweredBody.Statements);
                loweredBody = CopyBlock(loweredBody, combined);
            }

            return new FuncDeclStmt(
                stmt.Name,
                stmt.Parameters,
                loweredBody,
                stmt.MinArgs,
                stmt.RestParameter,
                stmt.Line,
                stmt.Col,
                stmt.OriginFile,
                stmt.IsAsync,
                parameterSpecs: new List<FunctionParameterSpec>());
        }

        private FuncExpr LowerFunctionExpr(FuncExpr expr)
        {
            BlockStmt loweredBody = LowerBlock(expr.Body);
            List<Stmt> prefix = BuildParamInitializers(expr.ParameterSpecs);
            if (prefix.Count > 0)
            {
                List<Stmt> combined = new(prefix.Count + loweredBody.Statements.Count);
                combined.AddRange(prefix);
                combined.AddRange(loweredBody.Statements);
                loweredBody = CopyBlock(loweredBody, combined);
            }

            return new FuncExpr(
                expr.Parameters,
                loweredBody,
                expr.MinArgs,
                expr.RestParameter,
                expr.Line,
                expr.Col,
                expr.OriginFile,
                expr.IsAsync,
                parameterSpecs: new List<FunctionParameterSpec>());
        }

        private ClassDeclStmt LowerClassDecl(ClassDeclStmt stmt)
        {
            Dictionary<string, Expr?> fields = stmt.Fields.ToDictionary(
                pair => pair.Key,
                pair => LowerNullableExpr(pair.Value),
                StringComparer.Ordinal);

            Dictionary<string, Expr?> staticFields = stmt.StaticFields.ToDictionary(
                pair => pair.Key,
                pair => LowerNullableExpr(pair.Value),
                StringComparer.Ordinal);

            return new ClassDeclStmt(
                stmt.Name,
                stmt.Methods.Select(LowerFunctionDecl).ToList(),
                stmt.Enums,
                fields,
                staticFields,
                stmt.StaticMethods.Select(LowerFunctionDecl).ToList(),
                stmt.Parameters,
                stmt.Line,
                stmt.Col,
                stmt.OriginFile,
                stmt.BaseName,
                stmt.BaseCtorArgs.Select(LowerExpr).ToList(),
                stmt.ImplementedInterfaces,
                stmt.NestedClasses.Select(LowerClassDecl).ToList(),
                stmt.IsNested,
                new Dictionary<string, MemberVisibility>(stmt.FieldVisibility, StringComparer.Ordinal),
                new Dictionary<string, MemberVisibility>(stmt.StaticFieldVisibility, StringComparer.Ordinal),
                new Dictionary<string, MemberVisibility>(stmt.MethodVisibility, StringComparer.Ordinal),
                new Dictionary<string, MemberVisibility>(stmt.StaticMethodVisibility, StringComparer.Ordinal),
                new Dictionary<string, MemberVisibility>(stmt.EnumVisibility, StringComparer.Ordinal),
                new Dictionary<string, MemberVisibility>(stmt.NestedClassVisibility, StringComparer.Ordinal),
                new HashSet<string>(stmt.ConstFields, StringComparer.Ordinal),
                new HashSet<string>(stmt.StaticConstFields, StringComparer.Ordinal));
        }

        private BlockStmt LowerBlock(BlockStmt block)
        {
            BlockStmt lowered = new(LowerStatementList(block.Statements), block.Line, block.Col, block.OriginFile)
            {
                IsFunctionBody = block.IsFunctionBody,
                IsNamespaceScope = block.IsNamespaceScope
            };

            return lowered;
        }

        private static BlockStmt CopyBlock(BlockStmt block, List<Stmt> statements)
        {
            return new BlockStmt(statements, block.Line, block.Col, block.OriginFile)
            {
                IsFunctionBody = block.IsFunctionBody,
                IsNamespaceScope = block.IsNamespaceScope
            };
        }

        private List<Stmt> BuildParamInitializers(IEnumerable<FunctionParameterSpec> parameterSpecs)
        {
            List<Stmt> statements = new();
            foreach (FunctionParameterSpec parameter in parameterSpecs)
            {
                if (parameter.IsRest)
                    continue;

                if (parameter.DefaultValue is not null)
                {
                    Expr condition = new BinaryExpr(
                        new VarExpr(parameter.Name, parameter.Line, parameter.Col, parameter.File),
                        TokenType.Eq,
                        new NullExpr(parameter.Line, parameter.Col, parameter.File),
                        parameter.Line,
                        parameter.Col,
                        parameter.File);

                    BlockStmt thenBlock = new(
                        new List<Stmt>
                        {
                            new AssignStmt(parameter.Name, LowerExpr(parameter.DefaultValue), parameter.Line, parameter.Col, parameter.File)
                        },
                        parameter.Line,
                        parameter.Col,
                        parameter.File);

                    statements.Add(new IfStmt(condition, thenBlock, null, parameter.Line, parameter.Col, parameter.File));
                }

                if (parameter.DestructurePattern is not null)
                {
                    statements.Add(new DestructureDeclStmt(
                        LowerPattern(parameter.DestructurePattern),
                        new VarExpr(parameter.Name, parameter.Line, parameter.Col, parameter.File),
                        isConst: false,
                        parameter.Line,
                        parameter.Col,
                        parameter.File));
                }
            }

            return statements;
        }

        private CaseClause LowerCaseClause(CaseClause clause)
            => new(LowerPattern(clause.Pattern), LowerNullableExpr(clause.Guard), LowerBlock(clause.Body), clause.Line, clause.Col, clause.OriginFile);

        private CaseExprArm LowerCaseExprArm(CaseExprArm arm)
            => new(LowerPattern(arm.Pattern), LowerNullableExpr(arm.Guard), LowerExpr(arm.Body), arm.Line, arm.Col, arm.OriginFile);

        private MatchPattern LowerPattern(MatchPattern pattern)
        {
            return pattern switch
            {
                WildcardMatchPattern => pattern,
                BindingMatchPattern => pattern,
                ValueMatchPattern v => new ValueMatchPattern(LowerExpr(v.Value), v.Line, v.Col, v.OriginFile),
                ArrayMatchPattern a => new ArrayMatchPattern(a.Elements.Select(LowerPattern).ToList(), a.Line, a.Col, a.OriginFile),
                DictMatchPattern d => new DictMatchPattern(d.Entries.Select(entry => (entry.Key, LowerPattern(entry.Pattern))).ToList(), d.Line, d.Col, d.OriginFile),
                _ => pattern
            };
        }

        private Expr? LowerNullableExpr(Expr? expr) => expr is null ? null : LowerExpr(expr);

        private Expr LowerExpr(Expr expr)
        {
            return expr switch
            {
                NullExpr => expr,
                NumberExpr => expr,
                StringExpr => expr,
                CharExpr => expr,
                BoolExpr => expr,
                VarExpr => expr,
                BinaryExpr b => new BinaryExpr(LowerExpr(b.Left), b.Op, LowerExpr(b.Right), b.Line, b.Col, b.OriginFile),
                UnaryExpr u => new UnaryExpr(u.Op, LowerExpr(u.Right), u.Line, u.Col, u.OriginFile),
                ArrayExpr a => new ArrayExpr(a.Elements.Select(LowerExpr).ToList(), a.Line, a.Col, a.OriginFile),
                IndexExpr i => new IndexExpr(LowerNullableExpr(i.Target), LowerNullableExpr(i.Index), i.Line, i.Col, i.OriginFile),
                SliceExpr s => new SliceExpr(LowerNullableExpr(s.Target), LowerNullableExpr(s.Start), LowerNullableExpr(s.End), s.Line, s.Col, s.OriginFile),
                DictExpr d => new DictExpr(d.Pairs.Select(pair => (LowerExpr(pair.Key), LowerExpr(pair.Value))).ToList(), d.Line, d.Col, d.OriginFile),
                PrefixExpr p => new PrefixExpr(LowerNullableExpr(p.Target), p.Op, p.Line, p.Col, p.OriginFile),
                PostfixExpr p => new PostfixExpr(LowerNullableExpr(p.Target), p.Op, p.Line, p.Col, p.OriginFile),
                TryUnwrapExpr t => new TryUnwrapExpr(LowerNullableExpr(t.Inner), t.Line, t.Col, t.OriginFile),
                MethodCallExpr m => new MethodCallExpr(LowerExpr(m.Target), m.Method, m.Args.Select(LowerExpr).ToList(), m.Line, m.Col, m.OriginFile),
                NewExpr n => new NewExpr(
                    n.ClassName,
                    n.Args.Select(LowerExpr).ToList(),
                    n.Initializers.Select(init => (init.Name, LowerExpr(init.Value))).ToList(),
                    n.Line,
                    n.Col,
                    n.OriginFile),
                GetFieldExpr g => new GetFieldExpr(LowerExpr(g.Target), g.Field, g.Line, g.Col, g.OriginFile),
                ObjectInitExpr o => new ObjectInitExpr(LowerExpr(o.Target), o.Inits.Select(init => (init.Name, LowerExpr(init.Value))).ToList(), o.Line, o.Col, o.OriginFile),
                OutExpr o => new OutExpr(LowerBlock(o.Body), o.Line, o.Col, o.OriginFile),
                ConditionalExpr c => new ConditionalExpr(LowerExpr(c.Condition), LowerExpr(c.ThenExpr), LowerExpr(c.ElseExpr), c.Line, c.Col, c.OriginFile),
                MatchExpr m => new MatchExpr(
                    LowerExpr(m.Scrutinee),
                    m.Arms.Select(LowerCaseExprArm).ToList(),
                    LowerNullableExpr(m.DefaultArm),
                    m.Line,
                    m.Col,
                    m.OriginFile),
                AwaitExpr a => new AwaitExpr(LowerExpr(a.Inner), a.Line, a.Col, a.OriginFile),
                FuncExpr f => LowerFunctionExpr(f),
                NamedArgExpr n => new NamedArgExpr(n.Name, LowerExpr(n.Value), n.Line, n.Col, n.OriginFile),
                SpreadArgExpr s => new SpreadArgExpr(LowerExpr(s.Value), s.Line, s.Col, s.OriginFile),
                CallExpr c => new CallExpr(LowerNullableExpr(c.Target), c.Args.Select(LowerExpr).ToList(), c.Line, c.Col, c.OriginFile),
                _ => expr
            };
        }
    }
}
