using CFGS_VM.Analytic.Tokens;

namespace CFGS_VM.Analytic.Tree
{
    /// <summary>
    /// Defines class member visibility.
    /// </summary>
    public enum MemberVisibility
    {
        Public,
        Private,
        Protected
    }

    /// <summary>
    /// Defines property accessor kinds.
    /// </summary>
    public enum PropertyAccessorKind
    {
        Get,
        Set,
        Init
    }

    /// <summary>
    /// Defines the <see cref="Node" />
    /// </summary>
    public abstract class Node(int line, int col, string originFile)
    {
        /// <summary>
        /// Gets the Line
        /// </summary>
        public int Line { get; } = line;

        /// <summary>
        /// Gets the Col
        /// </summary>
        public int Col { get; } = col;

        /// <summary>
        /// Gets the OriginFile
        /// </summary>
        public string OriginFile { get; } = originFile;
    }

    // +++ AST +++
    public sealed class ObjectInitExpr : Expr
    {
        public Expr Target { get; }
        public List<(string Name, Expr Value)> Inits { get; }

        public ObjectInitExpr(Expr target, List<(string, Expr)> inits, int line, int col, string file)
            : base(line, col, file)
        {
            Target = target;
            Inits = inits;
        }
    }


    /// <summary>
    /// Defines the <see cref="Expr" />
    /// </summary>
    public abstract class Expr(int line, int col, string fname) : Node(line, col, fname)
    {
    }

    /// <summary>
    /// Defines the <see cref="Stmt" />
    /// </summary>
    public abstract class Stmt(int line, int col, string fname) : Node(line, col, fname)
    {
    }

    /// <summary>
    /// Defines the <see cref="EmptyStmt" />
    /// </summary>
    public class EmptyStmt(TokenType tp, int line, int col, string originFile) : Stmt(line, col, originFile)
    {
        /// <summary>
        /// Defines the eType
        /// </summary>
        public TokenType eType = tp;
    }

    /// <summary>
    /// Defines the <see cref="NullExpr" />
    /// </summary>
    public class NullExpr(int line, int col, string fname) : Expr(line, col, fname)
    {
    }

    /// <summary>
    /// Defines the <see cref="NumberExpr" />
    /// </summary>
    public class NumberExpr(dynamic? v, int line, int col, string fname) : Expr(line, col, fname)
    {
        /// <summary>
        /// Defines the Value
        /// </summary>
        public dynamic? Value = v;
    }

    /// <summary>
    /// Defines the <see cref="StringExpr" />
    /// </summary>
    public class StringExpr(string v, int line, int col, string fname) : Expr(line, col, fname)
    {
        /// <summary>
        /// Defines the Value
        /// </summary>
        public string Value = v;
    }

    /// <summary>
    /// Defines the <see cref="CharExpr" />
    /// </summary>
    public class CharExpr(char v, int line, int col, string fname) : Expr(line, col, fname)
    {
        /// <summary>
        /// Defines the Value
        /// </summary>
        public char Value = v;
    }

    /// <summary>
    /// Defines the <see cref="BoolExpr" />
    /// </summary>
    public class BoolExpr : Expr
    {
        /// <summary>
        /// Defines the Value
        /// </summary>
        public bool Value;

        /// <summary>
        /// Initializes a new instance of the <see cref="BoolExpr"/> class.
        /// </summary>
        /// <param name="v">The v<see cref="bool"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public BoolExpr(bool v, int line, int col, string fname) : base(line, col, fname) => Value = v;
    }

    /// <summary>
    /// Defines the <see cref="VarExpr" />
    /// </summary>
    public class VarExpr(string n, int line, int col, string fname) : Expr(line, col, fname)
    {
        /// <summary>
        /// Defines the Name
        /// </summary>
        public string Name = n;
    }

    /// <summary>
    /// Defines the <see cref="BinaryExpr" />
    /// </summary>
    public class BinaryExpr : Expr
    {
        /// <summary>
        /// Defines the Left
        /// </summary>
        public Expr Left;

        /// <summary>
        /// Defines the Op
        /// </summary>
        public TokenType Op;

        /// <summary>
        /// Defines the Right
        /// </summary>
        public Expr Right;

        /// <summary>
        /// Initializes a new instance of the <see cref="BinaryExpr"/> class.
        /// </summary>
        /// <param name="l">The l<see cref="Expr"/></param>
        /// <param name="op">The op<see cref="TokenType"/></param>
        /// <param name="r">The r<see cref="Expr"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public BinaryExpr(Expr l, TokenType op, Expr r, int line, int col, string fname) : base(line, col, fname)
        {
            Left = l; Op = op; Right = r;
        }
    }

    /// <summary>
    /// Defines the <see cref="UnaryExpr" />
    /// </summary>
    public class UnaryExpr : Expr
    {
        /// <summary>
        /// Gets the Op
        /// </summary>
        public TokenType Op { get; }

        /// <summary>
        /// Gets the Right
        /// </summary>
        public Expr Right { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="UnaryExpr"/> class.
        /// </summary>
        /// <param name="op">The op<see cref="TokenType"/></param>
        /// <param name="right">The right<see cref="Expr"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public UnaryExpr(TokenType op, Expr right, int line, int col, string fname) : base(line, col, fname)
        {
            Op = op;
            Right = right;
        }
    }

    /// <summary>
    /// Defines the <see cref="CompoundAssignStmt" />
    /// </summary>
    public class CompoundAssignStmt : Stmt
    {
        /// <summary>
        /// Gets the Target
        /// </summary>
        public Expr Target { get; }

        /// <summary>
        /// Gets the Op
        /// </summary>
        public TokenType Op { get; }

        /// <summary>
        /// Gets the Value
        /// </summary>
        public Expr Value { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="CompoundAssignStmt"/> class.
        /// </summary>
        /// <param name="target">The target<see cref="Expr"/></param>
        /// <param name="op">The op<see cref="TokenType"/></param>
        /// <param name="value">The value<see cref="Expr"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public CompoundAssignStmt(Expr target, TokenType op, Expr value, int line, int col, string fname) : base(line, col, fname)
        {
            Target = target;
            Op = op;
            Value = value;
        }
    }

    /// <summary>
    /// Defines the <see cref="ArrayExpr" />
    /// </summary>
    public class ArrayExpr : Expr
    {
        /// <summary>
        /// Defines the Elements
        /// </summary>
        public List<Expr> Elements;

        /// <summary>
        /// Initializes a new instance of the <see cref="ArrayExpr"/> class.
        /// </summary>
        /// <param name="elems">The elems<see cref="List{Expr}"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public ArrayExpr(List<Expr> elems, int line, int col, string fname) : base(line, col, fname)
        {
            Elements = elems;
        }
    }

    /// <summary>
    /// Defines the <see cref="IndexExpr" />
    /// </summary>
    public class IndexExpr : Expr
    {
        /// <summary>
        /// Defines the Target
        /// </summary>
        public Expr? Target;

        /// <summary>
        /// Defines the Index
        /// </summary>
        public Expr? Index;

        /// <summary>
        /// Gets whether this access was written with dot member syntax.
        /// </summary>
        public bool IsDotAccess { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="IndexExpr"/> class.
        /// </summary>
        /// <param name="t">The t<see cref="Expr?"/></param>
        /// <param name="i">The i<see cref="Expr?"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public IndexExpr(Expr? t, Expr? i, int line, int col, string fname, bool isDotAccess = false) : base(line, col, fname)
        {
            Target = t;
            Index = i;
            IsDotAccess = isDotAccess;
        }
    }

    /// <summary>
    /// Defines the <see cref="SliceExpr" />
    /// </summary>
    public class SliceExpr : Expr
    {
        /// <summary>
        /// Defines the Target
        /// </summary>
        public Expr? Target;

        /// <summary>
        /// Defines the Start
        /// </summary>
        public Expr? Start;

        /// <summary>
        /// Defines the End
        /// </summary>
        public Expr? End;

        /// <summary>
        /// Initializes a new instance of the <see cref="SliceExpr"/> class.
        /// </summary>
        /// <param name="t">The t<see cref="Expr?"/></param>
        /// <param name="s">The s<see cref="Expr?"/></param>
        /// <param name="e">The e<see cref="Expr?"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public SliceExpr(Expr? t, Expr? s, Expr? e, int line, int col, string fname) : base(line, col, fname)
        {
            Target = t;
            Start = s;
            End = e;
        }
    }

    /// <summary>
    /// Defines the <see cref="SliceSetStmt" />
    /// </summary>
    public class SliceSetStmt : Stmt
    {
        /// <summary>
        /// Gets the Slice
        /// </summary>
        public SliceExpr Slice { get; }

