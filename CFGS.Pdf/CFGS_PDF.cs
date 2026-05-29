using CFGS_VM.VMCore;
using CFGS_VM.VMCore.Extensions;
using CFGS_VM.VMCore.Plugin;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using System.Globalization;
using System.Text;

namespace CFGS.Pdf;

public sealed class CFGS_PDF : IVmPlugin
{
    public static bool AllowFileIO { get; set; } = true;

    static CFGS_PDF()
    {
        if (OperatingSystem.IsWindows())
            GlobalFontSettings.UseWindowsFontsUnderWindows = true;
    }

    public void Register(IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics)
    {
        RegisterBuiltins(builtins);
        RegisterDocumentIntrinsics(intrinsics);
        RegisterPageIntrinsics(intrinsics);
    }

    private static void RegisterBuiltins(IBuiltinRegistry builtins)
    {
        builtins.Register(new BuiltinDescriptor("pdf_new", 0, 1, (args, instr) =>
        {
            PdfDocument doc = new();
            if (args.Count >= 1)
                doc.Info.Title = args[0]?.ToString() ?? "";
            return new PdfDocumentHandle(doc);
        }));

        builtins.Register(new BuiltinDescriptor("pdf_load", 1, 2, (args, instr) =>
        {
            EnsureFileIo(instr);
            string path = args[0]?.ToString() ?? "";
            string? password = args.Count >= 2 ? args[1]?.ToString() : null;
            try
            {
                PdfDocument doc = string.IsNullOrEmpty(password)
                    ? PdfReader.Open(path, PdfDocumentOpenMode.Modify)
                    : PdfReader.Open(path, password, PdfDocumentOpenMode.Modify);
                return new PdfDocumentHandle(doc, path);
            }
            catch (Exception ex)
            {
                throw Error(instr, $"pdf_load('{path}') failed", ex);
            }
        }));

        builtins.Register(new BuiltinDescriptor("pdf_concat", 1, 2, (args, instr) =>
        {
            EnsureFileIo(instr);
            List<string> paths = ToStringList(args[0], instr, "paths");
            PdfDocument output = new();
            foreach (string path in paths)
                AppendPdfFile(output, path, instr);

            if (args.Count >= 2 && !string.IsNullOrWhiteSpace(args[1]?.ToString()))
            {
                string outPath = args[1]?.ToString() ?? "";
                SaveDocument(output, outPath, instr);
                return outPath;
            }

            return new PdfDocumentHandle(output);
        }));
    }

