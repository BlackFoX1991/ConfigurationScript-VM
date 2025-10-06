using CFGS_VM.Analytic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CFGS_VM.VMCore.Extensions
{
    [Flags]
    public enum Modifiers { None = 0, Public = 1, Private = 2, Protected = 4, Internal = 8, Static = 16 }


    public sealed class QualifiedNameExpr(List<string>? Pts, int line = 0, int column = 0, string fname = "") : Expr(line, column, fname) { public List<string>? Parts = Pts; /* ctor usw. */ }
    // NewExpr: ersetze string ClassName -> QualifiedNameExpr TypeName

}