        /// <summary>
        /// Gets the Value
        /// </summary>
        public Expr Value { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SliceSetStmt"/> class.
        /// </summary>
        /// <param name="slice">The slice<see cref="SliceExpr"/></param>
        /// <param name="value">The value<see cref="Expr"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public SliceSetStmt(SliceExpr slice, Expr value, int line, int col, string fname) : base(line, col, fname)
        {
            Slice = slice;
            Value = value;
        }
    }

    /// <summary>
    /// Defines the <see cref="DictExpr" />
    /// </summary>
    public class DictExpr : Expr
    {
        /// <summary>
        /// Defines the Pairs
        /// </summary>
        public List<(Expr Key, Expr Value)> Pairs;

        /// <summary>
        /// Initializes a new instance of the <see cref="DictExpr"/> class.
        /// </summary>
        /// <param name="pairs">The pairs<see cref="List{(Expr, Expr)}"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public DictExpr(List<(Expr, Expr)> pairs, int line, int col, string fname) : base(line, col, fname)
        {
            Pairs = pairs;
        }
    }

    /// <summary>
    /// Defines the <see cref="ExprStmt" />
    /// </summary>
    public class ExprStmt : Stmt
    {
        /// <summary>
        /// Gets the Expression
        /// </summary>
        public Expr Expression { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ExprStmt"/> class.
        /// </summary>
        /// <param name="expr">The expr<see cref="Expr"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public ExprStmt(Expr expr, int line, int col, string fname) : base(line, col, fname)
        {
            Expression = expr;
        }
    }

    /// <summary>
    /// Defines the <see cref="PrefixExpr" />
    /// </summary>
    public class PrefixExpr : Expr
    {
        /// <summary>
        /// Gets the Target
        /// </summary>
        public Expr? Target { get; }

        /// <summary>
        /// Gets the Op
        /// </summary>
        public TokenType Op { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="PrefixExpr"/> class.
        /// </summary>
        /// <param name="target">The target<see cref="Expr?"/></param>
        /// <param name="op">The op<see cref="TokenType"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public PrefixExpr(Expr? target, TokenType op, int line, int col, string fname) : base(line, col, fname)
        {
            Target = target;
            Op = op;
        }
    }

    /// <summary>
    /// Defines the <see cref="PostfixExpr" />
    /// </summary>
    public class PostfixExpr : Expr
    {
        /// <summary>
        /// Gets the Target
        /// </summary>
        public Expr? Target { get; }

        /// <summary>
        /// Gets the Op
        /// </summary>
        public TokenType Op { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="PostfixExpr"/> class.
        /// </summary>
        /// <param name="target">The target<see cref="Expr?"/></param>
        /// <param name="op">The op<see cref="TokenType"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public PostfixExpr(Expr? target, TokenType op, int line, int col, string fname) : base(line, col, fname)
        {
            Target = target;
            Op = op;
        }
    }

    /// <summary>
    /// Defines the <see cref="VarDecl" />
    /// </summary>
    public class VarDecl : Stmt
    {
        /// <summary>
        /// Defines the Name
        /// </summary>
        public string Name;

        /// <summary>
        /// Defines the Value
        /// </summary>
        public Expr? Value;

        /// <summary>
        /// Gets or sets a value indicating whether this declaration is a synthetic namespace root created during lowering.
        /// </summary>
        public bool IsSyntheticNamespaceRoot { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="VarDecl"/> class.
        /// </summary>
        /// <param name="n">The n<see cref="string"/></param>
        /// <param name="v">The v<see cref="Expr?"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public VarDecl(string n, Expr? v, int line, int col, string fname) : base(line, col, fname)
        {
            Name = n; Value = v;
        }
    }

    /// <summary>
    /// Defines the <see cref="ConstDecl" />
    /// </summary>
    public class ConstDecl : Stmt
    {
        /// <summary>
        /// Defines the Name
        /// </summary>
        public string Name;

        /// <summary>
        /// Defines the Value
        /// </summary>
        public Expr Value;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConstDecl"/> class.
        /// </summary>
        /// <param name="n">The n<see cref="string"/></param>
        /// <param name="v">The v<see cref="Expr"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public ConstDecl(string n, Expr v, int line, int col, string fname) : base(line, col, fname)
        {
            Name = n;
            Value = v;
        }
    }

    /// <summary>
    /// Defines the <see cref="DestructureDeclStmt" />
    /// </summary>
    public sealed class DestructureDeclStmt : Stmt
    {
        /// <summary>
        /// Gets the Pattern
        /// </summary>
        public MatchPattern Pattern { get; }

        /// <summary>
        /// Gets the Value
        /// </summary>
        public Expr Value { get; }

        /// <summary>
        /// Gets a value indicating whether IsConst
        /// </summary>
        public bool IsConst { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DestructureDeclStmt"/> class.
        /// </summary>
        /// <param name="pattern">The pattern<see cref="MatchPattern"/></param>
        /// <param name="value">The value<see cref="Expr"/></param>
        /// <param name="isConst">The isConst<see cref="bool"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="file">The file<see cref="string"/></param>
        public DestructureDeclStmt(MatchPattern pattern, Expr value, bool isConst, int line, int col, string file) : base(line, col, file)
        {
            Pattern = pattern;
            Value = value;
            IsConst = isConst;
        }
    }

    /// <summary>
    /// Defines the <see cref="DestructureAssignStmt" />
    /// </summary>
    public sealed class DestructureAssignStmt : Stmt
    {
        /// <summary>
        /// Gets the Pattern
        /// </summary>
        public MatchPattern Pattern { get; }

        /// <summary>
        /// Gets the Value
        /// </summary>
        public Expr Value { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DestructureAssignStmt"/> class.
        /// </summary>
        /// <param name="pattern">The pattern<see cref="MatchPattern"/></param>
        /// <param name="value">The value<see cref="Expr"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="file">The file<see cref="string"/></param>
        public DestructureAssignStmt(MatchPattern pattern, Expr value, int line, int col, string file) : base(line, col, file)
        {
            Pattern = pattern;
            Value = value;
        }
    }

    /// <summary>
    /// Defines the <see cref="ExportStmt" />
    /// </summary>
    public sealed class ExportStmt : Stmt
    {
        /// <summary>
        /// Gets the Name
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the Inner
        /// </summary>
        public Stmt Inner { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ExportStmt"/> class.
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="inner">The inner<see cref="Stmt"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="file">The file<see cref="string"/></param>
        public ExportStmt(string name, Stmt inner, int line, int col, string file) : base(line, col, file)
        {
            Name = name;
            Inner = inner;
        }
    }

    /// <summary>
    /// Defines the <see cref="AssignStmt" />
    /// </summary>
    public class AssignStmt : Stmt
    {
        /// <summary>
        /// Defines the Name
        /// </summary>
        public string Name;

        /// <summary>
        /// Defines the Value
        /// </summary>
        public Expr Value;

        /// <summary>
        /// Initializes a new instance of the <see cref="AssignStmt"/> class.
        /// </summary>
        /// <param name="n">The n<see cref="string"/></param>
        /// <param name="v">The v<see cref="Expr"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public AssignStmt(string n, Expr v, int line, int col, string fname) : base(line, col, fname)
        {
            Name = n; Value = v;
        }
    }

    /// <summary>
    /// Defines the <see cref="AssignIndexExprStmt" />
    /// </summary>
    public class AssignIndexExprStmt : Stmt
    {
        /// <summary>
        /// Gets the Target
        /// </summary>
        public IndexExpr Target { get; }

        /// <summary>
        /// Gets the Value
        /// </summary>
        public Expr Value { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="AssignIndexExprStmt"/> class.
        /// </summary>
        /// <param name="target">The target<see cref="IndexExpr"/></param>
        /// <param name="value">The value<see cref="Expr"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public AssignIndexExprStmt(IndexExpr target, Expr value, int line, int col, string fname)
            : base(line, col, fname)
        {
            Target = target;
            Value = value;
        }
    }

    /// <summary>
    /// Defines the <see cref="AssignExprStmt" />
    /// </summary>
    public class AssignExprStmt : Stmt
    {
        /// <summary>
        /// Gets the Target
        /// </summary>
        public Expr Target { get; }

        /// <summary>
        /// Gets the Value
        /// </summary>
        public Expr Value { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="AssignExprStmt"/> class.
        /// </summary>
        /// <param name="target">The target<see cref="Expr"/></param>
        /// <param name="value">The value<see cref="Expr"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public AssignExprStmt(Expr target, Expr value, int line, int col, string fname)
            : base(line, col, fname)
        {
            Target = target;
            Value = value;
        }
    }

    /// <summary>
    /// Defines the <see cref="TryUnwrapExpr" />
    /// </summary>
    public sealed class TryUnwrapExpr(Expr? inner, int line, int col, string file) : Expr(line, col, file)
    {
        /// <summary>
        /// Gets the Inner
        /// </summary>
        public Expr? Inner { get; } = inner;
    }

