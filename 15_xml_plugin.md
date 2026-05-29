# XML Plugin

## Core Idea

The XML plugin is loaded through a normal DLL import.

```cfs
import "dist/Debug/net10.0/CFGS.StandardLibrary.dll";
import "dist/Debug/net10.0/CFGS.Xml.dll";
```

It exposes XML documents and XML nodes as handles. That keeps the full XML model available, including attributes, namespaces, comments, CDATA, processing instructions, and XPath.

## Builtins

- `xml_parse(text)`
- `xml_load(path)`
- `xml_new(rootName = optional, namespaceUri = optional, prefix = optional)`
- `xml_escape(text)`
- `xml_unescape(text)`

`xml_load` and `save` use file I O and respect the runtime file I O switch.

## Document Intrinsics

An `XmlDocumentHandle` supports:

- `root()`
- `path()`
- `to_string(indented = true, omitDeclaration = false)`
- `save(path, indented = true, omitDeclaration = false)`
- `declaration(version = optional, encoding = optional, standalone = optional)`
- `create_element(name, text = optional, namespaceUri = optional, prefix = optional)`
- `create_text(text)`
- `create_cdata(text)`
- `create_comment(text)`
- `create_pi(target, data)`
- `append(node)`
- `select(xpath, namespaces = optional)`
- `select_all(xpath, namespaces = optional)`
- `to_dict()`

## Node Intrinsics

An `XmlNodeHandle` supports:

- `name()`, `local_name()`, `namespace_uri()`, `prefix(value = optional)`, `node_type()`
- `text()`, `set_text(value)`, `value(value = optional)`
- `inner_xml(value = optional)`, `outer_xml()`
- `attr(name, default = optional, namespaceUri = optional)`
- `set_attr(name, value, namespaceUri = optional, prefix = optional)`
- `has_attr(name, namespaceUri = optional)`, `remove_attr(name, namespaceUri = optional)`, `attrs()`
- `children()`, `elements(name = optional)`, `parent()`, `owner_document()`
- `append(node)`, `prepend(node)`, `before(node)`, `after(node)`, `remove()`, `clear()`
- `add_element(name, text = optional, namespaceUri = optional, prefix = optional)`
- `clone(deep = true)`
- `select(xpath, namespaces = optional)`, `select_all(xpath, namespaces = optional)`
- `to_dict()`

## Example

```cfs
import "dist/Debug/net10.0/CFGS.StandardLibrary.dll";
import "dist/Debug/net10.0/CFGS.Xml.dll";

var doc = xml_new("config");
doc.declaration("1.0", "utf-8");

var root = doc.root();
root.set_attr("version", "1");

var db = root.add_element("database");
db.add_element("host", "localhost");
db.add_element("port", "1433");

var host = doc.select("/config/database/host");
print(host.text());

host.set_text("127.0.0.1");
print(doc.to_string(true));
```

## Namespaces and XPath

XPath namespace prefixes are passed as a dictionary.

```cfs
var doc = xml_parse("<x:root xmlns:x=\"urn:test\"><x:item id=\"1\" /></x:root>");
var item = doc.select("/x:root/x:item", {"x": "urn:test"});
print(item.attr("id"));
```

## Dictionary Shape

`to_dict()` returns a plain CFGS dictionary with:

- `type`
- `name`
- `localName`
- `namespaceUri`
- `prefix`
- `text`
- `attributes`
- `children`
