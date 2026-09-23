using System.Text;

namespace CFGS_VM.Analytic.Semantics
{
    /// <summary>
    /// Names the single global binding that stores a namespace member. Namespaces exist only at compile
    /// time: every member of namespace <c>App.Core</c> named <c>add</c> lives in one global binding whose
    /// name encodes both parts, and every reference to the member is compiled to that binding. The
    /// encoding is reversible so runtime messages, the debugger, and the hosting API can show the
    /// qualified name instead of the binding name.
    /// </summary>
    internal static class NamespaceRuntimeBindingNames
    {
        public const string HiddenMemberPrefix = "__ns_member_";

        public static bool IsReservedUserIdentifier(string name)
            => name.StartsWith(HiddenMemberPrefix, StringComparison.Ordinal);

        public static bool IsMemberBindingName(string? name)
            => name is not null && name.StartsWith(HiddenMemberPrefix, StringComparison.Ordinal);

        public static string GetHiddenMemberBindingName(string namespacePath, string memberName)
            => $"{HiddenMemberPrefix}{Encode(namespacePath)}_{Encode(memberName)}";

        /// <summary>
        /// Recovers the namespace path and member name from a binding name created by
        /// <see cref="GetHiddenMemberBindingName"/>.
        /// </summary>
        public static bool TryDecode(string? bindingName, out string namespacePath, out string memberName)
        {
            namespacePath = string.Empty;
            memberName = string.Empty;

            if (!IsMemberBindingName(bindingName))
                return false;

            string encoded = bindingName![HiddenMemberPrefix.Length..];
            int separator = encoded.IndexOf('_');
            if (separator <= 0 || separator == encoded.Length - 1)
                return false;

            if (!TryDecodeHex(encoded[..separator], out namespacePath) ||
                !TryDecodeHex(encoded[(separator + 1)..], out memberName))
            {
                namespacePath = string.Empty;
                memberName = string.Empty;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Returns the qualified name (<c>App.Core.add</c>) for a member binding name, or the name unchanged
        /// when it is not a member binding.
        /// </summary>
        public static string ToDisplayName(string name)
            => TryDecode(name, out string namespacePath, out string memberName) ? $"{namespacePath}.{memberName}" : name;

        /// <summary>
        /// Yields every way to read a qualified name as a member binding followed by a member path, longest
        /// namespace prefix first. For <c>App.Core.Box.Inner</c> that is the binding of member Inner in
        /// App.Core.Box, then the binding of Box in App.Core followed by [Inner], then the binding of Core in
        /// App followed by [Box, Inner]. The caller uses the first binding that exists.
        /// </summary>
        public static IEnumerable<(string BindingName, string[] Remaining)> EnumerateQualifiedCandidates(string qualifiedName)
        {
            if (string.IsNullOrWhiteSpace(qualifiedName))
                yield break;

            string[] parts = qualifiedName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (int memberIndex = parts.Length - 1; memberIndex >= 1; memberIndex--)
            {
                string namespacePath = string.Join(".", parts, 0, memberIndex);
                yield return (GetHiddenMemberBindingName(namespacePath, parts[memberIndex]), parts[(memberIndex + 1)..]);
            }
        }

        private static string Encode(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            StringBuilder sb = new(bytes.Length * 2);
            foreach (byte b in bytes)
                sb.Append(b.ToString("x2"));

            return sb.ToString();
        }

        private static bool TryDecodeHex(string hex, out string value)
        {
            value = string.Empty;
            if (hex.Length == 0 || hex.Length % 2 != 0)
                return false;

            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                if (!byte.TryParse(hex.AsSpan(i * 2, 2), System.Globalization.NumberStyles.HexNumber, null, out bytes[i]))
                    return false;
            }

            value = Encoding.UTF8.GetString(bytes);
            return true;
        }
    }
}