    /// <summary>
    /// Defines the <see cref="PushStmt" />
    /// </summary>
    public class PushStmt : Stmt
    {
        /// <summary>
        /// Gets the Target
        /// </summary>
        public Expr Target { get; }

        /// <summary>
        /// Gets the Value
        /// </summary>
        public Expr Value { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="PushStmt"/> class.
        /// </summary>
        /// <param name="target">The target<see cref="Expr"/></param>
        /// <param name="value">The value<see cref="Expr"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public PushStmt(Expr target, Expr value, int line, int col, string fname) : base(line, col, fname)
        {
            Target = target;
            Value = value;
        }
    }

    /// <summary>
    /// Defines the <see cref="DeleteVarStmt" />
    /// </summary>
    public class DeleteVarStmt : Stmt
    {
        /// <summary>
        /// Defines the Name
        /// </summary>
        public string Name;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeleteVarStmt"/> class.
        /// </summary>
        /// <param name="n">The n<see cref="string"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public DeleteVarStmt(string n, int line, int col, string fname) : base(line, col, fname)
        {
            Name = n;
        }
    }

    /// <summary>
    /// Defines the <see cref="DeleteIndexStmt" />
    /// </summary>
    public class DeleteIndexStmt : Stmt
    {
        /// <summary>
        /// Defines the Name
        /// </summary>
        public string Name;

        /// <summary>
        /// Defines the Index
        /// </summary>
        public Expr Index;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeleteIndexStmt"/> class.
        /// </summary>
        /// <param name="n">The n<see cref="string"/></param>
        /// <param name="idx">The idx<see cref="Expr"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public DeleteIndexStmt(string n, Expr idx, int line, int col, string fname) : base(line, col, fname)
        {
            Name = n; Index = idx;
        }
    }

    /// <summary>
    /// Defines the <see cref="DeleteAllStmt" />
    /// </summary>
    public class DeleteAllStmt : Stmt
    {
        /// <summary>
        /// Gets the Target
        /// </summary>
        public Expr Target { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DeleteAllStmt"/> class.
        /// </summary>
        /// <param name="target">The target<see cref="Expr"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public DeleteAllStmt(Expr target, int line, int col, string fname) : base(line, col, fname)
        {
            Target = target;
        }
    }

    /// <summary>
    /// Defines the <see cref="DeleteExprStmt" />
    /// </summary>
    public class DeleteExprStmt : Stmt
    {
        /// <summary>
        /// Gets the Target
        /// </summary>
        public Expr Target { get; }

        /// <summary>
        /// Gets a value indicating whether DeleteAll
        /// </summary>
        public bool DeleteAll { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DeleteExprStmt"/> class.
        /// </summary>
        /// <param name="target">The target<see cref="Expr"/></param>
        /// <param name="deleteAll">The deleteAll<see cref="bool"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public DeleteExprStmt(Expr target, bool deleteAll, int line, int col, string fname) : base(line, col, fname)
        {
            Target = target;
            DeleteAll = deleteAll;
        }
    }

    /// <summary>
    /// Defines the <see cref="MethodCallExpr" />
    /// </summary>
    public class MethodCallExpr : Expr
    {
        /// <summary>
        /// Gets the Target
        /// </summary>
        public Expr Target { get; }

        /// <summary>
        /// Gets the Method
        /// </summary>
        public string Method { get; }

        /// <summary>
        /// Gets the Args
        /// </summary>
        public List<Expr> Args { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="MethodCallExpr"/> class.
        /// </summary>
        /// <param name="target">The target<see cref="Expr"/></param>
        /// <param name="method">The method<see cref="string"/></param>
        /// <param name="args">The args<see cref="List{Expr}"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public MethodCallExpr(Expr target, string method, List<Expr> args, int line, int col, string fname)
            : base(line, col, fname)
        {
            Target = target;
            Method = method;
            Args = args;
        }
    }

    /// <summary>
    /// Defines the <see cref="ClassDeclStmt" />
    /// </summary>
    public class ClassDeclStmt : Stmt
    {
        /// <summary>
        /// Defines the Name
        /// </summary>
        public string Name;

        /// <summary>
        /// Defines the Methods
        /// </summary>
        public List<FuncDeclStmt> Methods;

        /// <summary>
        /// Defines the Properties
        /// </summary>
        public List<PropertyDeclStmt> Properties;

        /// <summary>
        /// Defines the StaticProperties
        /// </summary>
        public List<PropertyDeclStmt> StaticProperties;

        /// <summary>
        /// Defines the Enums
        /// </summary>
        public List<EnumDeclStmt> Enums;

        /// <summary>
        /// Defines the Fields
        /// </summary>
        public Dictionary<string, Expr?> Fields;

        /// <summary>
        /// Defines the StaticFields
        /// </summary>
        public Dictionary<string, Expr?> StaticFields;

        /// <summary>
        /// Defines the StaticMethods
        /// </summary>
        public List<FuncDeclStmt> StaticMethods;

        /// <summary>
        /// Defines the FieldVisibility
        /// </summary>
        public Dictionary<string, MemberVisibility> FieldVisibility;

        /// <summary>
        /// Defines the StaticFieldVisibility
        /// </summary>
        public Dictionary<string, MemberVisibility> StaticFieldVisibility;

        /// <summary>
        /// Defines the MethodVisibility
        /// </summary>
        public Dictionary<string, MemberVisibility> MethodVisibility;

        /// <summary>
        /// Defines the StaticMethodVisibility
        /// </summary>
        public Dictionary<string, MemberVisibility> StaticMethodVisibility;

        /// <summary>
        /// Defines the EnumVisibility
        /// </summary>
        public Dictionary<string, MemberVisibility> EnumVisibility;

        /// <summary>
        /// Defines the NestedClassVisibility
        /// </summary>
        public Dictionary<string, MemberVisibility> NestedClassVisibility;

        /// <summary>
        /// Defines the ConstFields
        /// </summary>
        public HashSet<string> ConstFields;

        /// <summary>
        /// Defines the StaticConstFields
        /// </summary>
        public HashSet<string> StaticConstFields;

        /// <summary>
        /// Defines the BaseName
        /// </summary>
        public string? BaseName;

        /// <summary>
        /// Defines the BaseCtorArgs
        /// </summary>
        public List<Expr> BaseCtorArgs = new();

        /// <summary>
        /// Defines the ImplementedInterfaces
        /// </summary>
        public List<string> ImplementedInterfaces;

        /// <summary>
        /// Defines the Parameters
        /// </summary>
        public List<string> Parameters;

        /// <summary>
        /// Gets the NestedClasses
        /// </summary>
        public List<ClassDeclStmt> NestedClasses { get; } = new();

