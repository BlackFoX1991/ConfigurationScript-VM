using CFGS_VM.VMCore;
using CFGS_VM.VMCore.Extensions;
using CFGS_VM.VMCore.Plugin;
using System.Globalization;
using System.Security;
using System.Text;
using System.Xml;

namespace CFGS.Xml;

public sealed class CFGS_XML : IVmPlugin
{
    public static bool AllowFileIO { get; set; } = true;

    public void Register(IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics)
    {
        RegisterBuiltins(builtins);
        RegisterDocumentIntrinsics(intrinsics);
        RegisterNodeIntrinsics(intrinsics);
    }

    private static void RegisterBuiltins(IBuiltinRegistry builtins)
    {
        builtins.Register(new BuiltinDescriptor("xml_parse", 1, 1, (args, instr) =>
        {
            try
            {
                XmlDocument doc = NewDocument();
                using StringReader input = new(args[0]?.ToString() ?? "");
                using XmlReader reader = XmlReader.Create(input, SecureReaderSettings());
                doc.Load(reader);
                return new XmlDocumentHandle(doc);
            }
            catch (Exception ex)
            {
                throw Error(instr, "xml_parse failed", ex);
            }
        }));

        builtins.Register(new BuiltinDescriptor("xml_load", 1, 1, (args, instr) =>
        {
            EnsureFileIo(instr);
            string path = args[0]?.ToString() ?? "";
            try
            {
                XmlDocument doc = NewDocument();
                using XmlReader reader = XmlReader.Create(path, SecureReaderSettings());
                doc.Load(reader);
                return new XmlDocumentHandle(doc, path);
            }
            catch (Exception ex)
            {
                throw Error(instr, $"xml_load('{path}') failed", ex);
            }
        }));

        builtins.Register(new BuiltinDescriptor("xml_new", 0, 3, (args, instr) =>
        {
            try
            {
                XmlDocument doc = NewDocument();
                if (args.Count == 0 || string.IsNullOrWhiteSpace(args[0]?.ToString()))
                    return new XmlDocumentHandle(doc);

                string name = args[0]?.ToString() ?? "root";
                string ns = args.Count >= 2 ? args[1]?.ToString() ?? "" : "";
                string prefix = args.Count >= 3 ? args[2]?.ToString() ?? "" : "";
                XmlElement root = CreateElement(doc, name, ns, prefix);
                doc.AppendChild(root);
                return new XmlDocumentHandle(doc);
            }
            catch (Exception ex)
            {
                throw Error(instr, "xml_new failed", ex);
            }
        }));

        builtins.Register(new BuiltinDescriptor("xml_escape", 1, 1, (args, instr) =>
            SecurityElement.Escape(args[0]?.ToString() ?? "") ?? ""));

        builtins.Register(new BuiltinDescriptor("xml_unescape", 1, 1, (args, instr) =>
            System.Net.WebUtility.HtmlDecode(args[0]?.ToString() ?? "")));
    }

