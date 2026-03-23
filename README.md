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

## Complete Feature Index

The following topics are covered completely across the English documentation set.

- Language basics. Comments. Literals. Number formats. Strings. Chars. Booleans. `null`.
- Variables and constants. `var`. `const`. Scopes. Shadowing. Top level rules.
- Data types. Arrays. Dictionaries. Strings. Class instances. Enums. DateTime. DirectoryInfo. FileHandle. Exception objects. Tasks.
- Operators. Arithmetic. Comparisons. Logic. Bitwise operators. Power. Increment. Decrement. Compound assignments. Ternary. Null coalescing.
- Data syntax. Array literals. Dictionary literals. Dot access. Index access. Slicing. Slice replacement. Push syntax. Delete syntax.
- Control flow. `if`. `else`. `while`. `do while`. `for`. `foreach`. `break`. `continue`.
- Error handling. `try`. `catch`. `finally`. `throw`.
- Functions. Function declarations. Function expressions. Closures. Return values. Default parameters. Named arguments. Rest parameters. Spread arguments. Destructuring parameters.
- Pattern matching. `match` as a statement. `match` as an expression. Guards. Wildcards. Literal patterns. Array patterns. Dictionary patterns. `var` bindings.
- Destructuring. Declarations. Assignments. Parameters. `foreach` patterns.
- `out` blocks as an expression form.
- OOP. Classes. Constructors. `init`. Object initialization. Instance members. Static members. Visibility. Inheritance. `super`. `type`. `this`. `outer`. Nested classes. Enums inside classes. Override rules.
- Namespaces. Qualified names. Multiple declarations. Name resolution.
- Modules. `export`. Bare imports. Named imports. Alias imports. Namespace imports. Single symbol imports. File imports. URL imports. DLL imports. Header rules. Import resolution. Cycles.
- Async model. `async func`. `await`. `yield`. Top level await inside expressions. Hot start behavior. Await on lists and dictionaries. Await on Task and ValueTask values returned by plugins.
- Standard library. All builtins. All string, array, dictionary, DateTime, DirectoryInfo, FileHandle, exception, and task intrinsics.
- Official repository plugins. Standard library. HTTP. SQL. Crypto with hashing, HMAC, PBKDF2, AES GCM, RSA, ECDSA, Ed25519, X25519, JWT, HOTP and TOTP, and X509.
- Plugin development. `IVmPlugin`. `BuiltinDescriptor`. `IntrinsicDescriptor`. Attribute based registration. `BuiltinAttribute`. `IntrinsicAttribute`. `SmartAwait`. `NonBlocking`. Packaging and importing.

## Smallest Useful Example

```cfs
import "dist/Debug/net10.0/CFGS.StandardLibrary.dll";

func main() {
    print("Hello from CFGS");
}

main();
```

If you want to jump straight into the plugin topics, use these three pages.

- [Using the HTTP and SQL Plugins](10_using_http_and_sql_plugins.md)
- [Creating Plugins](11_creating_plugins.md)
- [Crypto Plugin](12_crypto_plugin.md)
