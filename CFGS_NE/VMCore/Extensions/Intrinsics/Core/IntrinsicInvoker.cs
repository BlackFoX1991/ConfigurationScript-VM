using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CFGS_VM.VMCore.Extensions.Intrinsics.Core
{
    public delegate object IntrinsicInvoker(object receiver, List<object> args, Instruction instr);
}