    private static void RegisterDocumentIntrinsics(IIntrinsicRegistry intrinsics)
    {
        Type T = typeof(XmlDocumentHandle);

        intrinsics.Register(T, new IntrinsicDescriptor("root", 0, 0, (recv, args, instr) =>
        {
            XmlElement? root = ((XmlDocumentHandle)recv).Document.DocumentElement;
            return root is null ? null! : new XmlNodeHandle(root);
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("path", 0, 0, (recv, args, instr) =>
            ((XmlDocumentHandle)recv).Path ?? ""));

        intrinsics.Register(T, new IntrinsicDescriptor("to_string", 0, 2, (recv, args, instr) =>
            Serialize(((XmlDocumentHandle)recv).Document, BoolArg(args, 0, true), BoolArg(args, 1, false))));

        intrinsics.Register(T, new IntrinsicDescriptor("save", 1, 3, (recv, args, instr) =>
        {
            EnsureFileIo(instr);
            XmlDocumentHandle handle = (XmlDocumentHandle)recv;
            string path = args[0]?.ToString() ?? "";
            bool indented = BoolArg(args, 1, true);
            bool omitDeclaration = BoolArg(args, 2, false);
            try
            {
                string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                XmlWriterSettings settings = WriterSettings(indented, omitDeclaration);
                using XmlWriter writer = XmlWriter.Create(path, settings);
                handle.Document.Save(writer);
                handle.Path = path;
                return path;
            }
            catch (Exception ex)
            {
                throw Error(instr, $"xml save('{path}') failed", ex);
            }
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("declaration", 0, 3, (recv, args, instr) =>
        {
            XmlDocument doc = ((XmlDocumentHandle)recv).Document;
            if (args.Count == 0)
            {
                if (doc.FirstChild is XmlDeclaration existing)
                    return new Dictionary<string, object>
                    {
                        ["version"] = existing.Version,
                        ["encoding"] = existing.Encoding,
                        ["standalone"] = existing.Standalone
                    };
                return null!;
            }

            string version = args[0]?.ToString() ?? "1.0";
            string? encoding = args.Count >= 2 ? args[1]?.ToString() : null;
            string? standalone = null;
            if (args.Count >= 3)
            {
                object? raw = args[2];
                standalone = raw is bool b ? (b ? "yes" : "no") : raw?.ToString();
            }

            XmlDeclaration decl = doc.CreateXmlDeclaration(version, encoding, standalone);
            if (doc.FirstChild is XmlDeclaration oldDecl)
                doc.ReplaceChild(decl, oldDecl);
            else
                doc.InsertBefore(decl, doc.FirstChild);
            return recv;
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("create_element", 1, 4, (recv, args, instr) =>
        {
            XmlDocument doc = ((XmlDocumentHandle)recv).Document;
            string name = args[0]?.ToString() ?? "";
            string text = args.Count >= 2 ? args[1]?.ToString() ?? "" : "";
            string ns = args.Count >= 3 ? args[2]?.ToString() ?? "" : "";
            string prefix = args.Count >= 4 ? args[3]?.ToString() ?? "" : "";
            XmlElement el = CreateElement(doc, name, ns, prefix);
            if (args.Count >= 2)
                el.InnerText = text;
            return new XmlNodeHandle(el);
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("create_text", 1, 1, (recv, args, instr) =>
            new XmlNodeHandle(((XmlDocumentHandle)recv).Document.CreateTextNode(args[0]?.ToString() ?? ""))));

        intrinsics.Register(T, new IntrinsicDescriptor("create_cdata", 1, 1, (recv, args, instr) =>
            new XmlNodeHandle(((XmlDocumentHandle)recv).Document.CreateCDataSection(args[0]?.ToString() ?? ""))));

        intrinsics.Register(T, new IntrinsicDescriptor("create_comment", 1, 1, (recv, args, instr) =>
            new XmlNodeHandle(((XmlDocumentHandle)recv).Document.CreateComment(args[0]?.ToString() ?? ""))));

        intrinsics.Register(T, new IntrinsicDescriptor("create_pi", 2, 2, (recv, args, instr) =>
            new XmlNodeHandle(((XmlDocumentHandle)recv).Document.CreateProcessingInstruction(args[0]?.ToString() ?? "", args[1]?.ToString() ?? ""))));

        intrinsics.Register(T, new IntrinsicDescriptor("append", 1, 1, (recv, args, instr) =>
        {
            XmlDocument doc = ((XmlDocumentHandle)recv).Document;
            XmlNode child = ImportFor(doc, RequireNode(args[0], instr), deep: true);
            doc.AppendChild(child);
            return new XmlNodeHandle(child);
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("select", 1, 2, (recv, args, instr) =>
            SelectOne(((XmlDocumentHandle)recv).Document, args, instr)));

        intrinsics.Register(T, new IntrinsicDescriptor("select_all", 1, 2, (recv, args, instr) =>
            SelectAll(((XmlDocumentHandle)recv).Document, args, instr)));

        intrinsics.Register(T, new IntrinsicDescriptor("to_dict", 0, 0, (recv, args, instr) =>
            NodeToDict(((XmlDocumentHandle)recv).Document)));

        intrinsics.Register(T, new IntrinsicDescriptor("toString", 0, 0, (recv, args, instr) =>
            Serialize(((XmlDocumentHandle)recv).Document, true, false)));
    }

    private static void RegisterNodeIntrinsics(IIntrinsicRegistry intrinsics)
    {
        Type T = typeof(XmlNodeHandle);

        intrinsics.Register(T, new IntrinsicDescriptor("name", 0, 0, (recv, args, instr) => ((XmlNodeHandle)recv).Node.Name));
        intrinsics.Register(T, new IntrinsicDescriptor("local_name", 0, 0, (recv, args, instr) => ((XmlNodeHandle)recv).Node.LocalName));
        intrinsics.Register(T, new IntrinsicDescriptor("namespace_uri", 0, 0, (recv, args, instr) => ((XmlNodeHandle)recv).Node.NamespaceURI));
        intrinsics.Register(T, new IntrinsicDescriptor("prefix", 0, 1, (recv, args, instr) =>
        {
            XmlNode node = ((XmlNodeHandle)recv).Node;
            if (args.Count == 0)
                return node.Prefix;
            node.Prefix = args[0]?.ToString() ?? "";
            return recv;
        }));
        intrinsics.Register(T, new IntrinsicDescriptor("node_type", 0, 0, (recv, args, instr) => ((XmlNodeHandle)recv).Node.NodeType.ToString()));

        intrinsics.Register(T, new IntrinsicDescriptor("text", 0, 0, (recv, args, instr) => ((XmlNodeHandle)recv).Node.InnerText));
        intrinsics.Register(T, new IntrinsicDescriptor("set_text", 1, 1, (recv, args, instr) =>
        {
            ((XmlNodeHandle)recv).Node.InnerText = args[0]?.ToString() ?? "";
            return recv;
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("value", 0, 1, (recv, args, instr) =>
        {
            XmlNode node = ((XmlNodeHandle)recv).Node;
            if (args.Count == 0)
                return node.Value ?? "";
            node.Value = args[0]?.ToString() ?? "";
            return recv;
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("inner_xml", 0, 1, (recv, args, instr) =>
        {
            XmlNode node = ((XmlNodeHandle)recv).Node;
            if (args.Count == 0)
                return node.InnerXml;
            try
            {
                node.InnerXml = args[0]?.ToString() ?? "";
                return recv;
            }
            catch (Exception ex)
            {
                throw Error(instr, "setting inner_xml failed", ex);
            }
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("outer_xml", 0, 0, (recv, args, instr) => ((XmlNodeHandle)recv).Node.OuterXml));

        intrinsics.Register(T, new IntrinsicDescriptor("attr", 1, 3, (recv, args, instr) =>
        {
            XmlElement el = RequireElement(((XmlNodeHandle)recv).Node, instr, "attr");
            string name = args[0]?.ToString() ?? "";
            string ns = args.Count >= 3 ? args[2]?.ToString() ?? "" : "";
            string value = string.IsNullOrEmpty(ns) ? el.GetAttribute(name) : el.GetAttribute(name, ns);
            if (value.Length == 0 && !HasAttribute(el, name, ns) && args.Count >= 2)
                return args[1];
            return value;
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("set_attr", 2, 4, (recv, args, instr) =>
        {
            XmlElement el = RequireElement(((XmlNodeHandle)recv).Node, instr, "set_attr");
            SetAttribute(el, args[0]?.ToString() ?? "", args[1]?.ToString() ?? "", args.Count >= 3 ? args[2]?.ToString() ?? "" : "", args.Count >= 4 ? args[3]?.ToString() ?? "" : "");
            return recv;
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("has_attr", 1, 2, (recv, args, instr) =>
        {
            XmlElement el = RequireElement(((XmlNodeHandle)recv).Node, instr, "has_attr");
            return HasAttribute(el, args[0]?.ToString() ?? "", args.Count >= 2 ? args[1]?.ToString() ?? "" : "");
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("remove_attr", 1, 2, (recv, args, instr) =>
        {
            XmlElement el = RequireElement(((XmlNodeHandle)recv).Node, instr, "remove_attr");
            string name = args[0]?.ToString() ?? "";
            string ns = args.Count >= 2 ? args[1]?.ToString() ?? "" : "";
            if (string.IsNullOrEmpty(ns))
                el.RemoveAttribute(name);
            else
                el.RemoveAttribute(name, ns);
            return recv;
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("attrs", 0, 0, (recv, args, instr) =>
        {
            Dictionary<string, object> result = new(StringComparer.Ordinal);
            XmlAttributeCollection? attrs = ((XmlNodeHandle)recv).Node.Attributes;
            if (attrs == null)
                return result;
            foreach (XmlAttribute attr in attrs)
                result[attr.Name] = attr.Value;
            return result;
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("children", 0, 0, (recv, args, instr) =>
            ((XmlNodeHandle)recv).Node.ChildNodes.Cast<XmlNode>().Select(n => (object)new XmlNodeHandle(n)).ToList()));

        intrinsics.Register(T, new IntrinsicDescriptor("elements", 0, 1, (recv, args, instr) =>
        {
            string? name = args.Count >= 1 ? args[0]?.ToString() : null;
            List<object> result = new();
            foreach (XmlNode child in ((XmlNodeHandle)recv).Node.ChildNodes)
            {
                if (child is not XmlElement) continue;
                if (string.IsNullOrEmpty(name) || child.Name == name || child.LocalName == name)
                    result.Add(new XmlNodeHandle(child));
            }
            return result;
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("parent", 0, 0, (recv, args, instr) =>
        {
            XmlNode? parent = ((XmlNodeHandle)recv).Node.ParentNode;
            return parent is null ? null! : new XmlNodeHandle(parent);
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("owner_document", 0, 0, (recv, args, instr) =>
            new XmlDocumentHandle(OwnerDocument(((XmlNodeHandle)recv).Node))));

        intrinsics.Register(T, new IntrinsicDescriptor("append", 1, 1, (recv, args, instr) =>
            AppendChild(((XmlNodeHandle)recv).Node, RequireNode(args[0], instr), atStart: false)));

        intrinsics.Register(T, new IntrinsicDescriptor("prepend", 1, 1, (recv, args, instr) =>
            AppendChild(((XmlNodeHandle)recv).Node, RequireNode(args[0], instr), atStart: true)));

        intrinsics.Register(T, new IntrinsicDescriptor("before", 1, 1, (recv, args, instr) =>
        {
            XmlNode node = ((XmlNodeHandle)recv).Node;
            XmlNode parent = node.ParentNode ?? throw Error(instr, "cannot insert before a node without parent");
            XmlNode inserted = ImportFor(parent, RequireNode(args[0], instr), deep: true);
            parent.InsertBefore(inserted, node);
            return new XmlNodeHandle(inserted);
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("after", 1, 1, (recv, args, instr) =>
        {
            XmlNode node = ((XmlNodeHandle)recv).Node;
            XmlNode parent = node.ParentNode ?? throw Error(instr, "cannot insert after a node without parent");
            XmlNode inserted = ImportFor(parent, RequireNode(args[0], instr), deep: true);
            parent.InsertAfter(inserted, node);
            return new XmlNodeHandle(inserted);
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("remove", 0, 0, (recv, args, instr) =>
        {
            XmlNode node = ((XmlNodeHandle)recv).Node;
            XmlNode? parent = node.ParentNode;
            if (parent == null)
                return 0;
            parent.RemoveChild(node);
            return 1;
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("clear", 0, 0, (recv, args, instr) =>
        {
            ((XmlNodeHandle)recv).Node.RemoveAll();
            return recv;
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("add_element", 1, 4, (recv, args, instr) =>
        {
            XmlNode parent = ((XmlNodeHandle)recv).Node;
            XmlDocument doc = OwnerDocument(parent);
            XmlElement el = CreateElement(doc, args[0]?.ToString() ?? "", args.Count >= 3 ? args[2]?.ToString() ?? "" : "", args.Count >= 4 ? args[3]?.ToString() ?? "" : "");
            if (args.Count >= 2)
                el.InnerText = args[1]?.ToString() ?? "";
            parent.AppendChild(el);
            return new XmlNodeHandle(el);
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("clone", 0, 1, (recv, args, instr) =>
            new XmlNodeHandle(((XmlNodeHandle)recv).Node.CloneNode(BoolArg(args, 0, true)))));

        intrinsics.Register(T, new IntrinsicDescriptor("select", 1, 2, (recv, args, instr) =>
            SelectOne(((XmlNodeHandle)recv).Node, args, instr)));

        intrinsics.Register(T, new IntrinsicDescriptor("select_all", 1, 2, (recv, args, instr) =>
            SelectAll(((XmlNodeHandle)recv).Node, args, instr)));

        intrinsics.Register(T, new IntrinsicDescriptor("to_dict", 0, 0, (recv, args, instr) =>
            NodeToDict(((XmlNodeHandle)recv).Node)));

        intrinsics.Register(T, new IntrinsicDescriptor("toString", 0, 0, (recv, args, instr) => ((XmlNodeHandle)recv).Node.OuterXml));
    }

    private static XmlDocument NewDocument() => new()
    {
        PreserveWhitespace = true,
        XmlResolver = null
    };

    private static XmlReaderSettings SecureReaderSettings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null
    };

    private static XmlWriterSettings WriterSettings(bool indented, bool omitDeclaration) => new()
    {
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        Indent = indented,
        OmitXmlDeclaration = omitDeclaration
    };

    private static string Serialize(XmlDocument doc, bool indented, bool omitDeclaration)
    {
        StringBuilder sb = new();
        using XmlWriter writer = XmlWriter.Create(sb, WriterSettings(indented, omitDeclaration));
        doc.Save(writer);
        return sb.ToString();
    }

    private static XmlElement CreateElement(XmlDocument doc, string name, string ns, string prefix = "")
    {
        if (!string.IsNullOrEmpty(prefix))
            return doc.CreateElement(prefix, LocalName(name), ns);
        if (!string.IsNullOrEmpty(ns) && TrySplitQName(name, out string qPrefix, out string local))
            return doc.CreateElement(qPrefix, local, ns);
        if (!string.IsNullOrEmpty(ns))
            return doc.CreateElement(name, ns);
        return doc.CreateElement(name);
    }

    private static object AppendChild(XmlNode parent, XmlNode child, bool atStart)
    {
        XmlNode imported = ImportFor(parent, child, deep: true);
        if (atStart)
            parent.InsertBefore(imported, parent.FirstChild);
        else
            parent.AppendChild(imported);
        return new XmlNodeHandle(imported);
    }

    private static XmlNode ImportFor(XmlNode targetParentOrDocument, XmlNode source, bool deep)
    {
        XmlDocument targetDoc = targetParentOrDocument is XmlDocument doc ? doc : OwnerDocument(targetParentOrDocument);
        if (ReferenceEquals(source.OwnerDocument, targetDoc) || ReferenceEquals(source, targetDoc))
            return source;
        return targetDoc.ImportNode(source, deep);
    }

    private static XmlDocument OwnerDocument(XmlNode node)
    {
        if (node is XmlDocument doc)
            return doc;
        return node.OwnerDocument ?? throw new InvalidOperationException("XML node has no owner document.");
    }

    private static XmlNode RequireNode(object? value, Instruction instr)
    {
        return value switch
        {
            XmlNodeHandle node => node.Node,
            XmlDocumentHandle doc => doc.Document,
            _ => throw Error(instr, "expected XmlNodeHandle or XmlDocumentHandle")
        };
    }

    private static XmlElement RequireElement(XmlNode node, Instruction instr, string op)
    {
        if (node is XmlElement el)
            return el;
        throw Error(instr, $"{op} requires an XML element node");
    }

    private static object SelectOne(XmlNode node, List<object> args, Instruction instr)
    {
        try
        {
            XmlNamespaceManager? ns = BuildNamespaceManager(OwnerDocument(node), args.Count >= 2 ? args[1] : null);
            string xpath = args[0]?.ToString() ?? "";
            XmlNode? found = ns is null ? node.SelectSingleNode(xpath) : node.SelectSingleNode(xpath, ns);
            return found is null ? null! : new XmlNodeHandle(found);
        }
        catch (Exception ex)
        {
            throw Error(instr, "xml select failed", ex);
        }
    }

    private static object SelectAll(XmlNode node, List<object> args, Instruction instr)
    {
        try
        {
            XmlNamespaceManager? ns = BuildNamespaceManager(OwnerDocument(node), args.Count >= 2 ? args[1] : null);
            string xpath = args[0]?.ToString() ?? "";
            XmlNodeList? nodes = ns is null ? node.SelectNodes(xpath) : node.SelectNodes(xpath, ns);
            List<object> result = new();
            if (nodes != null)
            {
                foreach (XmlNode found in nodes)
                    result.Add(new XmlNodeHandle(found));
            }
            return result;
        }
        catch (Exception ex)
        {
            throw Error(instr, "xml select_all failed", ex);
        }
    }

    private static XmlNamespaceManager? BuildNamespaceManager(XmlDocument doc, object? value)
    {
        if (value is not Dictionary<string, object> dict)
            return null;

        XmlNamespaceManager ns = new(doc.NameTable);
        foreach (KeyValuePair<string, object> kv in dict)
        {
            string prefix = kv.Key;
            string uri = kv.Value?.ToString() ?? "";
            if (prefix.Length > 0 && uri.Length > 0)
                ns.AddNamespace(prefix, uri);
        }
        return ns;
    }

    private static Dictionary<string, object> NodeToDict(XmlNode node)
    {
        Dictionary<string, object> result = new(StringComparer.Ordinal)
        {
            ["type"] = node.NodeType.ToString(),
            ["name"] = node.Name,
            ["localName"] = node.LocalName,
            ["namespaceUri"] = node.NamespaceURI,
            ["prefix"] = node.Prefix,
            ["text"] = node.InnerText
        };

        Dictionary<string, object> attrs = new(StringComparer.Ordinal);
        if (node.Attributes != null)
        {
            foreach (XmlAttribute attr in node.Attributes)
                attrs[attr.Name] = attr.Value;
        }
        result["attributes"] = attrs;

        List<object> children = new();
        foreach (XmlNode child in node.ChildNodes)
            children.Add(NodeToDict(child));
        result["children"] = children;

        return result;
    }

    private static void SetAttribute(XmlElement el, string name, string value, string ns, string prefix)
    {
        if (!string.IsNullOrEmpty(ns) || !string.IsNullOrEmpty(prefix))
        {
            string local = LocalName(name);
            string actualPrefix = !string.IsNullOrEmpty(prefix) ? prefix : Prefix(name);
            XmlAttribute attr = el.OwnerDocument.CreateAttribute(actualPrefix, local, ns);
            attr.Value = value;
            el.SetAttributeNode(attr);
            return;
        }

        el.SetAttribute(name, value);
    }

    private static bool HasAttribute(XmlElement el, string name, string ns)
    {
        return string.IsNullOrEmpty(ns) ? el.HasAttribute(name) : el.HasAttribute(name, ns);
    }

    private static string LocalName(string name)
    {
        return TrySplitQName(name, out _, out string local) ? local : name;
    }

    private static string Prefix(string name)
    {
        return TrySplitQName(name, out string prefix, out _) ? prefix : "";
    }

    private static bool TrySplitQName(string name, out string prefix, out string local)
    {
        int idx = name.IndexOf(':');
        if (idx > 0 && idx < name.Length - 1)
        {
            prefix = name[..idx];
            local = name[(idx + 1)..];
            return true;
        }

        prefix = "";
        local = name;
        return false;
    }

    private static bool BoolArg(List<object> args, int index, bool defaultValue)
    {
        if (args.Count <= index)
            return defaultValue;
        return Convert.ToBoolean(args[index], CultureInfo.InvariantCulture);
    }

    private static void EnsureFileIo(Instruction instr)
    {
        if (!AllowFileIO || !VM.AllowFileIO)
            throw Error(instr, "file I/O is disabled (AllowFileIO=false)");
    }

    private static VMException Error(Instruction instr, string message, Exception? ex = null)
    {
        string fullMessage = ex is null ? $"Runtime error: {message}" : $"Runtime error: {message}: {ex.GetType().Name}: {ex.Message}";
        return new VMException(fullMessage, instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
    }
}

public sealed class XmlDocumentHandle
{
    public XmlDocumentHandle(XmlDocument document, string? path = null)
    {
        Document = document;
        Path = path;
    }

    public XmlDocument Document { get; }

    public string? Path { get; set; }

    public override string ToString() => Document.OuterXml;
}

public sealed class XmlNodeHandle
{
    public XmlNodeHandle(XmlNode node)
    {
        Node = node;
    }

    public XmlNode Node { get; }

    public override string ToString() => Node.OuterXml;
}
