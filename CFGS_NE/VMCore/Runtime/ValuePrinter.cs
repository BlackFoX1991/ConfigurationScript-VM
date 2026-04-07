using CFGS_VM.VMCore.Extensions;
using CFGS_VM.VMCore.Extensions.Core;
using CFGS_VM.VMCore.Extensions.Instance;
using CFGS_VM.VMCore.Extensions.Intrinsics.Handles;
using System.Globalization;
using System.Text;

namespace CFGS_VM.VMCore
{
    public partial class VM
    {
        /// <summary>
        /// Formats a runtime value for compact debug output.
        /// </summary>
        private string FormatVal(object? v)
        {
            if (v == null)
                return "null";

            return v switch
            {
                string s => $"\"{s}\"",
                List<object> list => $"[{GetListCount(list)} elems]",
                Dictionary<string, object> dict => $"{{{GetDictionaryCount(dict)} pairs}}",
                ClassInstance ci => $"Object({ci.ClassName})",
                Closure clos => $"Closure({clos.Name ?? clos.Address.ToString()})",
                BoundMethod bm => $"BoundMethod({bm.Function.Name ?? bm.Function.Address.ToString()})",
                _ => $"{v} : {v.GetType().Name}"
            };
        }

        /// <summary>
        /// Escapes a string for JSON output.
        /// </summary>
        private static string JsonEscapeString(string s)
        {
            StringBuilder sb = new(s.Length + 8);
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '\"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < ' ')
                            sb.AppendFormat(CultureInfo.InvariantCulture, "\\u{0:X4}", (int)ch);
                        else
                            sb.Append(ch);
                        break;
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Writes a runtime value as JSON.
        /// </summary>
        private static void WriteJsonValue(object? v, TextWriter w, HashSet<object>? seen = null, int mode = 2)
        {
            seen ??= new HashSet<object>(ReferenceEqualityComparer.Instance);

            if (v is null)
            {
                w.Write("null");
                return;
            }

            switch (v)
            {
                case bool b:
                    w.Write(b ? "true" : "false");
                    return;

                case sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal:
                    w.Write(Convert.ToString(v, CultureInfo.InvariantCulture));
                    return;

                case string s:
                    w.Write('"');
                    w.Write(JsonEscapeString(s));
                    w.Write('"');
                    return;

                case ExceptionObject exo:
                    w.Write('{');
                    w.Write("\"type\":\"");
                    w.Write(JsonEscapeString(exo.Type));
                    w.Write("\",");
                    w.Write("\"message\":\"");
                    w.Write(JsonEscapeString(exo.eMessage));
                    w.Write("\",");
                    w.Write("\"file\":\"");
                    w.Write(JsonEscapeString(exo.File));
                    w.Write("\",");
                    w.Write("\"line\":");
                    w.Write(exo.Line.ToString(CultureInfo.InvariantCulture));
                    w.Write(",");
                    w.Write("\"col\":");
                    w.Write(exo.Col.ToString(CultureInfo.InvariantCulture));
                    if (!string.IsNullOrEmpty(exo.Stack))
                    {
                        w.Write(",\"stack\":\"");
                        w.Write(JsonEscapeString(exo.Stack));
                        w.Write('"');
                    }
                    w.Write('}');
                    return;

                case List<object> list:
                    if (seen.Contains(v))
                    {
                        w.Write("[]");
                        return;
                    }

                    seen.Add(v);
                    List<object> items = SnapshotList(list);
                    w.Write('[');
                    for (int i = 0; i < items.Count; i++)
                    {
                        WriteJsonValue(items[i], w, seen, mode);
                        if (i + 1 < items.Count)
                            w.Write(',');
                    }
                    w.Write(']');
                    seen.Remove(v);
                    return;

                case Dictionary<string, object> dict:
                    if (seen.Contains(v))
                    {
                        w.Write("{}");
                        return;
                    }

                    seen.Add(v);
                    List<KeyValuePair<string, object>> entries = SnapshotOrderedDictionaryEntries(dict);
                    if (mode == 2)
                    {
                        entries =
                        [
                            .. entries.Where(kv => kv.Value != null &&
                                                   kv.Value is not Closure &&
                                                   kv.Value is not FunctionInfo &&
                                                   kv.Value is not Delegate)
                        ];
                    }

                    w.Write('{');
                    for (int i = 0; i < entries.Count; i++)
                    {
                        KeyValuePair<string, object> kv = entries[i];
                        w.Write('"');
                        w.Write(JsonEscapeString(kv.Key));
                        w.Write("\":");
                        WriteJsonValue(kv.Value, w, seen, mode);
                        if (i + 1 < entries.Count)
                            w.Write(',');
                    }
                    w.Write('}');
                    seen.Remove(v);
                    return;

                case Closure clos:
                    w.Write('"');
                    w.Write(JsonEscapeString(clos.Name ?? "<closure>"));
                    w.Write('"');
                    return;

                default:
                    w.Write('"');
                    w.Write(JsonEscapeString(Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty));
                    w.Write('"');
                    return;
            }
        }

        /// <summary>
        /// Serializes a runtime value to JSON.
        /// </summary>
        public static string JsonStringify(object? v, int mode = 2)
        {
            StringBuilder sb = new();
            using StringWriter sw = new(sb, CultureInfo.InvariantCulture);
            WriteJsonValue(v, sw, null, mode);
            return sb.ToString();
        }

        /// <summary>
        /// Writes a runtime value in CFGS display format.
        /// </summary>
        public static void PrintValue(object v, TextWriter w, int mode = 2, HashSet<object>? seen = null, bool escapeNewlines = false)
        {
            static string UnescapeForPrinting(string s)
            {
                return s
                    .Replace("\\n", "\n")
                    .Replace("\\r", "\r")
                    .Replace("\\t", "\t")
                    .Replace("\\b", "\b")
                    .Replace("\\f", "\f");
            }

            seen ??= new HashSet<object>(ReferenceEqualityComparer.Instance);

            if (v == null)
            {
                w.Write("null");
                return;
            }

            if (v is List<object> list)
            {
                if (seen.Contains(v))
                {
                    w.Write("[...]");
                    return;
                }

                seen.Add(v);
                List<object> items = SnapshotList(list);
                w.Write("[");
                for (int i = 0; i < items.Count; i++)
                {
                    PrintValue(items[i], w, mode, seen, escapeNewlines);
                    if (i + 1 < items.Count)
                        w.Write(", ");
                }
                w.Write("]");
                seen.Remove(v);
                return;
            }

            if (v is Dictionary<string, object> dict)
            {
                if (seen.Contains(v))
                {
                    w.Write("{...}");
                    return;
                }

                seen.Add(v);
                List<KeyValuePair<string, object>> entries = SnapshotOrderedDictionaryEntries(dict);
                if (mode == 2)
                {
                    entries =
                    [
                        .. entries.Where(kv => kv.Value != null &&
                                               kv.Value is not Closure &&
                                               kv.Value is not FunctionInfo &&
                                               kv.Value is not Delegate)
                    ];
                }

                w.Write("{");
                for (int i = 0; i < entries.Count; i++)
                {
                    KeyValuePair<string, object> kv = entries[i];
                    w.Write("\"");
                    w.Write(escapeNewlines ? UnescapeForPrinting(kv.Key) : kv.Key);
                    w.Write("\": ");
                    PrintValue(kv.Value, w, mode, seen, escapeNewlines);
                    if (i + 1 < entries.Count)
                        w.Write(", ");
                }
                w.Write("}");
                seen.Remove(v);
                return;
            }

            if (v is ClassInstance ci)
            {
                w.Write(ci.ClassName);
                return;
            }

            if (v is Closure clos)
            {
                if (mode == 2)
                    w.Write("\"" + (escapeNewlines ? UnescapeForPrinting(clos.Name ?? "<closure>") : (clos.Name ?? "<closure>")) + "\"");
                else
                    w.Write(clos.ToString());
                return;
            }

            if (v is ExceptionObject exo)
            {
                w.Write(exo.ToString());
                return;
            }

            if (v is FunctionInfo fi)
            {
                w.Write($"<fn {fi.Address}>");
                return;
            }

            if (v is Delegate)
            {
                w.Write("<delegate>");
                return;
            }

            switch (v)
            {
                case string s:
                    w.Write(escapeNewlines ? UnescapeForPrinting(s) : s);
                    break;
                case double xd:
                    w.Write(xd.ToString(CultureInfo.InvariantCulture));
                    break;
                case float f:
                    w.Write(f.ToString(CultureInfo.InvariantCulture));
                    break;
                case decimal m:
                    w.Write(m.ToString(CultureInfo.InvariantCulture));
                    break;
                case long l:
                    w.Write(l.ToString(CultureInfo.InvariantCulture));
                    break;
                case int i:
                    w.Write(i.ToString(CultureInfo.InvariantCulture));
                    break;
                case bool b:
                    w.Write(b ? "true" : "false");
                    break;
                default:
                    w.Write(Convert.ToString(v, CultureInfo.InvariantCulture));
                    break;
            }

            w.Flush();
        }
    }
}
