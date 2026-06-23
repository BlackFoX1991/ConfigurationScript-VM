

<p align="center" width="100%">
    <img 
    src="https://raw.githubusercontent.com/BlackFoX1991/ConfigurationScript-VM/fcdee09eda6c54a4faa2f6f3e0ca402cd0339a3d/CFGS_NE/assets/CFGS_LOGO.png"
    alt="CFGS Logo"
    style="width: 640px; height: auto;">
</p>

# ConfigurationScript Documentation

If you are opening CFGS for the first time, this is the best reading order.

1. [Getting Started and Running Scripts](01_getting_started_and_running.md)
2. [Language and Data](02_language_and_data.md)
3. [Control Flow and Errors](03_control_flow_and_errors.md)
4. [Functions and Calls](04_functions_and_calls.md)
5. [Match, Destructuring, and Out](05_match_destructuring_and_out.md)
6. [Classes, Enums, Namespaces, and Inheritance](06_classes_enums_namespaces_and_inheritance.md)
7. [Modules, Imports, and Exports](07_modules_imports_and_exports.md)
8. [Async, Await, and Yield](08_async_await_and_yield.md)
9. [Standard Library](09_standard_library.md)
10. [Using the HTTP and SQL Plugins](10_using_http_and_sql_plugins.md)
11. [Creating Plugins](11_creating_plugins.md)
12. [Crypto Plugin](12_crypto_plugin.md)
13. [Visual Studio Code and LSP](13_visual_studio_code_and_lsp.md)
14. [Internal Architecture](14_internal_architecture.md)
15. [XML Plugin](15_xml_plugin.md)
16. [PDF Plugin](16_pdf_plugin.md)
17. [Debugging CFGS Scripts](17_debugging.md)
18. [CFGS Language Specification](18_language_specification.md)
19. [Diagnostics and Error Codes](19_diagnostics.md)
20. [Package and Dependency Management](20_package_dependency_management.md)
21. [Native .NET Interop](21_native_dotnet_interop.md)
22. [Embedding CFGS in .NET Applications](22_embedding_api.md)
23. [Practical Feature Samples](CFGS_NE/Samples/README.md)

## Complete Feature Index

The following topics are covered completely across the English documentation set.

- Language basics. Comments. Literals. Number formats. Strings. Chars. Booleans. `null`.
- Variables and constants. `var`. `const`. Scopes. Shadowing. Top level rules.
- Data types. Arrays. Dictionaries. Strings. Class instances. Enums. DateTime. DirectoryInfo. FileHandle. BinaryFileHandle. Byte arrays. Exception objects. Tasks.
- Operators. Arithmetic. Comparisons. Logic. Bitwise operators. Power. Increment. Decrement. Compound assignments. Ternary. Null coalescing.
- Data syntax. Array literals. Dictionary literals. Dot access. Index access. Slicing. Slice replacement. Push syntax. Delete syntax.
- Control flow. `if`. `else`. `while`. `do while`. `for`. `foreach`. `break`. `continue`.
- Error handling. `try`. `catch`. `finally`. `throw`.
- Functions. Function declarations. Function expressions. Closures. Return values. Default parameters. Named arguments. Rest parameters. Spread arguments. Destructuring parameters.
- Pattern matching. `match` as a statement. `match` as an expression. Guards. Wildcards. Literal patterns. Array patterns. Dictionary patterns. `var` bindings.
- Destructuring. Declarations. Assignments. Parameters. `foreach` patterns.
- `out` blocks as an expression form.
- OOP. Classes. Constructors. `init`. Object initialization. Fields. Properties with `get`/`set`/`init`. Auto-properties. Mixed auto/manual accessors. `field` backing symbol. Instance members. Static members. Visibility. Inheritance. `super`. `type`. `this`. `outer`. Nested classes. Enums inside classes. Override rules. Interfaces with instance/static contracts, default methods, structural checks, indexer contracts, and reflection builtins.
- Namespaces. Qualified names. Multiple declarations. `use` directives. Name resolution.
- Modules. `export`. Bare imports. Named imports. Alias imports. Namespace imports. Single symbol imports. File imports. URL imports. DLL imports. Header rules. Import resolution. Cycles.
- Async model. `async func`. `await`. `yield`. Top level await inside expressions. Hot start behavior. Await on lists and dictionaries. Await on Task and ValueTask values returned by plugins.
- Standard library. All builtins. Native namespace roots such as `encoding`, `regex`, `json`, `math`, `console`, `fs`, and `file`. All string, array, dictionary, DateTime, DirectoryInfo, FileHandle, BinaryFileHandle, exception, and task intrinsics.
- Official repository plugins. Standard library. HTTP. SQL. XML with DOM editing, XPath, namespaces, serialization, PDF with document creation, page editing, metadata, attachments, and Crypto with hashing, HMAC, PBKDF2, AES GCM, RSA, ECDSA, Ed25519, X25519, JWT, HOTP and TOTP, and X509.
- Plugin development. `IVmPlugin`. `INamespaceRegistry`. `NamespaceDescriptor`. `NativeCallOptions`. `BuiltinDescriptor`. `IntrinsicDescriptor`. Attribute based registration. `BuiltinAttribute`. `IntrinsicAttribute`. Packaging and importing.
- Package management. `cfgs.json`. `cfgs.lock.json`. Local plugin install. Restore into `.cfgs/plugins`. Reproducible plugin hashes.
- Native .NET interop. `import native`. Namespace and type access. Constructors through `.new`. Static and instance member access. CFB-safe assembly references.
- Embedding API. Referencing the CFGS assembly from .NET, compiling source, files, and CFB artifacts, running top-level code, reading and writing globals, injecting host services, invoking functions and script members, constructing script classes, typed host conversions, permission controls, and preloading plugins.
- Tooling. `-check`, `-lint`, and `-format` for syntax validation, style and semantic lint diagnostics, `--fix`, JSON output, CI checks, manifest-driven tooling defaults, and automatic indentation.
- Practical samples. Runnable feature examples under `CFGS_NE/Samples` for language syntax, modules, OOP, interfaces, async, standard library namespaces, plugins, native interop, embedding, REPL, CFB, linting, and formatting workflows.
- Language specification. Normative processing model, lexical rules, scoping, values, expressions, OOP, modules, async, errors, CLI execution, CFB artifacts, debugging, and compatibility rules.

## Smallest Useful Example

```cfs

func main() {
    print("Hello from CFGS");
}
```

If you want to jump straight into the plugin topics, use these three pages.

- [Using the HTTP and SQL Plugins](10_using_http_and_sql_plugins.md)
- [Creating Plugins](11_creating_plugins.md)
- [Crypto Plugin](12_crypto_plugin.md)
- [XML Plugin](15_xml_plugin.md)
- [PDF Plugin](16_pdf_plugin.md)