    private static void RegisterDocumentIntrinsics(IIntrinsicRegistry intrinsics)
    {
        Type T = typeof(PdfDocumentHandle);

        intrinsics.Register(T, new IntrinsicDescriptor("save", 1, 1, (recv, args, instr) =>
        {
            EnsureFileIo(instr);
            PdfDocumentHandle handle = (PdfDocumentHandle)recv;
            string path = args[0]?.ToString() ?? "";
            SaveDocument(handle.Document, path, instr);
            handle.Path = path;
            return path;
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("close", 0, 0, (recv, args, instr) =>
        {
            ((PdfDocumentHandle)recv).Document.Close();
            return 1;
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("path", 0, 0, (recv, args, instr) => ((PdfDocumentHandle)recv).Path ?? ""));
        intrinsics.Register(T, new IntrinsicDescriptor("page_count", 0, 0, (recv, args, instr) => ((PdfDocumentHandle)recv).Document.PageCount));
        intrinsics.Register(T, new IntrinsicDescriptor("version", 0, 1, (recv, args, instr) =>
        {
            PdfDocument doc = ((PdfDocumentHandle)recv).Document;
            if (args.Count == 0)
                return doc.Version;
            doc.Version = Convert.ToInt32(args[0], CultureInfo.InvariantCulture);
            return recv;
        }));
        intrinsics.Register(T, new IntrinsicDescriptor("is_pdfa", 0, 0, (recv, args, instr) => ((PdfDocumentHandle)recv).Document.IsPdfA));
        intrinsics.Register(T, new IntrinsicDescriptor("set_pdfa", 0, 0, (recv, args, instr) =>
        {
            ((PdfDocumentHandle)recv).Document.SetPdfA();
            return recv;
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("language", 0, 1, (recv, args, instr) =>
        {
            PdfDocument doc = ((PdfDocumentHandle)recv).Document;
            if (args.Count == 0)
                return doc.Language ?? "";
            doc.Language = args[0]?.ToString() ?? "";
            return recv;
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("info", 0, 0, (recv, args, instr) =>
            InfoToDict(((PdfDocumentHandle)recv).Document)));

        intrinsics.Register(T, new IntrinsicDescriptor("set_info", 1, 1, (recv, args, instr) =>
        {
            if (args[0] is not Dictionary<string, object> dict)
                throw Error(instr, "set_info expects a dictionary");
            ApplyInfo(((PdfDocumentHandle)recv).Document, dict);
            return recv;
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("set_metadata_xml", 1, 1, (recv, args, instr) =>
        {
            SetMetadataXml(((PdfDocumentHandle)recv).Document, args[0]?.ToString() ?? "", instr);
            return recv;
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("add_page", 0, 2, (recv, args, instr) =>
        {
            PdfDocument doc = ((PdfDocumentHandle)recv).Document;
            PdfPage page = doc.AddPage();
            if (args.Count >= 1)
                page.Width = new XUnit(Convert.ToDouble(args[0], CultureInfo.InvariantCulture));
            if (args.Count >= 2)
                page.Height = new XUnit(Convert.ToDouble(args[1], CultureInfo.InvariantCulture));
            return new PdfPageHandle(doc, page);
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("page", 1, 1, (recv, args, instr) =>
        {
            PdfDocument doc = ((PdfDocumentHandle)recv).Document;
            int index = NormalizePageIndex(doc, args[0], instr);
            return new PdfPageHandle(doc, doc.Pages[index]);
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("remove_page", 1, 1, (recv, args, instr) =>
        {
            PdfDocument doc = ((PdfDocumentHandle)recv).Document;
            int index = NormalizePageIndex(doc, args[0], instr);
            doc.Pages.Remove(doc.Pages[index]);
            return recv;
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("append_pdf", 1, 1, (recv, args, instr) =>
        {
            EnsureFileIo(instr);
            AppendPdfFile(((PdfDocumentHandle)recv).Document, args[0]?.ToString() ?? "", instr);
            return recv;
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("import_page", 2, 2, (recv, args, instr) =>
        {
            EnsureFileIo(instr);
            PdfDocument target = ((PdfDocumentHandle)recv).Document;
            string path = args[0]?.ToString() ?? "";
            try
            {
                using PdfDocument source = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                int index = NormalizePageIndex(source, args[1], instr);
                PdfPage imported = target.AddPage(source.Pages[index]);
                return new PdfPageHandle(target, imported);
            }
            catch (Exception ex)
            {
                throw Error(instr, $"import_page('{path}') failed", ex);
            }
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("pages_info", 0, 0, (recv, args, instr) =>
        {
            PdfDocument doc = ((PdfDocumentHandle)recv).Document;
            List<object> pages = new();
            for (int i = 0; i < doc.PageCount; i++)
                pages.Add(PageInfo(doc.Pages[i], i));
            return pages;
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("attach_file", 1, 5, (recv, args, instr) =>
        {
            EnsureFileIo(instr);
            string path = args[0]?.ToString() ?? "";
            string fileName = args.Count >= 2 && !string.IsNullOrWhiteSpace(args[1]?.ToString()) ? args[1]!.ToString()! : Path.GetFileName(path);
            string mime = args.Count >= 3 ? args[2]?.ToString() ?? "application/octet-stream" : "application/octet-stream";
            string relationship = args.Count >= 4 ? args[3]?.ToString() ?? "Data" : "Data";
            string description = args.Count >= 5 ? args[4]?.ToString() ?? "" : "";
            byte[] bytes = File.ReadAllBytes(path);
            AddAttachment(((PdfDocumentHandle)recv).Document, fileName, bytes, mime, relationship, description, instr);
            return recv;
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("attach_bytes", 2, 5, (recv, args, instr) =>
        {
            string fileName = args[0]?.ToString() ?? "";
            byte[] bytes = ToBytes(args[1], instr, "content");
            string mime = args.Count >= 3 ? args[2]?.ToString() ?? "application/octet-stream" : "application/octet-stream";
            string relationship = args.Count >= 4 ? args[3]?.ToString() ?? "Data" : "Data";
            string description = args.Count >= 5 ? args[4]?.ToString() ?? "" : "";
            AddAttachment(((PdfDocumentHandle)recv).Document, fileName, bytes, mime, relationship, description, instr);
            return recv;
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("attachments", 0, 0, (recv, args, instr) =>
            ListAttachments(((PdfDocumentHandle)recv).Document)));

        intrinsics.Register(T, new IntrinsicDescriptor("extract_attachment", 2, 2, (recv, args, instr) =>
        {
            EnsureFileIo(instr);
            string name = args[0]?.ToString() ?? "";
            string path = args[1]?.ToString() ?? "";
            byte[]? bytes = FindAttachmentBytes(((PdfDocumentHandle)recv).Document, name);
            if (bytes == null)
                return 0;
            string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllBytes(path, bytes);
            return bytes.Length;
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("encrypt", 2, 3, (recv, args, instr) =>
        {
            PdfDocument doc = ((PdfDocumentHandle)recv).Document;
            doc.SecuritySettings.UserPassword = args[0]?.ToString() ?? "";
            doc.SecuritySettings.OwnerPassword = args[1]?.ToString() ?? "";
            if (args.Count >= 3 && args[2] is Dictionary<string, object> permissions)
                ApplyPermissions(doc, permissions);
            return recv;
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("toString", 0, 0, (recv, args, instr) =>
            $"PdfDocument({((PdfDocumentHandle)recv).Document.PageCount} pages)"));
    }

    private static void RegisterPageIntrinsics(IIntrinsicRegistry intrinsics)
    {
        Type T = typeof(PdfPageHandle);

        intrinsics.Register(T, new IntrinsicDescriptor("index", 0, 0, (recv, args, instr) =>
        {
            PdfPageHandle handle = (PdfPageHandle)recv;
            for (int i = 0; i < handle.Document.PageCount; i++)
            {
                if (ReferenceEquals(handle.Document.Pages[i], handle.Page))
                    return i;
            }
            return -1;
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("size", 0, 0, (recv, args, instr) =>
            PageInfo(((PdfPageHandle)recv).Page, -1)));

        intrinsics.Register(T, new IntrinsicDescriptor("set_size", 2, 2, (recv, args, instr) =>
        {
            PdfPage page = ((PdfPageHandle)recv).Page;
            page.Width = new XUnit(Convert.ToDouble(args[0], CultureInfo.InvariantCulture));
            page.Height = new XUnit(Convert.ToDouble(args[1], CultureInfo.InvariantCulture));
            return recv;
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("rotate", 0, 1, (recv, args, instr) =>
        {
            PdfPage page = ((PdfPageHandle)recv).Page;
            if (args.Count == 0)
                return page.Elements.GetInteger("/Rotate");
            int rotation = Convert.ToInt32(args[0], CultureInfo.InvariantCulture);
            page.Elements.SetInteger("/Rotate", rotation);
            return recv;
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("draw_text", 3, 8, (recv, args, instr) =>
        {
            PdfPage page = ((PdfPageHandle)recv).Page;
            string text = args[0]?.ToString() ?? "";
            double x = ToDouble(args[1]);
            double y = ToDouble(args[2]);
            double size = args.Count >= 4 ? ToDouble(args[3]) : 12;
            string fontName = args.Count >= 5 ? args[4]?.ToString() ?? "Arial" : "Arial";
            string color = args.Count >= 6 ? args[5]?.ToString() ?? "#000000" : "#000000";
            string style = args.Count >= 7 ? args[6]?.ToString() ?? "regular" : "regular";
            bool append = args.Count < 8 || Convert.ToBoolean(args[7], CultureInfo.InvariantCulture);
            using XGraphics gfx = XGraphics.FromPdfPage(page, append ? XGraphicsPdfPageOptions.Append : XGraphicsPdfPageOptions.Prepend);
            XFont font = new(fontName, size, ParseFontStyle(style));
            gfx.DrawString(text, font, new XSolidBrush(ParseColor(color)), x, y);
            return recv;
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("draw_line", 4, 6, (recv, args, instr) =>
        {
            PdfPage page = ((PdfPageHandle)recv).Page;
            string color = args.Count >= 5 ? args[4]?.ToString() ?? "#000000" : "#000000";
            double width = args.Count >= 6 ? ToDouble(args[5]) : 1;
            using XGraphics gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            gfx.DrawLine(new XPen(ParseColor(color), width), ToDouble(args[0]), ToDouble(args[1]), ToDouble(args[2]), ToDouble(args[3]));
            return recv;
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("draw_rect", 4, 6, (recv, args, instr) =>
        {
            PdfPage page = ((PdfPageHandle)recv).Page;
            string color = args.Count >= 5 ? args[4]?.ToString() ?? "#000000" : "#000000";
            double width = args.Count >= 6 ? ToDouble(args[5]) : 1;
            using XGraphics gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            gfx.DrawRectangle(new XPen(ParseColor(color), width), ToDouble(args[0]), ToDouble(args[1]), ToDouble(args[2]), ToDouble(args[3]));
            return recv;
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("fill_rect", 4, 5, (recv, args, instr) =>
        {
            PdfPage page = ((PdfPageHandle)recv).Page;
            string color = args.Count >= 5 ? args[4]?.ToString() ?? "#000000" : "#000000";
            using XGraphics gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            gfx.DrawRectangle(new XSolidBrush(ParseColor(color)), ToDouble(args[0]), ToDouble(args[1]), ToDouble(args[2]), ToDouble(args[3]));
            return recv;
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("draw_image", 3, 5, (recv, args, instr) =>
        {
            EnsureFileIo(instr);
            PdfPage page = ((PdfPageHandle)recv).Page;
            string path = args[0]?.ToString() ?? "";
            using XGraphics gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            using XImage img = XImage.FromFile(path);
            double x = ToDouble(args[1]);
            double y = ToDouble(args[2]);
            if (args.Count >= 5)
                gfx.DrawImage(img, x, y, ToDouble(args[3]), ToDouble(args[4]));
            else
                gfx.DrawImage(img, x, y);
            return recv;
        }));

        intrinsics.Register(T, new IntrinsicDescriptor("toString", 0, 0, (recv, args, instr) =>
        {
            PdfPage page = ((PdfPageHandle)recv).Page;
            return $"PdfPage({page.Width.Point}x{page.Height.Point})";
        }));
    }

    private static void SaveDocument(PdfDocument doc, string path, Instruction instr)
    {
        try
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);
            doc.Save(path);
        }
        catch (Exception ex)
        {
            throw Error(instr, $"PDF save('{path}') failed", ex);
        }
    }

    private static void AppendPdfFile(PdfDocument output, string path, Instruction instr)
    {
        try
        {
            using PdfDocument input = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            for (int i = 0; i < input.PageCount; i++)
                output.AddPage(input.Pages[i]);
        }
        catch (Exception ex)
        {
            throw Error(instr, $"append_pdf('{path}') failed", ex);
        }
    }

    private static Dictionary<string, object> InfoToDict(PdfDocument doc)
    {
        return new Dictionary<string, object>
        {
            ["title"] = doc.Info.Title ?? "",
            ["author"] = doc.Info.Author ?? "",
            ["subject"] = doc.Info.Subject ?? "",
            ["keywords"] = doc.Info.Keywords ?? "",
            ["creator"] = doc.Info.Creator ?? "",
            ["producer"] = doc.Info.Producer ?? "",
            ["creationDate"] = doc.Info.CreationDate.ToString("O", CultureInfo.InvariantCulture),
            ["modificationDate"] = doc.Info.ModificationDate.ToString("O", CultureInfo.InvariantCulture),
            ["pageCount"] = doc.PageCount,
            ["version"] = doc.Version,
            ["isPdfA"] = doc.IsPdfA
        };
    }

    private static void ApplyInfo(PdfDocument doc, Dictionary<string, object> dict)
    {
        if (TryGet(dict, "title", out string title)) doc.Info.Title = title;
        if (TryGet(dict, "author", out string author)) doc.Info.Author = author;
        if (TryGet(dict, "subject", out string subject)) doc.Info.Subject = subject;
        if (TryGet(dict, "keywords", out string keywords)) doc.Info.Keywords = keywords;
        if (TryGet(dict, "creator", out string creator)) doc.Info.Creator = creator;
    }

    private static void SetMetadataXml(PdfDocument doc, string xml, Instruction instr)
    {
        try
        {
            PdfMetadata metadata = new(doc);
            metadata.Elements.SetName("/Type", "/Metadata");
            metadata.Elements.SetName("/Subtype", "/XML");
            metadata.CreateStream(Encoding.UTF8.GetBytes(xml));
            doc.Internals.AddObject(metadata);
            doc.Internals.Catalog.Elements.SetValue("/Metadata", metadata.ReferenceNotNull);
        }
        catch (Exception ex)
        {
            throw Error(instr, "set_metadata_xml failed", ex);
        }
    }

    private static void AddAttachment(PdfDocument doc, string fileName, byte[] bytes, string mime, string relationship, string description, Instruction instr)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw Error(instr, "attachment filename must not be empty");

        try
        {
            PdfDictionary embeddedFile = new(doc);
            embeddedFile.Elements.SetName("/Type", "/EmbeddedFile");
            embeddedFile.Elements.SetName("/Subtype", "/" + EscapePdfName(mime));
            embeddedFile.Elements.SetInteger("/Length", bytes.Length);
            embeddedFile.CreateStream(bytes);
            doc.Internals.AddObject(embeddedFile);

            PdfDictionary ef = new(doc);
            ef.Elements.SetValue("/F", embeddedFile.ReferenceNotNull);
            ef.Elements.SetValue("/UF", embeddedFile.ReferenceNotNull);

            PdfDictionary fileSpec = new(doc);
            fileSpec.Elements.SetName("/Type", "/Filespec");
            fileSpec.Elements.SetString("/F", fileName);
            fileSpec.Elements.SetString("/UF", fileName);
            fileSpec.Elements.SetValue("/EF", ef);
            fileSpec.Elements.SetName("/AFRelationship", "/" + NormalizeRelationship(relationship));
            if (!string.IsNullOrWhiteSpace(description))
                fileSpec.Elements.SetString("/Desc", description);
            doc.Internals.AddObject(fileSpec);

            PdfDictionary names = GetOrCreateCatalogDictionary(doc, "/Names");
            PdfDictionary embeddedFiles = GetOrCreateChildDictionary(doc, names, "/EmbeddedFiles");
            PdfArray namesArray = embeddedFiles.Elements.GetArray("/Names") ?? new PdfArray(doc);
            namesArray.Elements.Add(new PdfString(fileName));
            namesArray.Elements.Add(fileSpec.ReferenceNotNull);
            embeddedFiles.Elements.SetValue("/Names", namesArray);

            PdfArray afArray = doc.Internals.Catalog.Elements.GetArray("/AF") ?? new PdfArray(doc);
            afArray.Elements.Add(fileSpec.ReferenceNotNull);
            doc.Internals.Catalog.Elements.SetValue("/AF", afArray);
        }
        catch (Exception ex)
        {
            throw Error(instr, "attach file failed", ex);
        }
    }

    private static List<object> ListAttachments(PdfDocument doc)
    {
        List<object> result = new();
        PdfArray? names = GetEmbeddedFilesArray(doc);
        if (names == null)
            return result;

        for (int i = 0; i + 1 < names.Elements.Count; i += 2)
        {
            string fileName = names.Elements.GetString(i);
            PdfDictionary? spec = ResolveDictionary(names.Elements[i + 1]);
            PdfDictionary? ef = spec?.Elements.GetDictionary("/EF");
            PdfDictionary? embedded = ef == null ? null : ResolveDictionary(ef.Elements.GetValue("/F"));
            result.Add(new Dictionary<string, object>
            {
                ["name"] = fileName,
                ["description"] = spec?.Elements.GetString("/Desc") ?? "",
                ["relationship"] = TrimName(spec?.Elements.GetValue("/AFRelationship")),
                ["mime"] = TrimName(embedded?.Elements.GetValue("/Subtype")).Replace("#2F", "/", StringComparison.OrdinalIgnoreCase),
                ["size"] = embedded?.Stream?.UnfilteredValue?.Length ?? embedded?.Stream?.Value?.Length ?? 0
            });
        }

        return result;
    }

    private static byte[]? FindAttachmentBytes(PdfDocument doc, string name)
    {
        PdfArray? names = GetEmbeddedFilesArray(doc);
        if (names == null)
            return null;

        for (int i = 0; i + 1 < names.Elements.Count; i += 2)
        {
            string fileName = names.Elements.GetString(i);
            if (!string.Equals(fileName, name, StringComparison.OrdinalIgnoreCase))
                continue;

            PdfDictionary? spec = ResolveDictionary(names.Elements[i + 1]);
            PdfDictionary? ef = spec?.Elements.GetDictionary("/EF");
            PdfDictionary? embedded = ef == null ? null : ResolveDictionary(ef.Elements.GetValue("/F"));
            if (embedded?.Stream == null)
                return null;
            return embedded.Stream.UnfilteredValue ?? embedded.Stream.Value;
        }

        return null;
    }

    private static PdfArray? GetEmbeddedFilesArray(PdfDocument doc)
    {
        PdfDictionary? names = doc.Internals.Catalog.Elements.GetDictionary("/Names");
        PdfDictionary? embeddedFiles = names?.Elements.GetDictionary("/EmbeddedFiles");
        return embeddedFiles?.Elements.GetArray("/Names");
    }

    private static PdfDictionary GetOrCreateCatalogDictionary(PdfDocument doc, string key)
    {
        PdfDictionary? dict = doc.Internals.Catalog.Elements.GetDictionary(key);
        if (dict != null)
            return dict;
        dict = new PdfDictionary(doc);
        doc.Internals.Catalog.Elements.SetValue(key, dict);
        return dict;
    }

    private static PdfDictionary GetOrCreateChildDictionary(PdfDocument doc, PdfDictionary parent, string key)
    {
        PdfDictionary? dict = parent.Elements.GetDictionary(key);
        if (dict != null)
            return dict;
        dict = new PdfDictionary(doc);
        parent.Elements.SetValue(key, dict);
        return dict;
    }

    private static PdfDictionary? ResolveDictionary(PdfItem? item)
    {
        if (item is PdfDictionary dict)
            return dict;
        if (item is PdfReference reference && reference.Value is PdfDictionary refDict)
            return refDict;
        return null;
    }

    private static Dictionary<string, object> PageInfo(PdfPage page, int index)
    {
        Dictionary<string, object> result = new()
        {
            ["width"] = page.Width.Point,
            ["height"] = page.Height.Point,
            ["rotation"] = page.Elements.GetInteger("/Rotate")
        };
        if (index >= 0)
            result["index"] = index;
        return result;
    }

    private static void ApplyPermissions(PdfDocument doc, Dictionary<string, object> permissions)
    {
        if (TryGetBool(permissions, "print", out bool print)) doc.SecuritySettings.PermitPrint = print;
        if (TryGetBool(permissions, "modify", out bool modify)) doc.SecuritySettings.PermitModifyDocument = modify;
        if (TryGetBool(permissions, "extract", out bool extract)) doc.SecuritySettings.PermitExtractContent = extract;
        if (TryGetBool(permissions, "annotations", out bool annotations)) doc.SecuritySettings.PermitAnnotations = annotations;
        if (TryGetBool(permissions, "forms", out bool forms)) doc.SecuritySettings.PermitFormsFill = forms;
        if (TryGetBool(permissions, "assemble", out bool assemble)) doc.SecuritySettings.PermitAssembleDocument = assemble;
        if (TryGetBool(permissions, "full_quality_print", out bool fullQualityPrint)) doc.SecuritySettings.PermitFullQualityPrint = fullQualityPrint;
    }

    private static int NormalizePageIndex(PdfDocument doc, object? value, Instruction instr)
    {
        int index = Convert.ToInt32(value, CultureInfo.InvariantCulture);
        if (index < 0)
            index += doc.PageCount;
        if (index < 0 || index >= doc.PageCount)
            throw Error(instr, $"page index {index} is outside 0..{doc.PageCount - 1}");
        return index;
    }

    private static List<string> ToStringList(object? value, Instruction instr, string name)
    {
        if (value is not List<object> list)
            throw Error(instr, $"{name} must be an array");
        return list.Select(v => v?.ToString() ?? "").ToList();
    }

    private static byte[] ToBytes(object? value, Instruction instr, string name)
    {
        if (value is string s)
            return Encoding.UTF8.GetBytes(s);
        if (value is byte[] bytes)
            return bytes;
        if (value is List<object> list)
        {
            byte[] result = new byte[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                int b = Convert.ToInt32(list[i], CultureInfo.InvariantCulture);
                if (b is < 0 or > 255)
                    throw Error(instr, $"{name}[{i}] is outside byte range 0..255");
                result[i] = (byte)b;
            }
            return result;
        }
        throw Error(instr, $"{name} must be string or byte array");
    }

    private static XFontStyleEx ParseFontStyle(string value)
    {
        string normalized = value.Trim().ToLowerInvariant();
        bool bold = normalized.Contains("bold", StringComparison.Ordinal);
        bool italic = normalized.Contains("italic", StringComparison.Ordinal) || normalized.Contains("oblique", StringComparison.Ordinal);
        if (bold && italic) return XFontStyleEx.BoldItalic;
        if (bold) return XFontStyleEx.Bold;
        if (italic) return XFontStyleEx.Italic;
        return XFontStyleEx.Regular;
    }

    private static XColor ParseColor(string value)
    {
        string s = value.Trim();
        if (s.StartsWith("#", StringComparison.Ordinal))
            s = s[1..];
        if (s.Length == 6 && int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            return XColor.FromArgb((rgb >> 16) & 0xff, (rgb >> 8) & 0xff, rgb & 0xff);
        if (s.Length == 8 && int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int argb))
            return XColor.FromArgb((argb >> 24) & 0xff, (argb >> 16) & 0xff, (argb >> 8) & 0xff, argb & 0xff);
        return XColors.Black;
    }

    private static double ToDouble(object? value) => Convert.ToDouble(value, CultureInfo.InvariantCulture);

    private static string EscapePdfName(string value)
    {
        StringBuilder sb = new();
        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_' or '.')
                sb.Append(c);
            else
                sb.Append('#').Append(((int)c).ToString("X2", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    private static string NormalizeRelationship(string relationship)
    {
        string r = relationship.Trim();
        return r switch
        {
            "Source" or "Data" or "Alternative" or "Supplement" or "Unspecified" => r,
            _ => "Data"
        };
    }

    private static string TrimName(PdfItem? item)
    {
        string raw = item?.ToString() ?? "";
        return raw.StartsWith("/", StringComparison.Ordinal) ? raw[1..] : raw;
    }

    private static bool TryGet(Dictionary<string, object> dict, string key, out string value)
    {
        if (dict.TryGetValue(key, out object? raw))
        {
            value = raw?.ToString() ?? "";
            return true;
        }
        value = "";
        return false;
    }

    private static bool TryGetBool(Dictionary<string, object> dict, string key, out bool value)
    {
        if (dict.TryGetValue(key, out object? raw))
        {
            value = Convert.ToBoolean(raw, CultureInfo.InvariantCulture);
            return true;
        }
        value = false;
        return false;
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

public sealed class PdfDocumentHandle
{
    public PdfDocumentHandle(PdfDocument document, string? path = null)
    {
        Document = document;
        Path = path;
    }

    public PdfDocument Document { get; }

    public string? Path { get; set; }

    public override string ToString() => $"PdfDocument({Document.PageCount} pages)";
}

public sealed class PdfPageHandle
{
    public PdfPageHandle(PdfDocument document, PdfPage page)
    {
        Document = document;
        Page = page;
    }

    public PdfDocument Document { get; }

    public PdfPage Page { get; }

    public override string ToString() => $"PdfPage({Page.Width.Point}x{Page.Height.Point})";
}
