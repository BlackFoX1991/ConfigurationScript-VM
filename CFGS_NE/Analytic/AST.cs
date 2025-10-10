using CFGS_VM.Analytic.TTypes;

namespace CFGS_VM.Analytic
{
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
        /// Initializes a new instance of the <see cref="IndexExpr"/> class.
        /// </summary>
        /// <param name="t">The t<see cref="Expr?"/></param>
        /// <param name="i">The i<see cref="Expr?"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public IndexExpr(Expr? t, Expr? i, int line, int col, string fname) : base(line, col, fname)
        {
            Target = t; Index = i;
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
    /// Defines the <see cref="PrintStmt" />
    /// </summary>
    public class PrintStmt : Stmt
    {
        /// <summary>
        /// Defines the Expression
        /// </summary>
        public Expr Expression;

        /// <summary>
        /// Initializes a new instance of the <see cref="PrintStmt"/> class.
        /// </summary>
        /// <param name="e">The e<see cref="Expr"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public PrintStmt(Expr e, int line, int col, string fname) : base(line, col, fname)
        {
            Expression = e;
        }
    }

    /// <summary>
    /// Defines the <see cref="EmitStmt" />
    /// </summary>
    public class EmitStmt : Stmt
    {
        /// <summary>
        /// Defines the Command
        /// </summary>
        public int Command;

        /// <summary>
        /// Defines the Argument
        /// </summary>
        public object? Argument;

        /// <summary>
        /// Initializes a new instance of the <see cref="EmitStmt"/> class.
        /// </summary>
        /// <param name="command">The command<see cref="int"/></param>
        /// <param name="argm">The argm<see cref="object?"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public EmitStmt(int command, object? argm, int line, int col, string fname) : base(line, col, fname)
        {
            Command = command;
            Argument = argm;
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
        /// Defines the BaseName
        /// </summary>
        public string? BaseName;

        /// <summary>
        /// Defines the BaseCtorArgs
        /// </summary>
        public List<Expr> BaseCtorArgs = new();

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
        /// <param name="enums">The enums<see cref="List{EnumDeclStmt}"/></param>
        /// <param name="fields">The fields<see cref="Dictionary{string, Expr?}"/></param>
        /// <param name="staticFields">The staticFields<see cref="Dictionary{string, Expr?}"/></param>
        /// <param name="staticMethods">The staticMethods<see cref="List{FuncDeclStmt}"/></param>
        /// <param name="parameters">The parameters<see cref="List{string}"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        /// <param name="baseName">The baseName<see cref="string?"/></param>
        /// <param name="baseArgs">The baseArgs<see cref="List{Expr}?"/></param>
        /// <param name="nestedClasses">The nestedClasses<see cref="List{ClassDeclStmt}?"/></param>
        /// <param name="isNested">The isNested<see cref="bool"/></param>
        public ClassDeclStmt(
            string name,
            List<FuncDeclStmt> methods,
            List<EnumDeclStmt> enums,
            Dictionary<string, Expr?> fields,
            Dictionary<string, Expr?> staticFields,
            List<FuncDeclStmt> staticMethods,
            List<string> parameters,
            int line,
            int col,
            string fname,
            string? baseName = null,
        List<Expr>? baseArgs = null,
            List<ClassDeclStmt>? nestedClasses = null,
    bool isNested = false
        ) : base(line, col, fname)
        {
            Name = name;
            Methods = methods;
            Enums = enums;
            Fields = fields;
            Parameters = parameters;
            StaticFields = staticFields;
            StaticMethods = staticMethods;
            BaseName = baseName;
            if (baseArgs != null) BaseCtorArgs = baseArgs;
            NestedClasses = nestedClasses ?? new List<ClassDeclStmt>();
            IsNested = isNested;
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
    public class NewExpr : Expr
    {
        /// <summary>
        /// Gets the ClassName
        /// </summary>
        public string ClassName { get; }

        /// <summary>
        /// Gets the Args
        /// </summary>
        public List<Expr> Args { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="NewExpr"/> class.
        /// </summary>
        /// <param name="className">The className<see cref="string"/></param>
        /// <param name="args">The args<see cref="List{Expr}"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public NewExpr(string className, List<Expr> args, int line, int col, string fname)
            : base(line, col, fname)
        {
            ClassName = className;
            Args = args;
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
        /// Gets a value indicating whether DeclareLocal
        /// </summary>
        public bool DeclareLocal { get; }

        /// <summary>
        /// Gets the Iterable
        /// </summary>
        public Expr Iterable { get; }

        /// <summary>
        /// Gets the Body
        /// </summary>
        public Stmt Body { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ForeachStmt"/> class.
        /// </summary>
        /// <param name="varName">The varName<see cref="string"/></param>
        /// <param name="declareLocal">The declareLocal<see cref="bool"/></param>
        /// <param name="iterable">The iterable<see cref="Expr"/></param>
        /// <param name="body">The body<see cref="Stmt"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="origin">The origin<see cref="string"/></param>
        public ForeachStmt(string varName, bool declareLocal, Expr iterable, Stmt body, int line, int col, string origin)
            : base(line, col, origin)
        {
            VarName = varName;
            DeclareLocal = declareLocal;
            Iterable = iterable;
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
    /// Defines the <see cref="TryCatchFinallyStmt" />
    /// </summary>
    public class TryCatchFinallyStmt : Stmt
    {
        /// <summary>
        /// Gets the TryBlock
        /// </summary>
        public BlockStmt TryBlock { get; }

        /// <summary>
        /// Gets the CatchVar
        /// </summary>
        public string? CatchVar { get; }

        /// <summary>
        /// Gets the CatchBlock
        /// </summary>
        public BlockStmt? CatchBlock { get; }

        /// <summary>
        /// Gets the FinallyBlock
        /// </summary>
        public BlockStmt? FinallyBlock { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TryCatchFinallyStmt"/> class.
        /// </summary>
        /// <param name="tryBlock">The tryBlock<see cref="BlockStmt"/></param>
        /// <param name="catchVar">The catchVar<see cref="string?"/></param>
        /// <param name="catchBlock">The catchBlock<see cref="BlockStmt?"/></param>
        /// <param name="finallyBlock">The finallyBlock<see cref="BlockStmt?"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public TryCatchFinallyStmt(BlockStmt tryBlock, string? catchVar, BlockStmt? catchBlock, BlockStmt? finallyBlock, int line, int col, string fname)
            : base(line, col, fname)
        {
            TryBlock = tryBlock;
            CatchVar = catchVar;
            CatchBlock = catchBlock;
            FinallyBlock = finallyBlock;
        }
    }

    /// <summary>
    /// Defines the <see cref="ThrowStmt" />
    /// </summary>
    public class ThrowStmt : Stmt
    {
        /// <summary>
        /// Gets the Value
        /// </summary>
        public Expr Value { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ThrowStmt"/> class.
        /// </summary>
        /// <param name="value">The value<see cref="Expr"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public ThrowStmt(Expr value, int line, int col, string fname) : base(line, col, fname)
        {
            Value = value;
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
        public Expr Pattern;

        /// <summary>
        /// Defines the Body
        /// </summary>
        public BlockStmt Body;

        /// <summary>
        /// Initializes a new instance of the <see cref="CaseClause"/> class.
        /// </summary>
        /// <param name="pattern">The pattern<see cref="Expr"/></param>
        /// <param name="body">The body<see cref="BlockStmt"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public CaseClause(Expr pattern, BlockStmt body, int line, int col, string fname) : base(line, col, fname)
        {
            Pattern = pattern;
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
        /// Defines the Body
        /// </summary>
        public BlockStmt Body;

        /// <summary>
        /// Initializes a new instance of the <see cref="FuncDeclStmt"/> class.
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="parameters">The parameters<see cref="List{string}"/></param>
        /// <param name="body">The body<see cref="BlockStmt"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public FuncDeclStmt(string name, List<string> parameters, BlockStmt body, int line, int col, string fname) : base(line, col, fname)
        {
            Name = name;
            Parameters = parameters;
            Body = body;
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
        /// Gets the Body
        /// </summary>
        public BlockStmt Body { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="FuncExpr"/> class.
        /// </summary>
        /// <param name="parameters">The parameters<see cref="List{string}"/></param>
        /// <param name="body">The body<see cref="BlockStmt"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fname">The fname<see cref="string"/></param>
        public FuncExpr(List<string> parameters, BlockStmt body, int line, int col, string fname)
            : base(line, col, fname)
        {
            Parameters = parameters;
            Body = body;
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
    public class ReturnStmt(Expr value, int line, int col, string fname) : Stmt(line, col, fname)
    {
        /// <summary>
        /// Defines the Value
        /// </summary>
        public Expr Value = value;
    }
}
