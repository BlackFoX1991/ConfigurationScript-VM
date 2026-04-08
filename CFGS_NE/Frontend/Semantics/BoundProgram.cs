using CFGS_VM.Analytic.Tree;

namespace CFGS_VM.Analytic.Semantics
{
    internal abstract class BoundNode(Node syntax)
    {
        public Node Syntax { get; } = syntax;
    }

    internal abstract class BoundStmt(Stmt syntax) : BoundNode(syntax)
    {
        public new Stmt Syntax { get; } = syntax;
    }

    internal abstract class BoundExpr(Expr syntax) : BoundNode(syntax)
    {
        public new Expr Syntax { get; } = syntax;
    }

    internal sealed class BoundSyntaxStmt(Stmt syntax) : BoundStmt(syntax);

    internal sealed class BoundSyntaxExpr(Expr syntax) : BoundExpr(syntax);

    internal sealed class BoundBlockStmt(BlockStmt block) : BoundStmt(block)
    {
        public BlockStmt Block { get; } = block;

        public IReadOnlyList<BoundStmt> Statements { get; } =
            block.Statements.Select(BindStatement).ToList();

        private static BoundStmt BindStatement(Stmt stmt)
            => stmt switch
            {
                BlockStmt nestedBlock => new BoundBlockStmt(nestedBlock),
                FuncDeclStmt funcDecl => new BoundFunction(funcDecl),
                ClassDeclStmt classDecl => new BoundClass(classDecl, classDecl.Name),
                InterfaceDeclStmt interfaceDecl => new BoundInterface(interfaceDecl, interfaceDecl.Name),
                _ => new BoundSyntaxStmt(stmt)
            };
    }

    internal sealed class BoundFunction(FuncDeclStmt declaration) : BoundStmt(declaration)
    {
        public FuncDeclStmt Declaration { get; } = declaration;

        public BoundBlockStmt Body { get; } = new(declaration.Body);
    }

    internal sealed class BoundInterface(InterfaceDeclStmt declaration, string qualifiedName) : BoundStmt(declaration)
    {
        public InterfaceDeclStmt Declaration { get; } = declaration;

        public string QualifiedName { get; } = qualifiedName;
    }

    internal sealed class BoundClass(ClassDeclStmt declaration, string qualifiedName) : BoundStmt(declaration)
    {
        public ClassDeclStmt Declaration { get; } = declaration;

        public string QualifiedName { get; } = qualifiedName;

        public IReadOnlyDictionary<string, BoundExpr?> FieldInitializers { get; } =
            BindExprMap(declaration.Fields);

        public IReadOnlyDictionary<string, BoundExpr?> StaticFieldInitializers { get; } =
            BindExprMap(declaration.StaticFields);

        private static IReadOnlyDictionary<string, BoundExpr?> BindExprMap(Dictionary<string, Expr?> source)
        {
            Dictionary<string, BoundExpr?> result = new(StringComparer.Ordinal);
            foreach ((string name, Expr? expr) in source)
                result[name] = expr is null ? null : new BoundSyntaxExpr(expr);
            return result;
        }
    }

    internal sealed class BoundProgram
    {
        private readonly Dictionary<FuncDeclStmt, BoundFunction> _functionsByDeclaration = new();
        private readonly Dictionary<InterfaceDeclStmt, BoundInterface> _interfacesByDeclaration = new();
        private readonly Dictionary<ClassDeclStmt, BoundClass> _classesByDeclaration = new();

        public List<BoundFunction> Functions { get; } = [];

        public List<BoundInterface> Interfaces { get; } = [];

        public List<BoundClass> Classes { get; } = [];

        public List<BoundStmt> TopLevelStatements { get; } = [];

        public Dictionary<string, BoundStmt> TopLevelSurface { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, BoundStmt> ExportSurface { get; } = new(StringComparer.Ordinal);

        public List<BoundInterface> OrderedInterfaces { get; set; } = [];

        public List<BoundClass> OrderedClasses { get; set; } = [];

        public void AddTopLevelStatement(BoundStmt stmt)
        {
            TopLevelStatements.Add(stmt);
            switch (stmt)
            {
                case BoundFunction function:
                    Functions.Add(function);
                    _functionsByDeclaration[function.Declaration] = function;
                    break;
                case BoundInterface iface:
                    Interfaces.Add(iface);
                    _interfacesByDeclaration[iface.Declaration] = iface;
                    break;
                case BoundClass cls:
                    Classes.Add(cls);
                    _classesByDeclaration[cls.Declaration] = cls;
                    break;
            }
        }

        public BoundInterface GetInterface(InterfaceDeclStmt declaration)
            => _interfacesByDeclaration[declaration];

        public BoundClass GetClass(ClassDeclStmt declaration)
            => _classesByDeclaration[declaration];
    }
}
