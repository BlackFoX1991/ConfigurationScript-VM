# Native .NET Interop Samples

Build the repository first.

```bat
dotnet build -v minimal CFGS_VM.sln
```

Run from the repository root.

```bat
dotnet dist\Debug\net10.0\CFGS_VM.dll CFGS_NE\Samples\Interop_Tests\native_basic.cfs
dotnet dist\Debug\net10.0\CFGS_VM.dll CFGS_NE\Samples\Interop_Tests\native_static_and_enums.cfs
```

`native_delegate_event_optional.cfs` uses the edgecase helper assembly `CFGS.EdgeAwaitables.dll`. Build that helper project first.

```bat
dotnet build CFGS_NE\_edgecases\EdgeAwaitablesPlugin\CFGS.EdgeAwaitables.csproj
dotnet dist\Debug\net10.0\CFGS_VM.dll CFGS_NE\Samples\Interop_Tests\native_delegate_event_optional.cfs
```

Covered features:

- `import native "Assembly.dll" as Alias`
- namespace and type traversal with dot syntax
- constructors through `.new(...)`
- public properties, methods, and static members
- CLR indexers through bracket syntax
- named arguments, optional parameters, and `params` arrays
- CFGS functions converted to CLR delegates
- CLR event `add` and `remove`
