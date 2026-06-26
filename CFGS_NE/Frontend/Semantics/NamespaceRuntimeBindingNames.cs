using System.Text;
using CFGS_VM.Analytic.Tree;

namespace CFGS_VM.Analytic.Semantics
{
    internal static class NamespaceRuntimeBindingNames
    {
        public const string HiddenMemberPrefix = "__ns_member_";

        public static bool UsesSyntheticBinding(MemberVisibility visibility)
            => visibility != MemberVisibility.Public;

        public static bool IsReservedUserIdentifier(string name)
            => name.StartsWith(HiddenMemberPrefix, StringComparison.Ordinal);

        public static string GetHiddenMemberBindingName(string namespacePath, string memberName)
            => $"{HiddenMemberPrefix}{Encode(namespacePath)}_{Encode(memberName)}";

        private static string Encode(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            StringBuilder sb = new(bytes.Length * 2);
            foreach (byte b in bytes)
                sb.Append(b.ToString("x2"));

            return sb.ToString();
        }
    }
}