        /// <summary>
        /// Gets a value indicating whether IsNested
        /// </summary>
        public bool IsNested { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ClassDeclStmt"/> class.
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="methods">The methods<see cref="List{FuncDeclStmt}"/></param>
        /// <param name="properties">The properties<see cref="List{PropertyDeclStmt}"/></param>
        /// <param name="enums">The enums<see cref="List{EnumDeclStmt}"/></param>
        /// <param name="fields">The fields<see cref="Dictionary{string, Expr?}"/></param>
        /// <param name="staticFields">The staticFields<see cref="Dictionary{string, Expr?}"/></param>
        /// <param name="staticMethods">The staticMethods<see cref="List{FuncDeclStmt}"/></param>
        /// <param name="staticProperties">The staticProperties<see cref="List{PropertyDeclStmt}"/></param>
        /// <param name="parameters">The parameters<see cref="List{string}"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        /// <param name="baseName">The baseName<see cref="string?"/></param>
        /// <param name="baseArgs">The baseArgs<see cref="List{Expr}?"/></param>
        /// <param name="implementedInterfaces">The implementedInterfaces<see cref="List{string}?"/></param>
        /// <param name="nestedClasses">The nestedClasses<see cref="List{ClassDeclStmt}?"/></param>
        /// <param name="isNested">The isNested<see cref="bool"/></param>
        public ClassDeclStmt(
            string name,
            List<FuncDeclStmt> methods,
            List<PropertyDeclStmt> properties,
            List<EnumDeclStmt> enums,
            Dictionary<string, Expr?> fields,
            Dictionary<string, Expr?> staticFields,
            List<FuncDeclStmt> staticMethods,
            List<PropertyDeclStmt> staticProperties,
            List<string> parameters,
            int line,
            int col,
            string fname,
            string? baseName = null,
            List<Expr>? baseArgs = null,
            List<string>? implementedInterfaces = null,
            List<ClassDeclStmt>? nestedClasses = null,
            bool isNested = false,
            Dictionary<string, MemberVisibility>? fieldVisibility = null,
            Dictionary<string, MemberVisibility>? staticFieldVisibility = null,
            Dictionary<string, MemberVisibility>? methodVisibility = null,
            Dictionary<string, MemberVisibility>? staticMethodVisibility = null,
            Dictionary<string, MemberVisibility>? enumVisibility = null,
            Dictionary<string, MemberVisibility>? nestedClassVisibility = null,
            HashSet<string>? constFields = null,
            HashSet<string>? staticConstFields = null
        ) : base(line, col, fname)
        {
            Name = name;
            Methods = methods;
            Properties = properties;
            Enums = enums;
            Fields = fields;
            Parameters = parameters;
            StaticFields = staticFields;
            StaticMethods = staticMethods;
            StaticProperties = staticProperties;
            BaseName = baseName;
            if (baseArgs != null) BaseCtorArgs = baseArgs;
            ImplementedInterfaces = implementedInterfaces ?? new List<string>();
            NestedClasses = nestedClasses ?? new List<ClassDeclStmt>();
            IsNested = isNested;
            FieldVisibility = fieldVisibility ?? new Dictionary<string, MemberVisibility>(StringComparer.Ordinal);
            StaticFieldVisibility = staticFieldVisibility ?? new Dictionary<string, MemberVisibility>(StringComparer.Ordinal);
            MethodVisibility = methodVisibility ?? new Dictionary<string, MemberVisibility>(StringComparer.Ordinal);
            StaticMethodVisibility = staticMethodVisibility ?? new Dictionary<string, MemberVisibility>(StringComparer.Ordinal);
            EnumVisibility = enumVisibility ?? new Dictionary<string, MemberVisibility>(StringComparer.Ordinal);
            NestedClassVisibility = nestedClassVisibility ?? new Dictionary<string, MemberVisibility>(StringComparer.Ordinal);
            ConstFields = constFields ?? new HashSet<string>(StringComparer.Ordinal);
            StaticConstFields = staticConstFields ?? new HashSet<string>(StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Defines the <see cref="PropertyAccessorDecl" />
    /// </summary>
    public sealed class PropertyAccessorDecl : Node
    {
        /// <summary>
        /// Gets the Kind.
        /// </summary>
        public PropertyAccessorKind Kind { get; }

        /// <summary>
        /// Gets the Visibility.
        /// </summary>
        public MemberVisibility Visibility { get; }

        /// <summary>
        /// Gets a value indicating whether the accessor has an explicit visibility modifier.
        /// </summary>
        public bool HasExplicitVisibility { get; }

        /// <summary>
        /// Gets the ValueParameterName.
        /// </summary>
        public string? ValueParameterName { get; }

        /// <summary>
        /// Gets the Body.
        /// </summary>
        public BlockStmt? Body { get; }

        /// <summary>
        /// Gets a value indicating whether the accessor is auto-implemented.
        /// </summary>
        public bool IsAuto { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="PropertyAccessorDecl"/> class.
        /// </summary>
        public PropertyAccessorDecl(
            PropertyAccessorKind kind,
            MemberVisibility visibility,
            bool hasExplicitVisibility,
            string? valueParameterName,
            BlockStmt? body,
            bool isAuto,
            int line,
            int col,
            string file)
            : base(line, col, file)
        {
            Kind = kind;
            Visibility = visibility;
            HasExplicitVisibility = hasExplicitVisibility;
            ValueParameterName = valueParameterName;
            Body = body;
            IsAuto = isAuto;
        }
    }

    /// <summary>
    /// Defines the <see cref="PropertyDeclStmt" />
    /// </summary>
    public sealed class PropertyDeclStmt : Node
    {
        /// <summary>
        /// Gets the Name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the Visibility.
        /// </summary>
        public MemberVisibility Visibility { get; }

        /// <summary>
        /// Gets a value indicating whether the property is static.
        /// </summary>
        public bool IsStatic { get; }

        /// <summary>
        /// Gets the Accessors.
        /// </summary>
        public List<PropertyAccessorDecl> Accessors { get; }

        /// <summary>
        /// Gets the Initializer.
        /// </summary>
        public Expr? Initializer { get; }

        /// <summary>
        /// Gets a value indicating whether the property uses hidden backing storage.
        /// </summary>
        public bool HasAutoStorage { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="PropertyDeclStmt"/> class.
        /// </summary>
        public PropertyDeclStmt(
            string name,
            MemberVisibility visibility,
            bool isStatic,
            List<PropertyAccessorDecl> accessors,
            Expr? initializer,
            bool hasAutoStorage,
            int line,
            int col,
            string file)
            : base(line, col, file)
        {
            Name = name;
            Visibility = visibility;
            IsStatic = isStatic;
            Accessors = accessors;
            Initializer = initializer;
            HasAutoStorage = hasAutoStorage;
        }
    }

    /// <summary>
    /// Defines a raw namespace declaration before lowering into synthetic scope statements.
    /// </summary>
    public sealed class NamespaceDeclStmt : Stmt
    {
        /// <summary>
        /// Gets the namespace parts.
        /// </summary>
        public List<string> Parts { get; }

        /// <summary>
        /// Gets the raw namespace body statements.
        /// </summary>
        public List<Stmt> BodyStatements { get; }

        /// <summary>
        /// Gets a value indicating whether this namespace used file-scoped syntax.
        /// </summary>
        public bool IsFileScoped { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="NamespaceDeclStmt"/> class.
        /// </summary>
        /// <param name="parts">The namespace parts.</param>
        /// <param name="bodyStatements">The raw body statements.</param>
        /// <param name="line">The line.</param>
        /// <param name="col">The column.</param>
        /// <param name="file">The source file.</param>
        public NamespaceDeclStmt(List<string> parts, List<Stmt> bodyStatements, int line, int col, string file, bool isFileScoped = false)
            : base(line, col, file)
        {
            Parts = parts;
            BodyStatements = bodyStatements;
            IsFileScoped = isFileScoped;
        }
    }

    /// <summary>
    /// Defines a syntax-only bare import before module resolution.
    /// </summary>
    public sealed class BareImportSyntaxStmt : Stmt
    {
        /// <summary>
        /// Gets the raw import path.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="BareImportSyntaxStmt"/> class.
        /// </summary>
        /// <param name="path">The raw import path.</param>
        /// <param name="line">The line.</param>
        /// <param name="col">The column.</param>
        /// <param name="file">The source file.</param>
        public BareImportSyntaxStmt(string path, int line, int col, string file)
            : base(line, col, file)
        {
            Path = path;
        }
    }

    /// <summary>
    /// Defines a syntax-only namespace import before module resolution.
    /// </summary>
    public sealed class NamespaceImportSyntaxStmt : Stmt
    {
        /// <summary>
        /// Gets the local alias.
        /// </summary>
        public string Alias { get; }

        /// <summary>
        /// Gets the raw import path.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="NamespaceImportSyntaxStmt"/> class.
        /// </summary>
        /// <param name="alias">The local alias.</param>
        /// <param name="path">The raw import path.</param>
        /// <param name="line">The line.</param>
        /// <param name="col">The column.</param>
        /// <param name="file">The source file.</param>
        public NamespaceImportSyntaxStmt(string alias, string path, int line, int col, string file)
            : base(line, col, file)
        {
            Alias = alias;
            Path = path;
        }
    }

    /// <summary>
    /// Defines one named import binding before module resolution.
    /// </summary>
    public sealed class ImportBindingSpec
    {
        /// <summary>
        /// Gets the exported source name.
        /// </summary>
        public string ImportName { get; }

        /// <summary>
        /// Gets the local target name.
        /// </summary>
        public string LocalName { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ImportBindingSpec"/> class.
        /// </summary>
        /// <param name="importName">The exported source name.</param>
        /// <param name="localName">The local target name.</param>
        public ImportBindingSpec(string importName, string localName)
        {
            ImportName = importName;
            LocalName = localName;
        }
    }

    /// <summary>
    /// Defines a syntax-only named import before module resolution.
    /// </summary>
    public sealed class NamedImportSyntaxStmt : Stmt
    {
        /// <summary>
        /// Gets the bindings requested from the target module.
        /// </summary>
        public List<ImportBindingSpec> Imports { get; }

        /// <summary>
        /// Gets the raw import path.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="NamedImportSyntaxStmt"/> class.
        /// </summary>
        /// <param name="imports">The requested bindings.</param>
        /// <param name="path">The raw import path.</param>
        /// <param name="line">The line.</param>
        /// <param name="col">The column.</param>
        /// <param name="file">The source file.</param>
        public NamedImportSyntaxStmt(List<ImportBindingSpec> imports, string path, int line, int col, string file)
            : base(line, col, file)
        {
            Imports = imports;
            Path = path;
        }
    }

    /// <summary>
    /// Defines a syntax-only default import before module resolution.
    /// </summary>
    public sealed class DefaultImportSyntaxStmt : Stmt
    {
        /// <summary>
        /// Gets the requested export name.
        /// </summary>
        public string ImportName { get; }

        /// <summary>
        /// Gets the raw import path.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DefaultImportSyntaxStmt"/> class.
        /// </summary>
        /// <param name="importName">The requested export name.</param>
        /// <param name="path">The raw import path.</param>
        /// <param name="line">The line.</param>
        /// <param name="col">The column.</param>
        /// <param name="file">The source file.</param>
        public DefaultImportSyntaxStmt(string importName, string path, int line, int col, string file)
            : base(line, col, file)
        {
            ImportName = importName;
            Path = path;
        }
    }

    /// <summary>
    /// Defines a syntax-only namespace-lookup import before use resolution.
    /// </summary>
    public sealed class UseNamespaceStmt : Stmt
    {
        /// <summary>
        /// Gets the namespace parts.
        /// </summary>
        public List<string> Parts { get; }

        /// <summary>
        /// Gets the fully qualified namespace path.
        /// </summary>
        public string QualifiedPath { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="UseNamespaceStmt"/> class.
        /// </summary>
        /// <param name="parts">The namespace parts.</param>
        /// <param name="line">The line.</param>
        /// <param name="col">The column.</param>
        /// <param name="file">The source file.</param>
        public UseNamespaceStmt(List<string> parts, int line, int col, string file)
            : base(line, col, file)
        {
            Parts = parts;
            QualifiedPath = string.Join(".", parts);
        }
    }

    /// <summary>
    /// Defines a raw namespace import alias before lowering.
    /// </summary>
    public sealed class NamespaceImportAliasStmt : Stmt
    {
        /// <summary>
        /// Gets the alias name.
        /// </summary>
        public string Alias { get; }

        /// <summary>
        /// Gets the imported names exposed under the alias.
        /// </summary>
        public List<string> ImportedNames { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="NamespaceImportAliasStmt"/> class.
        /// </summary>
        /// <param name="alias">The alias name.</param>
        /// <param name="importedNames">The imported names.</param>
        /// <param name="line">The line.</param>
        /// <param name="col">The column.</param>
        /// <param name="file">The source file.</param>
        public NamespaceImportAliasStmt(string alias, List<string> importedNames, int line, int col, string file)
            : base(line, col, file)
        {
            Alias = alias;
            ImportedNames = importedNames;
        }
    }

    /// <summary>
    /// Defines a raw named-import alias before lowering.
    /// </summary>
    public sealed class ImportAliasDeclStmt : Stmt
    {
        /// <summary>
        /// Gets the local alias name.
        /// </summary>
        public string LocalName { get; }

        /// <summary>
        /// Gets the imported source name.
        /// </summary>
        public string ImportName { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ImportAliasDeclStmt"/> class.
        /// </summary>
        /// <param name="localName">The local alias name.</param>
        /// <param name="importName">The imported source name.</param>
        /// <param name="line">The line.</param>
        /// <param name="col">The column.</param>
        /// <param name="file">The source file.</param>
        public ImportAliasDeclStmt(string localName, string importName, int line, int col, string file)
            : base(line, col, file)
        {
            LocalName = localName;
            ImportName = importName;
        }
    }

    /// <summary>
    /// Defines the <see cref="InterfaceMethodDecl" />
    /// </summary>
    public sealed class InterfaceMethodDecl : Node
    {
        /// <summary>
        /// Gets the Name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the Parameters.
        /// </summary>
        public List<string> Parameters { get; }

        /// <summary>
        /// Gets the MinArgs.
        /// </summary>
        public int MinArgs { get; }

        /// <summary>
        /// Gets the RestParameter.
        /// </summary>
        public string? RestParameter { get; }

        /// <summary>
        /// Gets a value indicating whether the signature is async.
        /// </summary>
        public bool IsAsync { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="InterfaceMethodDecl"/> class.
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="parameters">The parameters<see cref="List{string}"/></param>
        /// <param name="minArgs">The minArgs<see cref="int"/></param>
        /// <param name="restParameter">The restParameter<see cref="string?"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="file">The file<see cref="string"/></param>
        /// <param name="isAsync">The isAsync<see cref="bool"/></param>
        public InterfaceMethodDecl(string name, List<string> parameters, int minArgs, string? restParameter, int line, int col, string file, bool isAsync = false)
            : base(line, col, file)
        {
            Name = name;
            Parameters = parameters;
            MinArgs = minArgs;
            RestParameter = restParameter;
            IsAsync = isAsync;
        }
    }

    /// <summary>
    /// Defines the <see cref="InterfaceDeclStmt" />
    /// </summary>
    public sealed class InterfaceDeclStmt : Stmt
    {
        /// <summary>
        /// Gets the Name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the Methods.
        /// </summary>
        public List<InterfaceMethodDecl> Methods { get; }

        /// <summary>
        /// Gets the Properties.
        /// </summary>
        public List<InterfacePropertyDecl> Properties { get; }

        /// <summary>
        /// Gets the BaseInterfaces.
        /// </summary>
        public List<string> BaseInterfaces { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="InterfaceDeclStmt"/> class.
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="methods">The methods<see cref="List{InterfaceMethodDecl}"/></param>
        /// <param name="properties">The properties<see cref="List{InterfacePropertyDecl}"/></param>
        /// <param name="baseInterfaces">The baseInterfaces<see cref="List{string}?"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="file">The file<see cref="string"/></param>
        public InterfaceDeclStmt(string name, List<InterfaceMethodDecl> methods, List<InterfacePropertyDecl> properties, List<string>? baseInterfaces, int line, int col, string file)
            : base(line, col, file)
        {
            Name = name;
            Methods = methods;
            Properties = properties;
            BaseInterfaces = baseInterfaces ?? new List<string>();
        }
    }

    /// <summary>
    /// Defines the <see cref="InterfacePropertyDecl" />
    /// </summary>
    public sealed class InterfacePropertyDecl : Node
    {
        /// <summary>
        /// Gets the Name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets a value indicating whether the property has a getter.
        /// </summary>
        public bool HasGetter { get; }

        /// <summary>
        /// Gets a value indicating whether the property has a setter.
        /// </summary>
        public bool HasSetter { get; }

        /// <summary>
        /// Gets a value indicating whether the property has an init accessor.
        /// </summary>
        public bool HasInit { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="InterfacePropertyDecl"/> class.
        /// </summary>
        public InterfacePropertyDecl(string name, bool hasGetter, bool hasSetter, bool hasInit, int line, int col, string file)
            : base(line, col, file)
        {
            Name = name;
            HasGetter = hasGetter;
            HasSetter = hasSetter;
            HasInit = hasInit;
        }
    }

    /// <summary>
    /// Defines the <see cref="EnumMemberNode" />
    /// </summary>
    public class EnumMemberNode : Node
    {
        /// <summary>
        /// Gets the Name
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the Value
        /// </summary>
        public dynamic? Value { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="EnumMemberNode"/> class.
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="value">The value<see cref="dynamic?"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="origin">The origin<see cref="string"/></param>
        public EnumMemberNode(string name, dynamic? value, int line, int col, string origin)
            : base(line, col, origin)
        {
            Name = name;
            Value = value;
        }
    }

    /// <summary>
    /// Defines the <see cref="EnumDeclStmt" />
    /// </summary>
    public class EnumDeclStmt : Stmt
    {
        /// <summary>
        /// Gets the Name
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the Members
        /// </summary>
        public List<EnumMemberNode> Members { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="EnumDeclStmt"/> class.
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="members">The members<see cref="List{EnumMemberNode}"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="origin">The origin<see cref="string"/></param>
        public EnumDeclStmt(string name, List<EnumMemberNode> members, int line, int col, string origin)
            : base(line, col, origin)
        {
            Name = name;
            Members = members;
        }
    }

    /// <summary>
    /// Defines the <see cref="NewExpr" />
    /// </summary>
    // in deiner AST-Definition
    public sealed class NewExpr : Expr
    {
        public string ClassName { get; }
        public List<Expr> Args { get; }
        public List<(string Name, Expr Value)> Initializers { get; }   // NEU

        public NewExpr(string className, List<Expr> args, List<(string, Expr)> inits,
                       int line, int col, string origin)
            : base(line, col, origin)
        {
            ClassName = className;
            Args = args;
            Initializers = inits ?? new();
        }
    }


    /// <summary>
    /// Defines the <see cref="GetFieldExpr" />
    /// </summary>
    public class GetFieldExpr : Expr
    {
        /// <summary>
        /// Gets the Target
        /// </summary>
        public Expr Target { get; }

        /// <summary>
        /// Gets the Field
        /// </summary>
        public string Field { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="GetFieldExpr"/> class.
        /// </summary>
        /// <param name="target">The target<see cref="Expr"/></param>
        /// <param name="field">The field<see cref="string"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public GetFieldExpr(Expr target, string field, int line, int col, string fname)
            : base(line, col, fname)
        {
            Target = target;
            Field = field;
        }
    }

    /// <summary>
    /// Defines the <see cref="SetFieldStmt" />
    /// </summary>
    public class SetFieldStmt : Stmt
    {
        /// <summary>
        /// Gets the Target
        /// </summary>
        public Expr Target { get; }

        /// <summary>
        /// Gets the Field
        /// </summary>
        public string Field { get; }

        /// <summary>
        /// Gets the Value
        /// </summary>
        public Expr Value { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SetFieldStmt"/> class.
        /// </summary>
        /// <param name="target">The target<see cref="Expr"/></param>
        /// <param name="field">The field<see cref="string"/></param>
        /// <param name="value">The value<see cref="Expr"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public SetFieldStmt(Expr target, string field, Expr value, int line, int col, string fname)
            : base(line, col, fname)
        {
            Target = target;
            Field = field;
            Value = value;
        }
    }

    /// <summary>
    /// Defines the <see cref="BlockStmt" />
    /// </summary>
    public class BlockStmt : Stmt
    {
        /// <summary>
        /// Defines the Statements
        /// </summary>
        public List<Stmt> Statements;

        /// <summary>
        /// Gets or sets a value indicating whether IsFunctionBody
        /// </summary>
        public bool IsFunctionBody { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether this block is the synthetic
        /// execution scope created for a namespace declaration.
        /// </summary>
        public bool IsNamespaceScope { get; set; } = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="BlockStmt"/> class.
        /// </summary>
        /// <param name="s">The s<see cref="List{Stmt}"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public BlockStmt(List<Stmt> s, int line, int col, string fname) : base(line, col, fname)
        {
            Statements = s;
        }
    }

    /// <summary>
    /// Defines the <see cref="OutExpr" />
    /// </summary>
    public sealed class OutExpr : Expr
    {
        /// <summary>
        /// Gets the Body
        /// </summary>
        public BlockStmt Body { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="OutExpr"/> class.
        /// </summary>
        /// <param name="body">The body<see cref="BlockStmt"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="file">The file<see cref="string"/></param>
        public OutExpr(BlockStmt body, int line, int col, string file)
            : base(line, col, file) => Body = body;
    }

    /// <summary>
    /// Defines the <see cref="ConditionalExpr" />
    /// </summary>
    public class ConditionalExpr : Expr
    {
        /// <summary>
        /// Gets the Condition
        /// </summary>
        public Expr Condition { get; }

        /// <summary>
        /// Gets the ThenExpr
        /// </summary>
        public Expr ThenExpr { get; }

        /// <summary>
        /// Gets the ElseExpr
        /// </summary>
        public Expr ElseExpr { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConditionalExpr"/> class.
        /// </summary>
        /// <param name="cond">The cond<see cref="Expr"/></param>
        /// <param name="thenExpr">The thenExpr<see cref="Expr"/></param>
        /// <param name="elseExpr">The elseExpr<see cref="Expr"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public ConditionalExpr(Expr cond, Expr thenExpr, Expr elseExpr, int line, int col, string fname)
            : base(line, col, fname)
        {
            Condition = cond;
            ThenExpr = thenExpr;
            ElseExpr = elseExpr;
        }
    }

    /// <summary>
    /// Defines the <see cref="IfStmt" />
    /// </summary>
    public class IfStmt : Stmt
    {
        /// <summary>
        /// Defines the Condition
        /// </summary>
        public Expr Condition;

        /// <summary>
        /// Defines the ThenBlock
        /// </summary>
        public BlockStmt ThenBlock;

        /// <summary>
        /// Defines the ElseBranch
        /// </summary>
        public Stmt? ElseBranch;

        /// <summary>
        /// Initializes a new instance of the <see cref="IfStmt"/> class.
        /// </summary>
        /// <param name="cond">The cond<see cref="Expr"/></param>
        /// <param name="t">The t<see cref="BlockStmt"/></param>
        /// <param name="e">The e<see cref="Stmt?"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public IfStmt(Expr cond, BlockStmt t, Stmt? e, int line, int col, string fname) : base(line, col, fname)
        {
            Condition = cond;
            ThenBlock = t;
            ElseBranch = e;
        }
    }

    /// <summary>
    /// Defines the <see cref="WhileStmt" />
    /// </summary>
    public class WhileStmt : Stmt
    {
        /// <summary>
        /// Gets the Condition
        /// </summary>
        public Expr Condition { get; }

        /// <summary>
        /// Gets the Body
        /// </summary>
        public BlockStmt Body { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="WhileStmt"/> class.
        /// </summary>
        /// <param name="cond">The cond<see cref="Expr"/></param>
        /// <param name="body">The body<see cref="BlockStmt"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public WhileStmt(Expr cond, BlockStmt body, int line, int col, string fname) : base(line, col, fname)
        {
            Condition = cond;
            Body = body;
        }
    }

    public sealed class DoWhileStmt : Stmt
    {
        public Stmt Body { get; }
        public Expr Condition { get; }

        public DoWhileStmt(Stmt body, Expr condition, int line, int col, string originFile)
            : base(line, col, originFile)
        {
            Body = body;
            Condition = condition;
        }
    }


    /// <summary>
    /// Defines the <see cref="ForStmt" />
    /// </summary>
    public class ForStmt : Stmt
    {
        /// <summary>
        /// Gets the Init
        /// </summary>
        public Stmt? Init { get; }

        /// <summary>
        /// Gets the Condition
        /// </summary>
        public Expr? Condition { get; }

        /// <summary>
        /// Gets the Increment
        /// </summary>
        public Stmt? Increment { get; }

        /// <summary>
        /// Gets the Body
        /// </summary>
        public BlockStmt Body { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ForStmt"/> class.
        /// </summary>
        /// <param name="init">The init<see cref="Stmt?"/></param>
        /// <param name="cond">The cond<see cref="Expr?"/></param>
        /// <param name="inc">The inc<see cref="Stmt?"/></param>
        /// <param name="body">The body<see cref="BlockStmt"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public ForStmt(Stmt? init, Expr? cond, Stmt? inc, BlockStmt body, int line, int col, string fname) : base(line, col, fname)
        {
            Init = init;
            Condition = cond;
            Increment = inc;
            Body = body;
        }
    }

    /// <summary>
    /// Defines the <see cref="ForeachStmt" />
    /// </summary>
    public sealed class ForeachStmt : Stmt
    {
        /// <summary>
        /// Gets the VarName
        /// </summary>
        public string VarName { get; }

        /// <summary>
        /// Gets the TargetPattern
        /// </summary>
        public MatchPattern? TargetPattern { get; }

        /// <summary>
        /// Gets a value indicating whether DeclareLocal
        /// </summary>
        public bool DeclareLocal { get; }

        /// <summary>
        /// Gets the Iterable
        /// </summary>
        public Expr Iterable { get; }

        /// <summary>
        /// Gets a value indicating whether UseIndexValuePair
        /// </summary>
        public bool UseIndexValuePair { get; }

        /// <summary>
        /// Gets the Body
        /// </summary>
        public Stmt Body { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ForeachStmt"/> class.
        /// </summary>
        /// <param name="varName">The varName<see cref="string"/></param>
        /// <param name="targetPattern">The targetPattern<see cref="MatchPattern?"/></param>
        /// <param name="declareLocal">The declareLocal<see cref="bool"/></param>
        /// <param name="iterable">The iterable<see cref="Expr"/></param>
        /// <param name="useIndexValuePair">The useIndexValuePair<see cref="bool"/></param>
        /// <param name="body">The body<see cref="Stmt"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="origin">The origin<see cref="string"/></param>
        public ForeachStmt(string varName, MatchPattern? targetPattern, bool declareLocal, Expr iterable, bool useIndexValuePair, Stmt body, int line, int col, string origin)
            : base(line, col, origin)
        {
            VarName = varName;
            TargetPattern = targetPattern;
            DeclareLocal = declareLocal;
            Iterable = iterable;
            UseIndexValuePair = useIndexValuePair;
            Body = body;
        }
    }

    /// <summary>
    /// Defines the <see cref="BreakStmt" />
    /// </summary>
    public class BreakStmt : Stmt
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BreakStmt"/> class.
        /// </summary>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public BreakStmt(int line, int col, string fname) : base(line, col, fname)
        {
        }
    }

    /// <summary>
    /// Defines the <see cref="ContinueStmt" />
    /// </summary>
    public class ContinueStmt : Stmt
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ContinueStmt"/> class.
        /// </summary>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public ContinueStmt(int line, int col, string fname) : base(line, col, fname)
        {
        }
    }

    /// <summary>
    /// Defines the <see cref="MatchStmt" />
    /// </summary>
    public class MatchStmt : Stmt
    {
        /// <summary>
        /// Defines the Expression
        /// </summary>
        public Expr Expression;

        /// <summary>
        /// Defines the Cases
        /// </summary>
        public List<CaseClause> Cases;

        /// <summary>
        /// Defines the DefaultCase
        /// </summary>
        public BlockStmt? DefaultCase;

        /// <summary>
        /// Initializes a new instance of the <see cref="MatchStmt"/> class.
        /// </summary>
        /// <param name="expr">The expr<see cref="Expr"/></param>
        /// <param name="cases">The cases<see cref="List{CaseClause}"/></param>
        /// <param name="def">The def<see cref="BlockStmt?"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public MatchStmt(Expr expr, List<CaseClause> cases, BlockStmt? def, int line, int col, string fname) : base(line, col, fname)
        {
            Expression = expr;
            Cases = cases;
            DefaultCase = def;
        }
    }

    /// <summary>
    /// Defines the <see cref="MatchPattern" />
    /// </summary>
    public abstract class MatchPattern(int line, int col, string file) : Node(line, col, file)
    {
    }

    /// <summary>
    /// Defines the <see cref="WildcardMatchPattern" />
    /// </summary>
    public sealed class WildcardMatchPattern(int line, int col, string file) : MatchPattern(line, col, file)
    {
    }

    /// <summary>
    /// Defines the <see cref="BindingMatchPattern" />
    /// </summary>
    public sealed class BindingMatchPattern : MatchPattern
    {
        /// <summary>
        /// Gets the Name
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="BindingMatchPattern"/> class.
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="file">The file<see cref="string"/></param>
        public BindingMatchPattern(string name, int line, int col, string file) : base(line, col, file)
        {
            Name = name;
        }
    }

    /// <summary>
    /// Defines the <see cref="ValueMatchPattern" />
    /// </summary>
    public sealed class ValueMatchPattern : MatchPattern
    {
        /// <summary>
        /// Gets the Value
        /// </summary>
        public Expr Value { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ValueMatchPattern"/> class.
        /// </summary>
        /// <param name="value">The value<see cref="Expr"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="file">The file<see cref="string"/></param>
        public ValueMatchPattern(Expr value, int line, int col, string file) : base(line, col, file)
        {
            Value = value;
        }
    }

    /// <summary>
    /// Defines the <see cref="ArrayMatchPattern" />
    /// </summary>
    public sealed class ArrayMatchPattern : MatchPattern
    {
        /// <summary>
        /// Gets the Elements
        /// </summary>
        public List<MatchPattern> Elements { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ArrayMatchPattern"/> class.
        /// </summary>
        /// <param name="elements">The elements<see cref="List{MatchPattern}"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="file">The file<see cref="string"/></param>
        public ArrayMatchPattern(List<MatchPattern> elements, int line, int col, string file) : base(line, col, file)
        {
            Elements = elements;
        }
    }

    /// <summary>
    /// Defines the <see cref="DictMatchPattern" />
    /// </summary>
    public sealed class DictMatchPattern : MatchPattern
    {
        /// <summary>
        /// Gets the Entries
        /// </summary>
        public List<(string Key, MatchPattern Pattern)> Entries { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DictMatchPattern"/> class.
        /// </summary>
        /// <param name="entries">The entries<see cref="List{(string Key, MatchPattern Pattern)}"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="file">The file<see cref="string"/></param>
        public DictMatchPattern(List<(string Key, MatchPattern Pattern)> entries, int line, int col, string file) : base(line, col, file)
        {
            Entries = entries;
        }
    }

    /// <summary>
    /// Defines the <see cref="CaseExprArm" />
    /// </summary>
    public sealed class CaseExprArm : Node
    {
        /// <summary>
        /// Gets the Pattern
        /// </summary>
        public MatchPattern Pattern { get; }

        /// <summary>
        /// Gets the Guard
        /// </summary>
        public Expr? Guard { get; }

        /// <summary>
        /// Gets the Body
        /// </summary>
        public Expr Body { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="CaseExprArm"/> class.
        /// </summary>
        /// <param name="p">The p<see cref="MatchPattern"/></param>
        /// <param name="g">The g<see cref="Expr?"/></param>
        /// <param name="b">The b<see cref="Expr"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="file">The file<see cref="string"/></param>
        public CaseExprArm(MatchPattern p, Expr? g, Expr b, int line, int col, string file) : base(line, col, file)
        {
            Pattern = p;
            Guard = g;
            Body = b;
        }
    }

    /// <summary>
    /// Defines the <see cref="MatchExpr" />
    /// </summary>
    public sealed class MatchExpr : Expr
    {
        /// <summary>
        /// Gets the Scrutinee
        /// </summary>
        public Expr Scrutinee { get; }

        /// <summary>
        /// Gets the Arms
        /// </summary>
        public List<CaseExprArm> Arms { get; }

        /// <summary>
        /// Gets the DefaultArm
        /// </summary>
        public Expr? DefaultArm { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="MatchExpr"/> class.
        /// </summary>
        /// <param name="scr">The scr<see cref="Expr"/></param>
        /// <param name="arms">The arms<see cref="List{CaseExprArm}"/></param>
        /// <param name="def">The def<see cref="Expr?"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="file">The file<see cref="string"/></param>
        public MatchExpr(Expr scr, List<CaseExprArm> arms, Expr? def, int line, int col, string file)
            : base(line, col, file)
        {
            Scrutinee = scr; Arms = arms; DefaultArm = def;
        }
    }

    /// <summary>
    /// Defines the <see cref="AwaitExpr" />
    /// </summary>
    public sealed class AwaitExpr : Expr
    {
        /// <summary>
        /// Gets the Inner
        /// </summary>
        public Expr Inner { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="AwaitExpr"/> class.
        /// </summary>
        /// <param name="inner">The inner<see cref="Expr"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="file">The file<see cref="string"/></param>
        public AwaitExpr(Expr inner, int line, int col, string file)
            : base(line, col, file)
        {
            Inner = inner;
        }
    }

    /// <summary>
    /// Defines the <see cref="TryStmt" />
    /// </summary>
    public class TryStmt : Stmt
    {
        /// <summary>
        /// Defines the TryBlock
        /// </summary>
        public BlockStmt TryBlock;

        /// <summary>
        /// Defines the CatchIdent
        /// </summary>
        public string? CatchIdent;

        /// <summary>
        /// Defines the CatchBlock
        /// </summary>
        public BlockStmt? CatchBlock;

        /// <summary>
        /// Defines the FinallyBlock
        /// </summary>
        public BlockStmt? FinallyBlock;

        /// <summary>
        /// Initializes a new instance of the <see cref="TryStmt"/> class.
        /// </summary>
        /// <param name="tryBlock">The tryBlock<see cref="BlockStmt"/></param>
        /// <param name="catchIdent">The catchIdent<see cref="string?"/></param>
        /// <param name="catchBlock">The catchBlock<see cref="BlockStmt?"/></param>
        /// <param name="finallyBlock">The finallyBlock<see cref="BlockStmt?"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="file">The file<see cref="string"/></param>
        public TryStmt(BlockStmt tryBlock, string? catchIdent, BlockStmt? catchBlock, BlockStmt? finallyBlock, int line, int col, string file)
            : base(line, col, file)
        {
            TryBlock = tryBlock;
            CatchIdent = catchIdent;
            CatchBlock = catchBlock;
            FinallyBlock = finallyBlock;
        }
    }

    /// <summary>
    /// Defines the <see cref="UsingStmt" />
    /// </summary>
    public sealed class UsingStmt : Stmt
    {
        /// <summary>
        /// Gets the BindingName.
        /// </summary>
        public string? BindingName { get; }

        /// <summary>
        /// Gets a value indicating whether the binding is const.
        /// </summary>
        public bool BindingIsConst { get; }

        /// <summary>
        /// Gets the Resource.
        /// </summary>
        public Expr Resource { get; }

        /// <summary>
        /// Gets the Body.
        /// </summary>
        public BlockStmt Body { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="UsingStmt"/> class.
        /// </summary>
        /// <param name="bindingName">The bindingName<see cref="string?"/></param>
        /// <param name="bindingIsConst">The bindingIsConst<see cref="bool"/></param>
        /// <param name="resource">The resource<see cref="Expr"/></param>
        /// <param name="body">The body<see cref="BlockStmt"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="file">The file<see cref="string"/></param>
        public UsingStmt(string? bindingName, bool bindingIsConst, Expr resource, BlockStmt body, int line, int col, string file)
            : base(line, col, file)
        {
            BindingName = bindingName;
            BindingIsConst = bindingIsConst;
            Resource = resource;
            Body = body;
        }
    }

    /// <summary>
    /// Defines the <see cref="ThrowStmt" />
    /// </summary>
    public class ThrowStmt : Stmt
    {
        /// <summary>
        /// Defines the Value
        /// </summary>
        public Expr Value;

        /// <summary>
        /// Initializes a new instance of the <see cref="ThrowStmt"/> class.
        /// </summary>
        /// <param name="value">The value<see cref="Expr"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="file">The file<see cref="string"/></param>
        public ThrowStmt(Expr value, int line, int col, string file)
            : base(line, col, file)
        {
            Value = value;
        }
    }

    /// <summary>
    /// Defines the <see cref="YieldStmt" />
    /// </summary>
    public sealed class YieldStmt : Stmt
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="YieldStmt"/> class.
        /// </summary>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="file">The file<see cref="string"/></param>
        public YieldStmt(int line, int col, string file)
            : base(line, col, file)
        {
        }
    }

    /// <summary>
    /// Describes the raw parameter form of a function before lowering.
    /// </summary>
    public sealed class FunctionParameterSpec
    {
        /// <summary>
        /// Gets the lowered parameter name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the optional destructuring pattern.
        /// </summary>
        public MatchPattern? DestructurePattern { get; }

        /// <summary>
        /// Gets the optional default value expression.
        /// </summary>
        public Expr? DefaultValue { get; }

        /// <summary>
        /// Gets a value indicating whether this parameter is the rest parameter.
        /// </summary>
        public bool IsRest { get; }

        /// <summary>
        /// Gets the line.
        /// </summary>
        public int Line { get; }

        /// <summary>
        /// Gets the column.
        /// </summary>
        public int Col { get; }

        /// <summary>
        /// Gets the source file.
        /// </summary>
        public string File { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="FunctionParameterSpec"/> class.
        /// </summary>
        /// <param name="name">The lowered parameter name.</param>
        /// <param name="destructurePattern">The optional destructuring pattern.</param>
        /// <param name="defaultValue">The optional default value.</param>
        /// <param name="isRest">Whether this is the rest parameter.</param>
        /// <param name="line">The line.</param>
        /// <param name="col">The column.</param>
        /// <param name="file">The source file.</param>
        public FunctionParameterSpec(
            string name,
            MatchPattern? destructurePattern,
            Expr? defaultValue,
            bool isRest,
            int line,
            int col,
            string file)
        {
            Name = name;
            DestructurePattern = destructurePattern;
            DefaultValue = defaultValue;
            IsRest = isRest;
            Line = line;
            Col = col;
            File = file;
        }
    }

    /// <summary>
    /// Defines the <see cref="CaseClause" />
    /// </summary>
    public class CaseClause : Node
    {
        /// <summary>
        /// Defines the Pattern
        /// </summary>
        public MatchPattern Pattern;

        /// <summary>
        /// Defines the Guard
        /// </summary>
        public Expr? Guard;

        /// <summary>
        /// Defines the Body
        /// </summary>
        public BlockStmt Body;

        /// <summary>
        /// Initializes a new instance of the <see cref="CaseClause"/> class.
        /// </summary>
        /// <param name="pattern">The pattern<see cref="MatchPattern"/></param>
        /// <param name="guard">The guard<see cref="Expr?"/></param>
        /// <param name="body">The body<see cref="BlockStmt"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public CaseClause(MatchPattern pattern, Expr? guard, BlockStmt body, int line, int col, string fname) : base(line, col, fname)
        {
            Pattern = pattern;
            Guard = guard;
            Body = body;
        }
    }

    /// <summary>
    /// Defines the <see cref="FuncDeclStmt" />
    /// </summary>
    public class FuncDeclStmt : Stmt
    {
        /// <summary>
        /// Defines the Name
        /// </summary>
        public string Name;

        /// <summary>
        /// Defines the Parameters
        /// </summary>
        public List<string> Parameters;

        /// <summary>
        /// Defines the MinArgs
        /// </summary>
        public int MinArgs;

        /// <summary>
        /// Defines the RestParameter
        /// </summary>
        public string? RestParameter;

        /// <summary>
        /// Defines whether the function was declared with async.
        /// </summary>
        public bool IsAsync;

        /// <summary>
        /// Defines the Body
        /// </summary>
        public BlockStmt Body;

        /// <summary>
        /// Gets or sets the raw parameter specifications used for lowering.
        /// </summary>
        public List<FunctionParameterSpec> ParameterSpecs { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="FuncDeclStmt"/> class.
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="parameters">The parameters<see cref="List{string}"/></param>
        /// <param name="body">The body<see cref="BlockStmt"/></param>
        /// <param name="minArgs">The minArgs<see cref="int"/></param>
        /// <param name="restParameter">The restParameter<see cref="string?"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        /// <param name="isAsync">The isAsync<see cref="bool"/></param>
        public FuncDeclStmt(
            string name,
            List<string> parameters,
            BlockStmt body,
            int minArgs,
            string? restParameter,
            int line,
            int col,
            string fname,
            bool isAsync = false,
            List<FunctionParameterSpec>? parameterSpecs = null) : base(line, col, fname)
        {
            Name = name;
            Parameters = parameters;
            Body = body;
            MinArgs = minArgs;
            RestParameter = restParameter;
            IsAsync = isAsync;
            ParameterSpecs = parameterSpecs ?? new List<FunctionParameterSpec>();
        }
    }

    /// <summary>
    /// Defines the <see cref="FuncExpr" />
    /// </summary>
    public class FuncExpr : Expr
    {
        /// <summary>
        /// Gets the Parameters
        /// </summary>
        public List<string> Parameters { get; }

        /// <summary>
        /// Gets the MinArgs
        /// </summary>
        public int MinArgs { get; }

        /// <summary>
        /// Gets the RestParameter
        /// </summary>
        public string? RestParameter { get; }

        /// <summary>
        /// Gets a value indicating whether IsAsync
        /// </summary>
        public bool IsAsync { get; }

        /// <summary>
        /// Gets the Body
        /// </summary>
        public BlockStmt Body { get; }

        /// <summary>
        /// Gets or sets the raw parameter specifications used for lowering.
        /// </summary>
        public List<FunctionParameterSpec> ParameterSpecs { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="FuncExpr"/> class.
        /// </summary>
        /// <param name="parameters">The parameters<see cref="List{string}"/></param>
        /// <param name="body">The body<see cref="BlockStmt"/></param>
        /// <param name="minArgs">The minArgs<see cref="int"/></param>
        /// <param name="restParameter">The restParameter<see cref="string?"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        /// <param name="isAsync">The isAsync<see cref="bool"/></param>
        public FuncExpr(
            List<string> parameters,
            BlockStmt body,
            int minArgs,
            string? restParameter,
            int line,
            int col,
            string fname,
            bool isAsync = false,
            List<FunctionParameterSpec>? parameterSpecs = null)
            : base(line, col, fname)
        {
            Parameters = parameters;
            Body = body;
            MinArgs = minArgs;
            RestParameter = restParameter;
            IsAsync = isAsync;
            ParameterSpecs = parameterSpecs ?? new List<FunctionParameterSpec>();
        }
    }

    /// <summary>
    /// Defines the <see cref="NamedArgExpr" />
    /// </summary>
    public sealed class NamedArgExpr : Expr
    {
        /// <summary>
        /// Gets the Name
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the Value
        /// </summary>
        public Expr Value { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="NamedArgExpr"/> class.
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="value">The value<see cref="Expr"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="file">The file<see cref="string"/></param>
        public NamedArgExpr(string name, Expr value, int line, int col, string file) : base(line, col, file)
        {
            Name = name;
            Value = value;
        }
    }

    /// <summary>
    /// Defines the <see cref="SpreadArgExpr" />
    /// </summary>
    public sealed class SpreadArgExpr : Expr
    {
        /// <summary>
        /// Gets the Value
        /// </summary>
        public Expr Value { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SpreadArgExpr"/> class.
        /// </summary>
        /// <param name="value">The value<see cref="Expr"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="file">The file<see cref="string"/></param>
        public SpreadArgExpr(Expr value, int line, int col, string file) : base(line, col, file)
        {
            Value = value;
        }
    }

    /// <summary>
    /// Defines the <see cref="CallExpr" />
    /// </summary>
    public class CallExpr : Expr
    {
        /// <summary>
        /// Gets the Target
        /// </summary>
        public Expr? Target { get; }

        /// <summary>
        /// Gets the Args
        /// </summary>
        public List<Expr> Args { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="CallExpr"/> class.
        /// </summary>
        /// <param name="target">The target<see cref="Expr?"/></param>
        /// <param name="args">The args<see cref="List{Expr}"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public CallExpr(Expr? target, List<Expr> args, int line, int col, string fname) : base(line, col, fname)
        {
            Target = target;
            Args = args ?? new List<Expr>();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CallExpr"/> class.
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="args">The args<see cref="List{Expr}"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public CallExpr(string name, List<Expr> args, int line, int col, string fname) : this(new VarExpr(name, line, col, fname), args, line, col, fname)
        {
        }
    }

    /// <summary>
    /// Defines the <see cref="ReturnStmt" />
    /// </summary>
    public class ReturnStmt(Expr? value, int line, int col, string fname) : Stmt(line, col, fname)
    {
        /// <summary>
        /// Defines the Value
        /// </summary>
        public Expr? Value = value;
    }
}
