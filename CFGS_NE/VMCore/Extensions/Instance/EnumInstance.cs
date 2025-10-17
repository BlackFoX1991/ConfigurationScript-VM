using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CFGS_VM.VMCore.Extensions.Instance
{
    public sealed class EnumInstance
    {
        public string EnumName { get; }
        public Dictionary<string, int> Values { get; }   // nur intern
        public EnumInstance(string enumName)
        {
            EnumName = enumName ?? throw new ArgumentNullException(nameof(enumName));
            Values = new Dictionary<string, int>();
        }

        public bool TryGet(string name, out int value) => Values.TryGetValue(name, out value);
        public void Add(string name, int value) => Values.Add(name, value); // nur Compiler/INIT
        public override string ToString() => $"<enum {EnumName}>";
    }

}
