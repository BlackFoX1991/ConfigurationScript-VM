# PDF Plugin

## Core Idea

The PDF plugin is loaded through a normal DLL import.

```cfs
import "dist/Debug/net10.0/CFGS.StandardLibrary.dll";
import "dist/Debug/net10.0/CFGS.Pdf.dll";
```

It is based on PDFsharp and exposes documents and pages as handles. The plugin focuses on practical document generation, page manipulation, metadata, encryption flags, and embedded files for workflows such as ZUGFeRD or Factur-X.

## Builtins

- `pdf_new(title = optional)`
- `pdf_load(path, password = optional)`
- `pdf_concat(paths, outputPath = optional)`

`pdf_concat` accepts an array of PDF paths. If `outputPath` is omitted, it returns a `PdfDocumentHandle`; otherwise it saves the merged PDF and returns the path.

## Document Intrinsics

A `PdfDocumentHandle` supports:

- `save(path)`
- `close()`
- `path()`
- `page_count()`
- `version(value = optional)`
- `is_pdfa()`
- `set_pdfa()`
- `language(value = optional)`
- `info()`
- `set_info(dictionary)`
- `set_metadata_xml(xml)`
- `add_page(width = optional, height = optional)`
- `page(index)`
- `remove_page(index)`
- `append_pdf(path)`
- `import_page(path, index)`
- `pages_info()`
- `attach_file(path, filename = optional, mime = optional, relationship = optional, description = optional)`
- `attach_bytes(filename, content, mime = optional, relationship = optional, description = optional)`
- `attachments()`
- `extract_attachment(filename, outputPath)`
- `encrypt(userPassword, ownerPassword, permissions = optional)`

Page indexes are zero based. Negative indexes count from the end.

## Page Intrinsics

A `PdfPageHandle` supports:

- `index()`
- `size()`
- `set_size(width, height)`
- `rotate(value = optional)`
- `draw_text(text, x, y, size = optional, fontName = optional, color = optional, style = optional, append = optional)`
- `draw_line(x1, y1, x2, y2, color = optional, width = optional)`
- `draw_rect(x, y, width, height, color = optional, strokeWidth = optional)`
- `fill_rect(x, y, width, height, color = optional)`
- `draw_image(path, x, y, width = optional, height = optional)`

Colors use hex strings such as `"#111827"` or `"#FF111827"`.

## ZUGFeRD / Factur-X XML Attachment

For a ZUGFeRD-style XML attachment, attach the XML with relationship `Data`.

```cfs
import "dist/Debug/net10.0/CFGS.StandardLibrary.dll";
import "dist/Debug/net10.0/CFGS.Xml.dll";
import "dist/Debug/net10.0/CFGS.Pdf.dll";

var xml = xml_new("rsm:CrossIndustryInvoice", "urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100", "rsm");
var pdf = pdf_new("Invoice");

var page = pdf.add_page(595, 842);
page.draw_text("Invoice", 72, 96, 18, "Arial", "#111827", "bold");

pdf.attach_bytes("factur-x.xml", xml.to_string(true), "application/xml", "Data", "Structured invoice data");
pdf.save("invoice.pdf");
```

For production ZUGFeRD/PDF-A-3 files, validate the output with veraPDF or another PDF/A validator. The plugin can create the embedded-file and associated-file PDF structures, but PDF/A conformance is still a validator-level requirement.

## Attachments

```cfs
var doc = pdf_load("invoice.pdf");
var files = doc.attachments();
foreach (var file in files) {
    print(file["name"]);
    print(file["mime"]);
}

doc.extract_attachment("factur-x.xml", "factur-x.xml");
```

`attachments()` returns dictionaries with:

- `name`
- `description`
- `relationship`
- `mime`
- `size`

## Metadata

```cfs
var doc = pdf_new();
doc.set_info({
    "title": "Report",
    "author": "CFGS",
    "subject": "Generated PDF",
    "keywords": "pdf,cfgs"
});

print(doc.info()["title"]);
```

`set_metadata_xml(xml)` writes an XMP metadata stream. Use this for advanced PDF/A and invoice profile metadata when you need explicit standards metadata.
