using System.Text;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using CFGS_VM.Analytic;
using CFGS_VM.Analytic.Core;
using CFGS_VM.Analytic.Ex;
using CFGS_VM.Analytic.Lowering;
using CFGS_VM.Analytic.Modules;
using CFGS_VM.Analytic.Tree;
using CFGS_VM.Tooling;
using CFGS_VM.VMCore;
using CFGS_VM.VMCore.Extensions;
using CFGS_VM.VMCore.Plugin;

namespace CFGS.Lsp;

internal sealed class CfgsAnalyzer
{
    private static readonly HashSet<string> NonSymbolKeywords = new(StringComparer.Ordinal)
    {
        "var", "const", "if", "else", "delete", "while", "do", "for", "foreach", "in", "is",
        "break", "continue", "func", "return", "match", "case", "default", "try", "catch",
        "finally", "throw", "class", "interface", "enum", "new", "null", "true", "false", "import",
        "export", "namespace", "from", "as", "static", "public", "private", "protected",
        "async", "await", "yield", "out", "using", "defer"
    };

    public static readonly string[] SemanticTokenTypes =
    [
        "namespace",
        "class",
        "interface",
        "enum",
        "enumMember",
        "function",
        "method",
        "parameter",
        "variable"
    ];

    public static readonly string[] SemanticTokenModifiers =
    [
        "declaration",
        "readonly",
        "static",
        "async",
        "defaultLibrary"
    ];

    private static readonly string[] Keywords =
    [
        "var", "const", "if", "else", "delete", "while", "do", "for", "foreach", "in", "is",
        "break", "continue", "func", "return", "match", "case", "default", "try", "catch",
        "finally", "throw", "class", "interface", "enum", "new", "null", "true", "false", "import",
        "export", "namespace", "from", "as", "static", "public", "private", "protected",
        "async", "await", "yield", "out", "using", "defer"
    ];

    private static readonly CfgsCompletionItem[] StandardLibraryCompletions =
    [
        new("encoding", 9, "standard library namespace"),
        new("regex", 9, "standard library namespace"),
        new("json", 9, "standard library namespace"),
        new("math", 9, "standard library namespace"),
        new("console", 9, "standard library namespace"),
        new("fs", 9, "standard library namespace"),
        new("file", 9, "standard library namespace"),
        new("task", 9, "standard library namespace"),
        new("textToBytes", 3, "builtin: textToBytes(text, encoding = \"utf-8\")"),
        new("bytesToText", 3, "builtin: bytesToText(bytes, encoding = \"utf-8\")"),
        new("encodeText", 3, "builtin: encodeText(text, encoding = \"utf-8\")"),
        new("decodeText", 3, "builtin: decodeText(bytes, encoding = \"utf-8\")"),
        new("convertEncoding", 3, "builtin: convertEncoding(bytes, fromEncoding, toEncoding)"),
        new("hasEncoding", 3, "builtin: hasEncoding(nameOrCodePage)"),
        new("encodingInfo", 3, "builtin: encodingInfo(nameOrCodePage)"),
        new("encodingNames", 3, "builtin: encodingNames()"),
        new("encodings", 3, "builtin: encodings()"),
        new("regex_match", 3, "builtin: regex_match(input, pattern)"),
        new("regex_matches", 3, "builtin: regex_matches(input, pattern)"),
        new("regex_replace", 3, "builtin: regex_replace(input, pattern, replacement)"),
        new("regex_test", 3, "builtin: regex_test(input, pattern)"),
        new("regex_split", 3, "builtin: regex_split(input, pattern)"),
        new("fromjson", 3, "builtin: fromjson(text)"),
        new("tojson", 3, "builtin: tojson(value)"),
        new("abs", 3, "builtin: abs(x)"),
        new("rand", 3, "builtin: rand(seed, min, max)"),
        new("floor", 3, "builtin: floor(x)"),
        new("ceil", 3, "builtin: ceil(x)"),
        new("round", 3, "builtin: round(x, digits = optional)"),
        new("min", 3, "builtin: min(a, b)"),
        new("max", 3, "builtin: max(a, b)"),
        new("sqrt", 3, "builtin: sqrt(x)"),
        new("pow", 3, "builtin: pow(base, exp)"),
        new("log", 3, "builtin: log(x, base = optional)"),
        new("log10", 3, "builtin: log10(x)"),
        new("sin", 3, "builtin: sin(x)"),
        new("cos", 3, "builtin: cos(x)"),
        new("tan", 3, "builtin: tan(x)"),
        new("sign", 3, "builtin: sign(x)"),
        new("trunc", 3, "builtin: trunc(x)"),
        new("PI", 21, "builtin constant"),
        new("E", 21, "builtin constant"),
        new("print", 3, "builtin: print(value)"),
        new("put", 3, "builtin: put(value)"),
        new("clear", 3, "builtin: clear()"),
        new("getl", 3, "builtin: getl()"),
        new("getc", 3, "builtin: getc()"),
        new("getlAsync", 3, "builtin: getlAsync()"),
        new("getcAsync", 3, "builtin: getcAsync()"),
        new("fopen", 3, "builtin: fopen(path, mode)"),
        new("fbopen", 3, "builtin: fbopen(path, mode)"),
        new("fexist", 3, "builtin: fexist(path)"),
        new("fcopy", 3, "builtin: fcopy(source, dest, overwrite = false)"),
        new("fmove", 3, "builtin: fmove(source, dest, overwrite = false)"),
        new("fdelete", 3, "builtin: fdelete(path)"),
        new("readAllBytes", 3, "builtin: readAllBytes(path)"),
        new("readAllBytesAsync", 3, "builtin: readAllBytesAsync(path)"),
        new("writeAllBytes", 3, "builtin: writeAllBytes(path, bytes)"),
        new("readTextAsync", 3, "builtin: readTextAsync(path)"),
        new("writeTextAsync", 3, "builtin: writeTextAsync(path, text)"),
        new("appendTextAsync", 3, "builtin: appendTextAsync(path, text)"),
        new("fileSize", 3, "builtin: fileSize(path)"),
        new("fileLastWriteTime", 3, "builtin: fileLastWriteTime(path)"),
        new("fileLastWriteTimeUtc", 3, "builtin: fileLastWriteTimeUtc(path)"),
        new("set_workspace", 3, "builtin: set_workspace(path)"),
        new("get_workspace", 3, "builtin: get_workspace()"),
        new("getDirectory", 3, "builtin: getDirectory(path)"),
        new("DirectoryInfo", 3, "builtin: DirectoryInfo(path)")
    ];

    private static readonly IReadOnlyDictionary<string, CfgsCompletionItem[]> StandardLibraryNamespaceMembers =
        new Dictionary<string, CfgsCompletionItem[]>(StringComparer.Ordinal)
        {
            ["encoding"] =
            [
                new("encode", 2, "encoding.encode(text, encoding = \"utf-8\")"),
                new("decode", 2, "encoding.decode(bytes, encoding = \"utf-8\")"),
                new("convert", 2, "encoding.convert(bytes, fromEncoding, toEncoding)"),
                new("has", 2, "encoding.has(nameOrCodePage)"),
                new("info", 2, "encoding.info(nameOrCodePage)"),
                new("names", 2, "encoding.names()"),
                new("all", 2, "encoding.all()"),
                new("textToBytes", 2, "encoding.textToBytes(text, encoding = \"utf-8\")"),
                new("bytesToText", 2, "encoding.bytesToText(bytes, encoding = \"utf-8\")"),
                new("encodeText", 2, "encoding.encodeText(text, encoding = \"utf-8\")"),
                new("decodeText", 2, "encoding.decodeText(bytes, encoding = \"utf-8\")"),
                new("convertEncoding", 2, "encoding.convertEncoding(bytes, fromEncoding, toEncoding)"),
                new("hasEncoding", 2, "encoding.hasEncoding(nameOrCodePage)"),
                new("encodingInfo", 2, "encoding.encodingInfo(nameOrCodePage)"),
                new("encodingNames", 2, "encoding.encodingNames()"),
                new("encodings", 2, "encoding.encodings()")
            ],
            ["regex"] =
            [
                new("match", 2, "regex.match(input, pattern)"),
                new("matches", 2, "regex.matches(input, pattern)"),
                new("replace", 2, "regex.replace(input, pattern, replacement)"),
                new("test", 2, "regex.test(input, pattern)"),
                new("split", 2, "regex.split(input, pattern)"),
                new("regex_match", 2, "regex.regex_match(input, pattern)"),
                new("regex_matches", 2, "regex.regex_matches(input, pattern)"),
                new("regex_replace", 2, "regex.regex_replace(input, pattern, replacement)"),
                new("regex_test", 2, "regex.regex_test(input, pattern)"),
                new("regex_split", 2, "regex.regex_split(input, pattern)")
            ],
            ["json"] =
            [
                new("parse", 2, "json.parse(text)"),
                new("stringify", 2, "json.stringify(value)"),
                new("from", 2, "json.from(text)"),
                new("to", 2, "json.to(value)"),
                new("fromjson", 2, "json.fromjson(text)"),
                new("tojson", 2, "json.tojson(value)")
            ],
            ["math"] =
            [
                new("abs", 2, "math.abs(x)"),
                new("rand", 2, "math.rand(seed, min, max)"),
                new("floor", 2, "math.floor(x)"),
                new("ceil", 2, "math.ceil(x)"),
                new("round", 2, "math.round(x, digits = optional)"),
                new("min", 2, "math.min(a, b)"),
                new("max", 2, "math.max(a, b)"),
                new("sqrt", 2, "math.sqrt(x)"),
                new("pow", 2, "math.pow(base, exp)"),
                new("log", 2, "math.log(x, base = optional)"),
                new("log10", 2, "math.log10(x)"),
                new("sin", 2, "math.sin(x)"),
                new("cos", 2, "math.cos(x)"),
                new("tan", 2, "math.tan(x)"),
                new("sign", 2, "math.sign(x)"),
                new("trunc", 2, "math.trunc(x)"),
                new("pi", 2, "math.pi()"),
                new("PI", 2, "math.PI()"),
                new("e", 2, "math.e()"),
                new("E", 2, "math.E()")
            ],
            ["console"] =
            [
                new("print", 2, "console.print(value)"),
                new("put", 2, "console.put(value)"),
                new("clear", 2, "console.clear()"),
                new("getl", 2, "console.getl()"),
                new("getc", 2, "console.getc()"),
                new("readLine", 2, "console.readLine()"),
                new("readChar", 2, "console.readChar()"),
                new("getlAsync", 2, "console.getlAsync()"),
                new("getcAsync", 2, "console.getcAsync()"),
                new("readLineAsync", 2, "console.readLineAsync()"),
                new("readCharAsync", 2, "console.readCharAsync()")
            ],
            ["fs"] =
            [
                new("open", 2, "fs.open(path, mode)"),
                new("fopen", 2, "fs.fopen(path, mode)"),
                new("openBinary", 2, "fs.openBinary(path, mode)"),
                new("fbopen", 2, "fs.fbopen(path, mode)"),
                new("exists", 2, "fs.exists(path)"),
                new("fexist", 2, "fs.fexist(path)"),
                new("copy", 2, "fs.copy(source, dest, overwrite = false)"),
                new("fcopy", 2, "fs.fcopy(source, dest, overwrite = false)"),
                new("move", 2, "fs.move(source, dest, overwrite = false)"),
                new("fmove", 2, "fs.fmove(source, dest, overwrite = false)"),
                new("delete", 2, "fs.delete(path)"),
                new("fdelete", 2, "fs.fdelete(path)"),
                new("readAllBytes", 2, "fs.readAllBytes(path)"),
                new("readAllBytesAsync", 2, "fs.readAllBytesAsync(path)"),
                new("writeAllBytes", 2, "fs.writeAllBytes(path, bytes)"),
                new("readTextAsync", 2, "fs.readTextAsync(path)"),
                new("writeTextAsync", 2, "fs.writeTextAsync(path, text)"),
                new("appendTextAsync", 2, "fs.appendTextAsync(path, text)"),
                new("size", 2, "fs.size(path)"),
                new("fileSize", 2, "fs.fileSize(path)"),
                new("lastWriteTime", 2, "fs.lastWriteTime(path)"),
                new("fileLastWriteTime", 2, "fs.fileLastWriteTime(path)"),
                new("lastWriteTimeUtc", 2, "fs.lastWriteTimeUtc(path)"),
                new("fileLastWriteTimeUtc", 2, "fs.fileLastWriteTimeUtc(path)"),
                new("setWorkspace", 2, "fs.setWorkspace(path)"),
                new("set_workspace", 2, "fs.set_workspace(path)"),
                new("getWorkspace", 2, "fs.getWorkspace()"),
                new("get_workspace", 2, "fs.get_workspace()"),
                new("getDirectory", 2, "fs.getDirectory(path)"),
                new("directoryInfo", 2, "fs.directoryInfo(path)"),
                new("DirectoryInfo", 2, "fs.DirectoryInfo(path)")
            ],
            ["file"] =
            [
                new("open", 2, "file.open(path, mode)"),
                new("fopen", 2, "file.fopen(path, mode)"),
                new("openBinary", 2, "file.openBinary(path, mode)"),
                new("fbopen", 2, "file.fbopen(path, mode)"),
                new("exists", 2, "file.exists(path)"),
                new("fexist", 2, "file.fexist(path)"),
                new("copy", 2, "file.copy(source, dest, overwrite = false)"),
                new("fcopy", 2, "file.fcopy(source, dest, overwrite = false)"),
                new("move", 2, "file.move(source, dest, overwrite = false)"),
                new("fmove", 2, "file.fmove(source, dest, overwrite = false)"),
                new("delete", 2, "file.delete(path)"),
                new("fdelete", 2, "file.fdelete(path)"),
                new("readAllBytes", 2, "file.readAllBytes(path)"),
                new("readAllBytesAsync", 2, "file.readAllBytesAsync(path)"),
                new("writeAllBytes", 2, "file.writeAllBytes(path, bytes)"),
                new("readTextAsync", 2, "file.readTextAsync(path)"),
                new("writeTextAsync", 2, "file.writeTextAsync(path, text)"),
                new("appendTextAsync", 2, "file.appendTextAsync(path, text)"),
                new("size", 2, "file.size(path)"),
                new("fileSize", 2, "file.fileSize(path)"),
                new("lastWriteTime", 2, "file.lastWriteTime(path)"),
                new("fileLastWriteTime", 2, "file.fileLastWriteTime(path)"),
                new("lastWriteTimeUtc", 2, "file.lastWriteTimeUtc(path)"),
                new("fileLastWriteTimeUtc", 2, "file.fileLastWriteTimeUtc(path)"),
                new("setWorkspace", 2, "file.setWorkspace(path)"),
                new("set_workspace", 2, "file.set_workspace(path)"),
                new("getWorkspace", 2, "file.getWorkspace()"),
                new("get_workspace", 2, "file.get_workspace()"),
                new("getDirectory", 2, "file.getDirectory(path)"),
                new("directoryInfo", 2, "file.directoryInfo(path)"),
                new("DirectoryInfo", 2, "file.DirectoryInfo(path)")
            ],
            ["task"] =
            [
                new("get", 2, "task.get(value)"),
                new("completed", 2, "task.completed()"),
                new("delay", 2, "task.delay(ms, value = null)")
            ]
        };

    public CfgsAnalysisResult Analyze(string documentUri, string sourceText, IReadOnlyDictionary<string, string>? openDocumentSources = null)
    {
        string origin = GetDocumentOrigin(documentUri);
        SourceResolver sources = new(documentUri, origin, sourceText, openDocumentSources);
        Dictionary<string, List<CfgsDiagnostic>> diagnosticsByUri = CreateUriMap<List<CfgsDiagnostic>>();

        try
        {
            FrontendPipeline frontendPipeline = new(
                loadPluginDll: _ => { },
                loadImportSource: sources.GetOverlaySourceText,
                workingDirectory: FrontendPipeline.TryGetWorkingDirectory(origin));
            List<Stmt> ast = frontendPipeline.BuildLoweredAst(origin, sourceText);

            Compiler compiler = new(origin);
            compiler.Compile(ast);

            SymbolCollector collector = new(sources, origin, documentUri);
            collector.Collect(ast);
            CfgsSemanticModel semanticModel = new CfgsSemanticModelBuilder(documentUri, origin, sourceText, openDocumentSources).Build(ast);

            EnsureDocumentEntry(diagnosticsByUri, documentUri);
            return new CfgsAnalysisResult(documentUri, origin, sourceText, diagnosticsByUri, collector.DocumentSymbols, collector.AllSymbols, semanticModel);
        }
        catch (LexerException ex)
        {
            AddExceptionDiagnostic(diagnosticsByUri, sources, documentUri, ex.Filename, ex.Line, ex.Column, ex.RawMessage, InferDiagnosticCode(ex.Category, ex.RawMessage), ex.SpanLength);
        }
        catch (ParserException ex)
        {
            AddExceptionDiagnostic(diagnosticsByUri, sources, documentUri, ex.Filename, ex.Line, ex.Column, ex.RawMessage, InferDiagnosticCode(ex.Category, ex.RawMessage), ex.SpanLength);
        }
        catch (CompilerException ex)
        {
            AddExceptionDiagnostic(diagnosticsByUri, sources, documentUri, ex.FileSource, ex.Line, ex.Column, ex.RawMessage, InferDiagnosticCode(ex.Category, ex.RawMessage), ex.SpanLength);
        }
        catch (Exception ex)
        {
            AddExceptionDiagnostic(diagnosticsByUri, sources, documentUri, origin, 1, 1, ex.Message, "CFGS9000");
        }

        EnsureDocumentEntry(diagnosticsByUri, documentUri);
        return new CfgsAnalysisResult(documentUri, origin, sourceText, diagnosticsByUri, [], [], new CfgsSemanticModel([], new Dictionary<string, CfgsSymbol>(), [], [], []));
    }

    public IReadOnlyList<CfgsCompletionItem> GetCompletions(CfgsAnalysisResult result)
    {
        Dictionary<string, CfgsCompletionItem> items = new(StringComparer.Ordinal);

        foreach (string keyword in Keywords)
            items[keyword] = new CfgsCompletionItem(keyword, 14, "keyword");

        foreach (CfgsCompletionItem completion in StandardLibraryCompletions)
            items.TryAdd(completion.Label, completion);

        foreach (CfgsSymbol symbol in result.AllSymbols)
        {
            if (symbol.IsSynthetic)
                continue;

            if (!items.ContainsKey(symbol.Name))
                items[symbol.Name] = new CfgsCompletionItem(symbol.Name, ToCompletionKind(symbol.Kind), symbol.Detail);
        }

        foreach (CfgsSymbol symbol in result.SemanticModel.Symbols)
        {
            if (symbol.IsSynthetic)
                continue;

            if (!items.ContainsKey(symbol.Name))
                items[symbol.Name] = new CfgsCompletionItem(symbol.Name, ToCompletionKind(symbol.Kind), symbol.Detail);
        }

        return items.Values.OrderBy(static item => item.Label, StringComparer.Ordinal).ToList();
    }

    public IReadOnlyList<int> GetSemanticTokens(CfgsAnalysisResult result)
    {
        List<CfgsSemanticTokenSpan> spans = [];

        foreach (CfgsResolvedOccurrence occurrence in result.SemanticModel.Occurrences)
        {
            if (!string.Equals(occurrence.Uri, result.DocumentUri, GetPathComparison()))
                continue;

            if (!result.SemanticModel.SymbolsById.TryGetValue(occurrence.SymbolId, out CfgsSymbol? symbol))
                continue;

            if (!TryMapSemanticToken(symbol, occurrence.IsDeclaration, out CfgsSemanticTokenSpan span))
                continue;

            spans.Add(span);
        }

        foreach (CfgsSymbol symbol in result.DocumentSymbols)
        {
            if (!string.Equals(symbol.Uri, result.DocumentUri, GetPathComparison()))
                continue;

            if (symbol.Kind != 3)
                continue;

            if (TryMapSemanticToken(symbol, isDeclaration: true, out CfgsSemanticTokenSpan span))
                spans.Add(span);
        }

        List<CfgsSemanticTokenSpan> ordered = spans
            .GroupBy(static span => (span.Line, span.Character, span.Length, span.TokenType, span.TokenModifiers))
            .Select(static group => group.First())
            .OrderBy(static span => span.Line)
            .ThenBy(static span => span.Character)
            .ThenBy(static span => span.Length)
            .ToList();

        List<int> data = [];
        int previousLine = 0;
        int previousCharacter = 0;
        bool hasPrevious = false;

        foreach (CfgsSemanticTokenSpan span in ordered)
        {
            int deltaLine = hasPrevious ? span.Line - previousLine : span.Line;
            int deltaCharacter = !hasPrevious || deltaLine != 0
                ? span.Character
                : span.Character - previousCharacter;

            data.Add(deltaLine);
            data.Add(deltaCharacter);
            data.Add(span.Length);
            data.Add(span.TokenType);
            data.Add(span.TokenModifiers);

            previousLine = span.Line;
            previousCharacter = span.Character;
            hasPrevious = true;
        }

        return data;
    }

    public IReadOnlyList<CfgsSymbol> FindDefinitions(CfgsAnalysisResult result, int line, int character)
    {
        if (TryGetNonSymbolKeywordAt(result.SourceText, line, character, out _))
            return [];

        if (TryFindResolvedSymbol(result, line, character, out CfgsSymbol? resolved))
            return [resolved!];

        string? qualifiedName = TextLocator.GetQualifiedIdentifierAt(result.SourceText, line, character);
        if (!string.IsNullOrWhiteSpace(qualifiedName))
        {
            List<CfgsSymbol> qualifiedMatches =
                result.AllSymbols
                    .Where(symbol => string.Equals(symbol.QualifiedName, qualifiedName, StringComparison.Ordinal))
                    .ToList();

            if (qualifiedMatches.Count > 0)
                return qualifiedMatches;
        }

        string? identifier = TextLocator.GetIdentifierAt(result.SourceText, line, character);
        if (string.IsNullOrWhiteSpace(identifier))
            return [];

        List<CfgsSymbol> sameFileMatches =
            result.AllSymbols
                .Where(symbol =>
                    string.Equals(symbol.Name, identifier, StringComparison.Ordinal) &&
                    string.Equals(symbol.Uri, result.DocumentUri, StringComparison.Ordinal))
                .ToList();

        if (sameFileMatches.Count > 0)
            return sameFileMatches;

        return result.AllSymbols
            .Where(symbol => string.Equals(symbol.Name, identifier, StringComparison.Ordinal))
            .ToList();
    }

    public CfgsSymbol? FindHoverSymbol(CfgsAnalysisResult result, int line, int character)
        => FindDefinitions(result, line, character).FirstOrDefault()
           ?? TryFindStandardLibrarySymbol(result, line, character);

    public CfgsSymbol? ResolveSymbol(CfgsAnalysisResult result, int line, int character)
    {
        if (TryGetNonSymbolKeywordAt(result.SourceText, line, character, out _))
            return null;

        if (TryFindResolvedSymbol(result, line, character, out CfgsSymbol? resolved))
            return resolved;

        return FindDefinitions(result, line, character).FirstOrDefault();
    }

    public IReadOnlyList<CfgsSymbol> FindDocumentHighlights(CfgsAnalysisResult result, int line, int character)
    {
        CfgsSymbol? symbol = ResolveSymbol(result, line, character);
        if (symbol is null)
            return [];

        return FindReferences(result, symbol, includeDeclaration: true)
            .Where(reference => string.Equals(reference.Uri, result.DocumentUri, GetPathComparison()))
            .ToList();
    }

    public CfgsPrepareRenameResult? PrepareRename(CfgsAnalysisResult result, int line, int character)
    {
        CfgsSymbol? symbol = ResolveSymbol(result, line, character);
        if (symbol is null || symbol.IsSynthetic)
            return null;

        return new CfgsPrepareRenameResult(symbol.SelectionRange, symbol.Name);
    }

    public IReadOnlyList<CfgsSymbol> FindReferences(CfgsAnalysisResult result, int line, int character, bool includeDeclaration)
    {
        CfgsSymbol? symbol = ResolveSymbol(result, line, character);
        return symbol is null ? [] : FindReferences(result, symbol, includeDeclaration);
    }

    public IReadOnlyList<CfgsSymbol> FindReferences(CfgsAnalysisResult result, CfgsSymbol symbol, bool includeDeclaration)
    {
        CfgsSymbol? resolvedSymbol = FindEquivalentSemanticSymbol(result, symbol);
        if (resolvedSymbol is null || string.IsNullOrWhiteSpace(resolvedSymbol.Id))
            return [];

        IEnumerable<CfgsResolvedOccurrence> occurrences = result.SemanticModel.Occurrences
            .Where(occurrence => string.Equals(occurrence.SymbolId, resolvedSymbol.Id, StringComparison.Ordinal));

        if (!includeDeclaration)
            occurrences = occurrences.Where(static occurrence => !occurrence.IsDeclaration);

        return occurrences
            .Select(occurrence => new CfgsSymbol(
                resolvedSymbol.Name,
                resolvedSymbol.QualifiedName,
                resolvedSymbol.Kind,
                occurrence.Uri,
                resolvedSymbol.OriginFile,
                occurrence.Range,
                occurrence.Range,
                resolvedSymbol.Detail,
                resolvedSymbol.HoverText,
                [],
                resolvedSymbol.IsSynthetic,
                resolvedSymbol.Id,
                resolvedSymbol.Signature,
                resolvedSymbol.ValueTypeQualifiedName,
                resolvedSymbol.CallTargetSymbolId))
            .ToList();
    }

    public IReadOnlyList<CfgsSymbol> FindWorkspaceSymbols(CfgsAnalysisResult result, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        return result.AllSymbols
            .Concat(result.SemanticModel.Symbols)
            .Where(symbol => !symbol.IsSynthetic &&
                             !string.IsNullOrWhiteSpace(symbol.Name) &&
                             symbol.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .GroupBy(symbol => (symbol.QualifiedName, symbol.Uri), StringTupleComparer.Instance)
            .Select(static group => group.First())
            .OrderBy(static symbol => symbol.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<CfgsSymbol> FindTypeDefinition(CfgsAnalysisResult result, int line, int character)
    {
        CfgsSymbol? symbol = ResolveSymbol(result, line, character);
        if (symbol is null)
            return [];

        string? typeQualifiedName = FindSymbolValueType(result, symbol);
        if (string.IsNullOrWhiteSpace(typeQualifiedName))
            return [];

        return result.SemanticModel.Symbols
            .Where(s => string.Equals(s.QualifiedName, typeQualifiedName, StringComparison.Ordinal) && s.Kind is 5 or 10 or 11)
            .Take(1)
            .Concat(result.AllSymbols
                .Where(s => string.Equals(s.QualifiedName, typeQualifiedName, StringComparison.Ordinal) && s.Kind is 5 or 10 or 11))
            .GroupBy(s => s.QualifiedName, StringComparer.Ordinal)
            .Select(static g => g.First())
            .ToList();
    }

    public IReadOnlyList<CfgsSymbol> FindImplementations(CfgsAnalysisResult result, int line, int character)
    {
        CfgsSymbol? symbol = ResolveSymbol(result, line, character);
        return symbol is null ? [] : FindImplementations(result, symbol);
    }

    public IReadOnlyList<CfgsSymbol> FindImplementations(CfgsAnalysisResult result, CfgsSymbol symbol)
    {
        CfgsSymbol? resolvedSymbol = FindEquivalentSemanticSymbol(result, symbol) ?? symbol;

        if (resolvedSymbol.Kind is 5 or 11)
            return FindTypeImplementations(result, resolvedSymbol);

        if (resolvedSymbol.Kind == 6)
        {
            string? containingTypeQualifiedName = TryGetContainingTypeQualifiedName(resolvedSymbol.QualifiedName);
            if (!string.IsNullOrWhiteSpace(containingTypeQualifiedName))
            {
                CfgsSymbol? containingType = result.AllSymbols
                    .Concat(result.SemanticModel.Symbols)
                    .FirstOrDefault(s =>
                        string.Equals(s.QualifiedName, containingTypeQualifiedName, StringComparison.Ordinal) &&
                        s.Kind is 5 or 11);

                if (containingType is not null)
                {
                    List<CfgsSymbol> members = FindTypeImplementations(result, containingType)
                        .SelectMany(candidateType => result.AllSymbols
                            .Concat(result.SemanticModel.Symbols)
                            .Where(s =>
                                s.Kind == 6 &&
                                !s.IsSynthetic &&
                                string.Equals(
                                    s.QualifiedName,
                                    $"{candidateType.QualifiedName}.{resolvedSymbol.Name}",
                                    StringComparison.Ordinal)))
                        .GroupBy(s => s.QualifiedName, StringComparer.Ordinal)
                        .Select(static g => g.First())
                        .ToList();

                    if (members.Count > 0)
                        return members;
                }
            }

            string methodName = resolvedSymbol.Name;
            return result.AllSymbols
                .Concat(result.SemanticModel.Symbols)
                .Where(s => s.Kind == 6 && !s.IsSynthetic &&
                            string.Equals(s.Name, methodName, StringComparison.Ordinal) &&
                            !string.Equals(s.QualifiedName, resolvedSymbol.QualifiedName, StringComparison.Ordinal))
                .GroupBy(s => s.QualifiedName, StringComparer.Ordinal)
                .Select(static g => g.First())
                .ToList();
        }

        return [];
    }

    public CfgsCallHierarchyItem? PrepareCallHierarchy(CfgsAnalysisResult result, int line, int character)
    {
        CfgsSymbol? symbol = ResolveSymbol(result, line, character);
        symbol = ResolveCallTargetSymbol(result, symbol);
        if (symbol is null || symbol.IsSynthetic || symbol.Kind is not (6 or 12))
        {
            if (!TryFindResolvedCallableSymbol(result, line, character, static s => !s.IsSynthetic && s.Kind is 6 or 12, out symbol))
                return null;
        }

        if (symbol is null)
            return null;

        return new CfgsCallHierarchyItem(symbol.Name, symbol.Kind, symbol.Uri, symbol.Range, symbol.SelectionRange, symbol.Detail);
    }

    public IReadOnlyList<CfgsCallHierarchyIncomingCall> FindIncomingCalls(CfgsAnalysisResult result, CfgsCallHierarchyItem item)
    {
        CfgsSymbol? target = result.SemanticModel.Symbols
            .FirstOrDefault(s => string.Equals(s.Name, item.Name, StringComparison.Ordinal) &&
                                 string.Equals(s.Uri, item.Uri, GetPathComparison()) &&
                                 s.SelectionRange.Equals(item.SelectionRange) &&
                                 s.Kind is 6 or 12);

        if (target is null || string.IsNullOrWhiteSpace(target.Id))
            return [];

        Dictionary<string, (CfgsSymbol Caller, List<RangeInfo> Ranges)> callers = new(StringComparer.Ordinal);
        foreach (CfgsResolvedCallSite callSite in GetResolvedCallSites(result))
        {
            IReadOnlyList<CfgsSymbol> calledSymbols = ResolveCallSiteSymbols(result, callSite, static s => s.Kind is 6 or 12);
            if (!calledSymbols.Any(calledSymbol => string.Equals(calledSymbol.Id, target.Id, StringComparison.Ordinal)))
                continue;

            CfgsSymbol? caller = FindEnclosingFunction(result, callSite.Uri, callSite.Range.Start.Line, callSite.Range.Start.Character);
            if (caller is null)
                continue;

            string key = $"{caller.Uri}|{caller.QualifiedName}";
            if (!callers.TryGetValue(key, out var entry))
            {
                entry = (caller, []);
                callers[key] = entry;
            }
            if (!entry.Ranges.Contains(callSite.Range))
                entry.Ranges.Add(callSite.Range);
        }

        return callers.Values
            .Select(entry => new CfgsCallHierarchyIncomingCall(
                new CfgsCallHierarchyItem(entry.Caller.Name, entry.Caller.Kind, entry.Caller.Uri, entry.Caller.Range, entry.Caller.SelectionRange, entry.Caller.Detail),
                entry.Ranges))
            .ToList();
    }

    public IReadOnlyList<CfgsCallHierarchyOutgoingCall> FindOutgoingCalls(CfgsAnalysisResult result, CfgsCallHierarchyItem item)
    {
        CfgsSymbol? source = result.SemanticModel.Symbols
            .FirstOrDefault(s => string.Equals(s.Name, item.Name, StringComparison.Ordinal) &&
                                 string.Equals(s.Uri, item.Uri, GetPathComparison()) &&
                                 s.SelectionRange.Equals(item.SelectionRange) &&
                                 s.Kind is 6 or 12);

        if (source is null || string.IsNullOrWhiteSpace(source.Id))
            return [];

        CfgsResolvedOccurrence? decl = result.SemanticModel.Occurrences
            .FirstOrDefault(o => string.Equals(o.SymbolId, source.Id, StringComparison.Ordinal) && o.IsDeclaration);

        if (decl is null)
            return [];

        Dictionary<string, (CfgsSymbol Callee, List<RangeInfo> Ranges)> callees = new(StringComparer.Ordinal);
        foreach (CfgsResolvedCallSite callSite in GetResolvedCallSites(result))
        {
            if (!string.Equals(callSite.Uri, decl.Uri, GetPathComparison()))
                continue;

            CfgsSymbol? enclosing = FindEnclosingFunction(result, callSite.Uri, callSite.Range.Start.Line, callSite.Range.Start.Character);
            if (enclosing is null || !string.Equals(enclosing.Id, source.Id, StringComparison.Ordinal))
                continue;

            foreach (CfgsSymbol calledSymbol in ResolveCallSiteSymbols(result, callSite, static s => s.Kind is 6 or 12))
            {
                string key = $"{calledSymbol.Uri}|{calledSymbol.QualifiedName}";
                if (!callees.TryGetValue(key, out var entry))
                {
                    entry = (calledSymbol, []);
                    callees[key] = entry;
                }

                if (!entry.Ranges.Contains(callSite.Range))
                    entry.Ranges.Add(callSite.Range);
            }

        }

        return callees.Values
            .Select(entry => new CfgsCallHierarchyOutgoingCall(
                new CfgsCallHierarchyItem(entry.Callee.Name, entry.Callee.Kind, entry.Callee.Uri, entry.Callee.Range, entry.Callee.SelectionRange, entry.Callee.Detail),
                entry.Ranges))
            .ToList();
    }

    public IReadOnlyList<CfgsInlayHint> GetInlayHints(CfgsAnalysisResult result, RangeInfo range)
    {
        List<CfgsInlayHint> hints = [];

        foreach (CfgsResolvedCallSite callSite in GetResolvedCallSites(result, range))
        {
            IReadOnlyList<CfgsSignature> signatures = ResolveCallSiteSymbols(result, callSite, static s => s.Signature is not null && s.Kind is 6 or 12 or 5)
                .Select(static symbol => symbol.Signature!)
                .GroupBy(static signature => signature.Label, StringComparer.Ordinal)
                .Select(static group => group.First())
                .ToList();
            if (signatures.Count == 0)
                continue;

            int callArgIndex = SignatureLocator.TryFindCallArgPositions(result.SourceText, callSite.Range.Start.Line, callSite.Range.End.Character);
            if (callArgIndex < 0)
                continue;

            IReadOnlyList<(PositionInfo Position, int ArgIndex)> argPositions =
                SignatureLocator.GetArgumentPositions(result.SourceText, callSite.Range.Start.Line, callArgIndex);

            foreach ((PositionInfo position, int argIndex) in argPositions)
            {
                string? label = TryGetConsensusParameterLabel(signatures, argIndex);
                if (label is not null)
                    hints.Add(new CfgsInlayHint(position, $"{label}:", CfgsInlayHintKind.Parameter));
            }
        }

        return hints;
    }

    public IReadOnlyList<CfgsCodeLens> GetCodeLenses(CfgsAnalysisResult result)
    {
        List<CfgsCodeLens> lenses = [];

        foreach (CfgsSymbol symbol in result.DocumentSymbols)
            CollectCodeLenses(result, symbol, lenses);

        return lenses;
    }

    public IReadOnlyList<CfgsDocumentLink> GetDocumentLinks(CfgsAnalysisResult result)
    {
        List<CfgsDocumentLink> links = [];
        string[] lines = result.SourceText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            (string RawPath, int StartChar, int EndChar)? importPath = TryMatchImportPath(lines[i]);
            if (importPath is null)
                continue;

            string? resolvedUri = TryResolveImportUri(result.Origin, importPath.Value.RawPath);
            if (resolvedUri is null)
                continue;

            links.Add(new CfgsDocumentLink(
                new RangeInfo(new PositionInfo(i, importPath.Value.StartChar), new PositionInfo(i, importPath.Value.EndChar)),
                resolvedUri));
        }

        return links;
    }

    public IReadOnlyList<CfgsSelectionRange> GetSelectionRanges(CfgsAnalysisResult result, IReadOnlyList<PositionInfo> positions)
    {
        List<CfgsSelectionRange> ranges = [];

        foreach (PositionInfo position in positions)
        {
            string? identifier = TextLocator.GetIdentifierAt(result.SourceText, position.Line, position.Character);
            RangeInfo wordRange = identifier is not null
                ? TextLocator.CreateRange(result.SourceText, position.Line + 1, position.Character + 1, identifier)
                : new RangeInfo(position, new PositionInfo(position.Line, position.Character + 1));

            string[] lines = result.SourceText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
            RangeInfo lineRange = position.Line < lines.Length
                ? new RangeInfo(new PositionInfo(position.Line, 0), new PositionInfo(position.Line, lines[position.Line].Length))
                : wordRange;

            CfgsSymbol? enclosing = FindSmallestEnclosingSymbol(result.DocumentSymbols, position.Line);
            CfgsSelectionRange? parent = null;

            RangeInfo fullRange = new(new PositionInfo(0, 0), new PositionInfo(Math.Max(lines.Length - 1, 0), lines.Length > 0 ? lines[^1].Length : 0));
            CfgsSelectionRange fullDocRange = new(fullRange, null);

            if (enclosing is not null)
            {
                CfgsSelectionRange symbolRange = new(enclosing.Range, fullDocRange);
                CfgsSelectionRange lineRangeNode = new(lineRange, symbolRange);
                parent = new CfgsSelectionRange(wordRange, lineRangeNode);
            }
            else
            {
                CfgsSelectionRange lineRangeNode = new(lineRange, fullDocRange);
                parent = new CfgsSelectionRange(wordRange, lineRangeNode);
            }

            ranges.Add(parent);
        }

        return ranges;
    }

    public IReadOnlyList<CfgsTextEdit> FormatDocument(CfgsAnalysisResult result, int tabSize, bool insertSpaces)
    {
        CfgsFormatResult formatted = CfgsFormatter.Format(result.SourceText, new CfgsFormatOptions(tabSize, !insertSpaces));
        if (!formatted.Changed)
            return [];

        return
        [
            new CfgsTextEdit(
                result.DocumentUri,
                new RangeInfo(new PositionInfo(0, 0), GetDocumentEndPosition(result.SourceText)),
                formatted.Text)
        ];
    }

    private static PositionInfo GetDocumentEndPosition(string text)
    {
        string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        int lastNewline = normalized.LastIndexOf('\n');
        if (lastNewline < 0)
            return new PositionInfo(0, normalized.Length);

        int line = normalized.Count(static ch => ch == '\n');
        return new PositionInfo(line, normalized.Length - lastNewline - 1);
    }

    public IReadOnlyList<CfgsCompletionItem> GetCompletions(CfgsAnalysisResult result, int line, int character)
    {
        if (TryFindImportPathCompletionRequest(result.SourceText, line, character, out ImportPathCompletionRequest importPathRequest))
            return GetImportPathCompletions(result, importPathRequest);

        if (TryFindNamedImportCompletionRequest(result.SourceText, line, character, out NamedImportCompletionRequest namedImportRequest))
            return GetNamedImportCompletions(result, namedImportRequest);

        MemberCompletionRequest? request = TryFindMemberCompletionRequest(result.SourceText, line, character);
        if (request is not null)
        {
            IReadOnlyList<CfgsCompletionItem> semanticItems = GetSemanticDotCompletions(result, request.Value);
            if (semanticItems.Count > 0)
                return semanticItems;

            string? completionPrefix = GetDotContainerPrefix(result.SourceText, line, request.Value.TargetCharacter);
            if (completionPrefix is not null)
            {
                IReadOnlyList<CfgsCompletionItem> metadataItems = GetMetadataDotCompletions(result, completionPrefix, request.Value.TypedPrefix);
                if (metadataItems.Count > 0)
                    return metadataItems;

                return GetDotCompletions(result, completionPrefix, request.Value.TypedPrefix);
            }
        }

        string? prefix = GetDotPrefix(result.SourceText, line, character);
        if (prefix is not null)
        {
            IReadOnlyList<CfgsCompletionItem> metadataItems = GetMetadataDotCompletions(result, prefix, typedPrefix: "");
            if (metadataItems.Count > 0)
                return metadataItems;

            return GetDotCompletions(result, prefix);
        }

        return AddImportedPluginTopLevelCompletions(result, GetCompletions(result));
    }

    public bool ShouldIncludeSnippetCompletions(CfgsAnalysisResult result, int line, int character)
        => !TryFindImportPathCompletionRequest(result.SourceText, line, character, out _) &&
           !TryFindNamedImportCompletionRequest(result.SourceText, line, character, out _) &&
           TryFindMemberCompletionRequest(result.SourceText, line, character) is null &&
           GetDotPrefix(result.SourceText, line, character) is null;

    public IReadOnlyList<CfgsCompletionItem> GetSnippetCompletions()
    {
        return
        [
            new CfgsCompletionItem("if", 15, "if statement", "if (${1:condition}) {\n\t$0\n}"),
            new CfgsCompletionItem("ife", 15, "if-else statement", "if (${1:condition}) {\n\t$2\n} else {\n\t$0\n}"),
            new CfgsCompletionItem("for", 15, "for loop", "for (var ${1:i} = 0; ${1:i} < ${2:count}; ${1:i}++;) {\n\t$0\n}"),
            new CfgsCompletionItem("foreach", 15, "foreach loop", "foreach (var ${1:item} in ${2:collection}) {\n\t$0\n}"),
            new CfgsCompletionItem("while", 15, "while loop", "while (${1:condition}) {\n\t$0\n}"),
            new CfgsCompletionItem("func", 15, "function declaration", "func ${1:name}(${2:params}) {\n\t$0\n}"),
            new CfgsCompletionItem("class", 15, "class declaration", "class ${1:Name}(${2:params}) {\n\t$0\n}"),
            new CfgsCompletionItem("interface", 15, "interface declaration", "interface ${1:IName} {\n\tfunc ${2:name}(${3:params});\n\t$0\n}"),
            new CfgsCompletionItem("try", 15, "try-catch block", "try {\n\t$1\n} catch (${2:err}) {\n\t$0\n}"),
            new CfgsCompletionItem("match", 15, "match statement", "match (${1:expr}) {\n\tcase ${2:pattern}: {\n\t\t$0\n\t}\n}"),
            new CfgsCompletionItem("import", 15, "import statement", "import { ${1:name} } from \"${2:./module.cfs}\""),
            new CfgsCompletionItem("export", 15, "export declaration", "export ${0}"),
            new CfgsCompletionItem("async", 15, "async function", "async func ${1:name}(${2:params}) {\n\t$0\n}"),
        ];
    }

    public CfgsSignatureHelpResult? FindSignatureHelp(CfgsAnalysisResult result, int line, int character)
    {
        SignatureRequest? request = SignatureLocator.TryFindSignatureRequest(result.SourceText, line, character);
        if (request is null)
            return TryFindStandardLibrarySignatureHelp(result, line, character);

        IReadOnlyList<CfgsSignature> signatures = FindResolvedCallSite(result, request.Value.TargetLine, request.Value.TargetCharacter) is CfgsResolvedCallSite callSite
            ? ResolveCallSiteSymbols(result, callSite, static s => s.Signature is not null)
                .Select(static symbol => symbol.Signature!)
                .GroupBy(static signature => signature.Label, StringComparer.Ordinal)
                .Select(static group => group.First())
                .ToList()
            : FindResolvedCallableSymbols(result, request.Value.TargetLine, request.Value.TargetCharacter, static s => s.Signature is not null)
                .Select(static symbol => symbol.Signature!)
                .GroupBy(static signature => signature.Label, StringComparer.Ordinal)
                .Select(static group => group.First())
                .ToList();

        if (signatures.Count == 0)
            return TryFindStandardLibrarySignatureHelp(result, request.Value)
                   ?? TryFindStandardLibrarySignatureHelp(result, line, character);

        int maxParameters = signatures.Max(static signature => signature.Parameters.Count);
        int activeParameter = Math.Min(request.Value.ActiveParameter, Math.Max(maxParameters - 1, 0));
        return new CfgsSignatureHelpResult(signatures, 0, activeParameter);
    }

    private static CfgsSignatureHelpResult? TryFindStandardLibrarySignatureHelp(CfgsAnalysisResult result, SignatureRequest request)
    {
        string? callablePath = GetCallablePathAt(result.SourceText, request.TargetLine, request.TargetCharacter);
        if (string.IsNullOrWhiteSpace(callablePath))
            return null;

        if (!TryFindStandardLibraryCompletion(callablePath!, out CfgsCompletionItem completion))
            return null;

        if (!completion.Detail.Contains('('))
            return null;

        CfgsSignature signature = CompletionItemToSignature(completion);
        int activeParameter = Math.Min(request.ActiveParameter, Math.Max(signature.Parameters.Count - 1, 0));
        return new CfgsSignatureHelpResult([signature], 0, activeParameter);
    }

    private static CfgsSignatureHelpResult? TryFindStandardLibrarySignatureHelp(CfgsAnalysisResult result, int line, int character)
    {
        if (!TryFindLineCallTarget(result.SourceText, line, character, out string? callablePath, out int activeParameter))
            return null;

        if (!TryFindStandardLibraryCompletion(callablePath, out CfgsCompletionItem completion))
            return null;

        if (!completion.Detail.Contains('('))
            return null;

        CfgsSignature signature = CompletionItemToSignature(completion);
        int boundedParameter = Math.Min(activeParameter, Math.Max(signature.Parameters.Count - 1, 0));
        return new CfgsSignatureHelpResult([signature], 0, boundedParameter);
    }

    private static CfgsSymbol? TryFindStandardLibrarySymbol(CfgsAnalysisResult result, int line, int character)
    {
        if (!TryGetQualifiedIdentifierAt(result.SourceText, line, character, out string? path, out RangeInfo fullRange))
            return null;

        if (!TryFindStandardLibraryCompletion(path, out CfgsCompletionItem completion))
            return null;

        string name = path.Contains('.', StringComparison.Ordinal)
            ? path[(path.LastIndexOf('.') + 1)..]
            : path;
        RangeInfo selectionRange = CreateLeafRange(result.SourceText, line, character, name);
        string hoverText = CreateStandardLibraryHoverText(completion);

        return new CfgsSymbol(
            name,
            path,
            ToStandardLibrarySymbolKind(completion),
            result.DocumentUri,
            result.Origin,
            fullRange,
            selectionRange,
            completion.Detail,
            hoverText,
            [],
            IsSynthetic: true);
    }

    private static string? GetCallablePathAt(string sourceText, int line, int character)
        => TryGetQualifiedIdentifierAt(sourceText, line, character, out string? path, out _)
            ? path
            : null;

    private static bool TryFindLineCallTarget(string sourceText, int line, int character, out string callablePath, out int activeParameter)
    {
        callablePath = string.Empty;
        activeParameter = 0;

        string[] lines = sourceText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        if (line < 0 || line >= lines.Length)
            return false;

        string lineText = lines[line];
        int cursor = Math.Clamp(character, 0, lineText.Length);
        int openParenIndex = FindLineCallOpenParen(lineText, cursor);
        if (openParenIndex < 0)
            return false;

        activeParameter = CountLineArguments(lineText, openParenIndex + 1, cursor);

        int targetIndex = openParenIndex - 1;
        while (targetIndex >= 0 && char.IsWhiteSpace(lineText[targetIndex]))
            targetIndex--;

        if (targetIndex < 0)
            return false;

        while (targetIndex >= 0 && !IsIdentifierChar(lineText[targetIndex]))
            targetIndex--;

        return targetIndex >= 0 &&
               TryGetQualifiedIdentifierAt(sourceText, line, targetIndex, out callablePath, out _);
    }

    private static int FindLineCallOpenParen(string lineText, int cursor)
    {
        int parenDepth = 0;
        int bracketDepth = 0;
        int braceDepth = 0;

        for (int i = cursor - 1; i >= 0; i--)
        {
            char ch = lineText[i];
            switch (ch)
            {
                case ')':
                    parenDepth++;
                    break;
                case ']':
                    bracketDepth++;
                    break;
                case '}':
                    braceDepth++;
                    break;
                case '(':
                    if (parenDepth == 0 && bracketDepth == 0 && braceDepth == 0)
                        return i;
                    parenDepth--;
                    break;
                case '[':
                    if (bracketDepth > 0)
                        bracketDepth--;
                    break;
                case '{':
                    if (braceDepth > 0)
                        braceDepth--;
                    break;
            }
        }

        return -1;
    }

    private static int CountLineArguments(string lineText, int start, int cursor)
    {
        int commas = 0;
        int parenDepth = 0;
        int bracketDepth = 0;
        int braceDepth = 0;

        for (int i = start; i < cursor && i < lineText.Length; i++)
        {
            char ch = lineText[i];
            switch (ch)
            {
                case '(':
                    parenDepth++;
                    break;
                case ')':
                    if (parenDepth > 0)
                        parenDepth--;
                    break;
                case '[':
                    bracketDepth++;
                    break;
                case ']':
                    if (bracketDepth > 0)
                        bracketDepth--;
                    break;
                case '{':
                    braceDepth++;
                    break;
                case '}':
                    if (braceDepth > 0)
                        braceDepth--;
                    break;
                case ',':
                    if (parenDepth == 0 && bracketDepth == 0 && braceDepth == 0)
                        commas++;
                    break;
            }
        }

        return commas;
    }

    private static bool TryFindStandardLibraryCompletion(string path, out CfgsCompletionItem completion)
    {
        if (path.Contains('.', StringComparison.Ordinal))
        {
            int dotIndex = path.LastIndexOf('.');
            string namespaceName = path[..dotIndex];
            string memberName = path[(dotIndex + 1)..];

            if (StandardLibraryNamespaceMembers.TryGetValue(namespaceName, out CfgsCompletionItem[]? members))
            {
                foreach (CfgsCompletionItem item in members)
                {
                    if (string.Equals(item.Label, memberName, StringComparison.Ordinal))
                    {
                        completion = item;
                        return true;
                    }
                }
            }
        }

        foreach (CfgsCompletionItem item in StandardLibraryCompletions)
        {
            if (string.Equals(item.Label, path, StringComparison.Ordinal))
            {
                completion = item;
                return true;
            }
        }

        completion = default!;
        return false;
    }

    private static bool TryGetQualifiedIdentifierAt(string sourceText, int line, int character, out string path, out RangeInfo range)
    {
        path = string.Empty;
        range = default;

        string[] lines = sourceText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        if (line < 0 || line >= lines.Length)
            return false;

        string lineText = lines[line];
        if (lineText.Length == 0)
            return false;

        int index = Math.Clamp(character, 0, lineText.Length);
        if (index == lineText.Length && index > 0)
            index--;

        char current = lineText[index];
        if (!IsQualifiedIdentifierChar(current) && index > 0)
            index--;

        if (!IsQualifiedIdentifierChar(lineText[index]))
            return false;

        int start = index;
        while (start > 0 && IsQualifiedIdentifierChar(lineText[start - 1]))
            start--;

        int end = index;
        while (end < lineText.Length && IsQualifiedIdentifierChar(lineText[end]))
            end++;

        string candidate = lineText[start..end].Trim('.');
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        string[] parts = candidate.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (!parts.All(static part => part.All(IsIdentifierChar)))
            return false;

        path = string.Join(".", parts);
        range = new RangeInfo(new PositionInfo(line, start), new PositionInfo(line, end));
        return true;
    }

    private static RangeInfo CreateLeafRange(string sourceText, int line, int character, string name)
    {
        string[] lines = sourceText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        if (line < 0 || line >= lines.Length || string.IsNullOrEmpty(name))
            return new RangeInfo(new PositionInfo(line, character), new PositionInfo(line, character + Math.Max(name.Length, 1)));

        string lineText = lines[line];
        int index = Math.Clamp(character, 0, lineText.Length);
        if (index == lineText.Length && index > 0)
            index--;

        if (index >= 0 && index < lineText.Length && !IsIdentifierChar(lineText[index]) && index > 0)
            index--;

        int start = index;
        while (start > 0 && IsIdentifierChar(lineText[start - 1]))
            start--;

        int end = Math.Max(index, start);
        while (end < lineText.Length && IsIdentifierChar(lineText[end]))
            end++;

        if (end <= start)
            end = Math.Min(lineText.Length, start + name.Length);

        return new RangeInfo(new PositionInfo(line, start), new PositionInfo(line, end));
    }

    private static string CreateStandardLibraryHoverText(CfgsCompletionItem item)
    {
        StringBuilder sb = new();
        sb.AppendLine("```cfgs");
        sb.AppendLine(item.Detail);
        sb.AppendLine("```");
        return sb.ToString();
    }

    private static int ToStandardLibrarySymbolKind(CfgsCompletionItem item)
    {
        if (item.Detail.Contains("namespace", StringComparison.OrdinalIgnoreCase))
            return 3;

        if (item.Detail.Contains("constant", StringComparison.OrdinalIgnoreCase))
            return 14;

        if (item.Detail.Contains('('))
            return 12;

        return 13;
    }

    private static CfgsSignature CompletionItemToSignature(CfgsCompletionItem item)
    {
        string label = item.Detail.StartsWith("builtin: ", StringComparison.Ordinal)
            ? item.Detail["builtin: ".Length..]
            : item.Detail;

        int open = label.IndexOf('(');
        int close = label.LastIndexOf(')');
        IReadOnlyList<string> parameters = [];
        if (open >= 0 && close > open)
        {
            parameters = label[(open + 1)..close]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        int minArgs = parameters.Count(parameter => !parameter.Contains('=', StringComparison.Ordinal));
        return new CfgsSignature(label, parameters, RestParameter: null, minArgs);
    }

    public CfgsRenameResult? RenameSymbol(CfgsAnalysisResult result, int line, int character, string newName)
    {
        if (!IsValidIdentifier(newName))
            return null;

        CfgsSymbol? symbol = ResolveSymbol(result, line, character);
        return symbol is null ? null : RenameSymbol(result, symbol, newName);
    }

    public CfgsRenameResult? RenameSymbol(CfgsAnalysisResult result, CfgsSymbol symbol, string newName)
    {
        if (!IsValidIdentifier(newName))
            return null;

        CfgsSymbol? resolvedSymbol = FindEquivalentSemanticSymbol(result, symbol);
        if (resolvedSymbol is null || string.IsNullOrWhiteSpace(resolvedSymbol.Id))
            return null;

        List<CfgsTextEdit> edits = result.SemanticModel.Occurrences
            .Where(occurrence => string.Equals(occurrence.SymbolId, resolvedSymbol.Id, StringComparison.Ordinal))
            .Select(occurrence => new CfgsTextEdit(occurrence.Uri, occurrence.Range, newName))
            .ToList();

        return edits.Count == 0 ? null : new CfgsRenameResult(edits);
    }

    public bool SupportsWorkspaceSymbolMatching(CfgsSymbol symbol)
        => !symbol.IsSynthetic &&
           !string.IsNullOrWhiteSpace(symbol.QualifiedName) &&
           !symbol.QualifiedName.Contains('#', StringComparison.Ordinal);

    private static CfgsSymbol? FindEquivalentSemanticSymbol(CfgsAnalysisResult result, CfgsSymbol symbol)
    {
        CfgsSymbolIdentity identity = CfgsSymbolIdentity.From(symbol);
        if (!string.IsNullOrWhiteSpace(symbol.Id) && result.SemanticModel.SymbolsById.TryGetValue(symbol.Id!, out CfgsSymbol? exact) && identity.Matches(exact))
            return exact;

        return result.SemanticModel.Symbols.FirstOrDefault(candidate => identity.Matches(candidate));
    }

    private static CfgsSymbol? ResolveCallTargetSymbol(CfgsAnalysisResult result, CfgsSymbol? symbol)
    {
        if (symbol is null)
            return null;

        HashSet<string> seen = new(StringComparer.Ordinal);
        CfgsSymbol current = symbol;

        while (!string.IsNullOrWhiteSpace(current.CallTargetSymbolId) &&
               seen.Add(current.Id ?? current.QualifiedName) &&
               result.SemanticModel.SymbolsById.TryGetValue(current.CallTargetSymbolId!, out CfgsSymbol? next))
        {
            current = next;
        }

        return current;
    }

    private static HashSet<string> CollectCallTargetSymbolIds(CfgsAnalysisResult result, CfgsSymbol target)
    {
        HashSet<string> ids = new(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(target.Id))
            ids.Add(target.Id!);

        foreach (CfgsSymbol symbol in result.SemanticModel.Symbols)
        {
            CfgsSymbol? resolved = ResolveCallTargetSymbol(result, symbol);
            if (resolved is null || !string.Equals(resolved.Id, target.Id, StringComparison.Ordinal))
                continue;

            if (!string.IsNullOrWhiteSpace(symbol.Id))
                ids.Add(symbol.Id!);
        }

        return ids;
    }

    private static IReadOnlyList<CfgsResolvedCallSite> GetResolvedCallSites(CfgsAnalysisResult result, RangeInfo? range = null)
    {
        return result.SemanticModel.CallSites
            .Where(callSite =>
                string.Equals(callSite.Uri, result.DocumentUri, GetPathComparison()) &&
                (range is null ||
                 (callSite.Range.Start.Line >= range.Value.Start.Line &&
                  callSite.Range.Start.Line <= range.Value.End.Line)))
            .ToList();
    }

    private static CfgsResolvedCallSite? FindResolvedCallSite(CfgsAnalysisResult result, int line, int character)
        => result.SemanticModel.CallSites.FirstOrDefault(callSite =>
            string.Equals(callSite.Uri, result.DocumentUri, GetPathComparison()) &&
            Contains(callSite.Range, line, character));

    private static IReadOnlyList<CfgsSymbol> ResolveCallSiteSymbols(CfgsAnalysisResult result, CfgsResolvedCallSite callSite, Func<CfgsSymbol, bool> predicate)
        => callSite.SymbolIds
            .Select(symbolId => result.SemanticModel.SymbolsById.GetValueOrDefault(symbolId))
            .Where(symbol => symbol is not null && predicate(symbol))
            .Cast<CfgsSymbol>()
            .GroupBy(static symbol => symbol.Id ?? symbol.QualifiedName, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToList();

    private static string? TryGetConsensusParameterLabel(IReadOnlyList<CfgsSignature> signatures, int argIndex)
    {
        string? label = null;
        foreach (CfgsSignature signature in signatures)
        {
            if (argIndex >= signature.Parameters.Count)
                return null;

            string candidate = signature.Parameters[argIndex];
            if (label is null)
            {
                label = candidate;
                continue;
            }

            if (!string.Equals(label, candidate, StringComparison.Ordinal))
                return null;
        }

        return label;
    }

    private static IReadOnlyList<CfgsSymbol> FindResolvedCallableSymbols(CfgsAnalysisResult result, int line, int character, Func<CfgsSymbol, bool> predicate)
    {
        List<CfgsSymbol> symbols = [];

        foreach (CfgsResolvedOccurrence occurrence in result.SemanticModel.Occurrences)
        {
            if (!string.Equals(occurrence.Uri, result.DocumentUri, GetPathComparison()))
                continue;

            if (!Contains(occurrence.Range, line, character))
                continue;

            if (!result.SemanticModel.SymbolsById.TryGetValue(occurrence.SymbolId, out CfgsSymbol? occurrenceSymbol))
                continue;

            CfgsSymbol? callable = ResolveCallTargetSymbol(result, occurrenceSymbol);
            if (callable is not null && predicate(callable))
                symbols.Add(callable);
        }

        return symbols
            .GroupBy(static symbol => symbol.Id ?? symbol.QualifiedName, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToList();
    }

    private static bool TryFindResolvedCallableSymbol(CfgsAnalysisResult result, int line, int character, Func<CfgsSymbol, bool> predicate, out CfgsSymbol? symbol)
    {
        symbol = FindResolvedCallableSymbols(result, line, character, predicate).FirstOrDefault();
        return symbol is not null;
    }

    private static bool TryFindResolvedSymbol(CfgsAnalysisResult result, int line, int character, out CfgsSymbol? symbol)
    {
        foreach (CfgsResolvedOccurrence occurrence in result.SemanticModel.Occurrences)
        {
            if (!string.Equals(occurrence.Uri, result.DocumentUri, GetPathComparison()))
                continue;

            if (!Contains(occurrence.Range, line, character))
                continue;

            if (result.SemanticModel.SymbolsById.TryGetValue(occurrence.SymbolId, out symbol))
                return true;
        }

        symbol = null;
        return false;
    }

    private static bool Contains(RangeInfo range, int line, int character)
    {
        if (line < range.Start.Line || line > range.End.Line)
            return false;

        if (line == range.Start.Line && character < range.Start.Character)
            return false;

        if (line == range.End.Line && character > range.End.Character)
            return false;

        return true;
    }

    private static bool TryGetNonSymbolKeywordAt(string sourceText, int line, int character, out string? keyword)
    {
        keyword = TextLocator.GetIdentifierAt(sourceText, line, character);
        return !string.IsNullOrWhiteSpace(keyword) && NonSymbolKeywords.Contains(keyword);
    }

    private static StringComparison GetPathComparison()
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool IsValidIdentifier(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            return false;

        if (!char.IsLetter(newName[0]) && newName[0] != '_')
            return false;

        return newName.All(static ch => char.IsLetterOrDigit(ch) || ch == '_');
    }

    private static int ToCompletionKind(int symbolKind)
        => symbolKind switch
        {
            5 => 7,
            7 => 10,
            11 => 8,
            6 => 2,
            10 => 13,
            12 => 3,
            13 => 6,
            14 => 21,
            22 => 20,
            _ => 1
        };

    private static bool TryMapSemanticToken(CfgsSymbol symbol, bool isDeclaration, out CfgsSemanticTokenSpan span)
    {
        int tokenType = symbol.Kind switch
        {
            3 => 0,
            5 => 1,
            11 => 2,
            10 => 3,
            22 => 4,
            12 => 5,
            6 => 6,
            13 when IsParameterSymbol(symbol) => 7,
            13 => 8,
            14 => 8,
            _ => -1
        };

        if (tokenType < 0)
        {
            span = default;
            return false;
        }

        int modifiers = 0;
        if (isDeclaration)
            modifiers |= 1 << 0; // declaration
        if (symbol.Kind == 14)
            modifiers |= 1 << 1; // readonly
        if (symbol.Detail.Contains("static", StringComparison.OrdinalIgnoreCase))
            modifiers |= 1 << 2; // static
        if (symbol.Detail.StartsWith("async ", StringComparison.Ordinal))
            modifiers |= 1 << 3; // async

        span = new CfgsSemanticTokenSpan(
            symbol.SelectionRange.Start.Line,
            symbol.SelectionRange.Start.Character,
            Math.Max(symbol.SelectionRange.End.Character - symbol.SelectionRange.Start.Character, 1),
            tokenType,
            modifiers);

        return true;
    }

    private static bool IsParameterSymbol(CfgsSymbol symbol)
        => symbol.Detail.Contains("parameter", StringComparison.Ordinal);

    private static IReadOnlyList<CfgsSymbol> FindTypeImplementations(CfgsAnalysisResult result, CfgsSymbol typeSymbol)
    {
        HashSet<string> candidates = new(StringComparer.Ordinal)
        {
            typeSymbol.Name,
            typeSymbol.QualifiedName
        };

        return result.AllSymbols
            .Concat(result.SemanticModel.Symbols)
            .Where(candidate => (typeSymbol.Kind == 11 ? candidate.Kind is 5 or 11 : candidate.Kind == 5) &&
                                !candidate.IsSynthetic &&
                                !string.Equals(candidate.QualifiedName, typeSymbol.QualifiedName, StringComparison.Ordinal) &&
                                DeclaresTypeReference(candidate.Detail, candidates))
            .GroupBy(candidate => candidate.QualifiedName, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToList();
    }

    private static string? TryGetContainingTypeQualifiedName(string qualifiedName)
    {
        if (string.IsNullOrWhiteSpace(qualifiedName))
            return null;

        int lastDot = qualifiedName.LastIndexOf('.');
        return lastDot > 0 ? qualifiedName[..lastDot] : null;
    }

    private static bool DeclaresTypeReference(string detail, HashSet<string> candidates)
        => ExtractDeclaredTypeReferences(detail).Any(candidates.Contains);

    private static IEnumerable<string> ExtractDeclaredTypeReferences(string detail)
    {
        int colonIndex = detail.IndexOf(':', StringComparison.Ordinal);
        if (colonIndex < 0 || colonIndex >= detail.Length - 1)
            yield break;

        string inheritanceClause = detail[(colonIndex + 1)..];
        foreach (string typeName in inheritanceClause.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(typeName))
                yield return typeName;
        }
    }

    private static string? FindSymbolValueType(CfgsAnalysisResult result, CfgsSymbol symbol)
    {
        if (symbol.Kind is 5 or 10 or 11)
            return symbol.QualifiedName;

        if (!string.IsNullOrWhiteSpace(symbol.ValueTypeQualifiedName))
            return symbol.ValueTypeQualifiedName;

        if (symbol.Detail.StartsWith("var ", StringComparison.Ordinal) || symbol.Detail.StartsWith("const ", StringComparison.Ordinal))
        {
            foreach (CfgsSymbol candidate in result.SemanticModel.Symbols)
            {
                if (candidate.Kind is 5 or 10 or 11 &&
                    !string.IsNullOrWhiteSpace(candidate.QualifiedName) &&
                    symbol.QualifiedName.StartsWith(candidate.QualifiedName + ".", StringComparison.Ordinal))
                    return candidate.QualifiedName;
            }
        }

        return null;
    }

    private static CfgsSymbol? FindEnclosingFunction(CfgsAnalysisResult result, string uri, int line, int character)
    {
        CfgsSymbol? best = null;
        int bestSize = int.MaxValue;

        foreach (CfgsSymbol symbol in result.SemanticModel.Symbols)
        {
            if (symbol.Kind is not (6 or 12))
                continue;

            if (!string.Equals(symbol.Uri, uri, GetPathComparison()))
                continue;

            if (!Contains(symbol.Range, line, character))
                continue;

            int size = (symbol.Range.End.Line - symbol.Range.Start.Line) * 10000 +
                       (symbol.Range.End.Character - symbol.Range.Start.Character);
            if (size < bestSize)
            {
                bestSize = size;
                best = symbol;
            }
        }

        return best;
    }

    private static CfgsSymbol? FindSmallestEnclosingSymbol(IReadOnlyList<CfgsSymbol> symbols, int line)
    {
        foreach (CfgsSymbol symbol in symbols)
        {
            if (symbol.Range.Start.Line <= line && symbol.Range.End.Line >= line)
            {
                CfgsSymbol? child = FindSmallestEnclosingSymbol(symbol.Children, line);
                return child ?? symbol;
            }
        }

        return null;
    }

    private void CollectCodeLenses(CfgsAnalysisResult result, CfgsSymbol symbol, List<CfgsCodeLens> lenses)
    {
        if (symbol.Kind is 5 or 6 or 10 or 12 && !symbol.IsSynthetic)
        {
            int refCount = 0;
            CfgsSymbol? resolved = FindEquivalentSemanticSymbol(result, symbol);
            if (resolved is not null && !string.IsNullOrWhiteSpace(resolved.Id))
            {
                refCount = result.SemanticModel.Occurrences
                    .Count(o => string.Equals(o.SymbolId, resolved.Id, StringComparison.Ordinal) && !o.IsDeclaration);
            }

            lenses.Add(new CfgsCodeLens(symbol.SelectionRange, $"{refCount} reference{(refCount == 1 ? "" : "s")}"));
        }

        foreach (CfgsSymbol child in symbol.Children)
            CollectCodeLenses(result, child, lenses);
    }

    private static string? TryResolveImportUri(string origin, string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return null;

        if (Uri.TryCreate(rawPath, UriKind.Absolute, out Uri? absoluteImportUri))
        {
            if (!absoluteImportUri.IsFile)
                return absoluteImportUri.AbsoluteUri;

            string absoluteImportPath = Path.GetFullPath(absoluteImportUri.LocalPath);
            return File.Exists(absoluteImportPath) ? new Uri(absoluteImportPath).AbsoluteUri : null;
        }

        string? sourceDir = Path.GetDirectoryName(origin);
        if (string.IsNullOrWhiteSpace(sourceDir))
            return null;

        string candidatePath = Path.IsPathFullyQualified(rawPath)
            ? rawPath
            : Path.Combine(sourceDir, rawPath.Replace('/', Path.DirectorySeparatorChar));
        string fullPath = Path.GetFullPath(candidatePath);

        return File.Exists(fullPath) ? new Uri(fullPath).AbsoluteUri : null;
    }

    private static (string RawPath, int StartChar, int EndChar)? TryMatchImportPath(string lineText)
    {
        foreach (string pattern in new[]
                 {
                     @"\bfrom\s+""(?<path>[^""]+)""",
                     @"\bfrom\s+'(?<path>[^']+)'",
                     @"^\s*import\s+""(?<path>[^""]+)""",
                     @"^\s*import\s+'(?<path>[^']+)'"
                 })
        {
            Match match = Regex.Match(lineText, pattern);
            if (!match.Success)
                continue;

            Group pathGroup = match.Groups["path"];
            return (pathGroup.Value, pathGroup.Index, pathGroup.Index + pathGroup.Length);
        }

        return null;
    }

    private IReadOnlyList<CfgsCompletionItem> GetMetadataDotCompletions(CfgsAnalysisResult result, string prefix, string typedPrefix)
    {
        Dictionary<string, CfgsCompletionItem> items = new(StringComparer.Ordinal);

        foreach (CfgsCompletionItem item in GetCfgsVariableMemberCompletions(result, prefix, typedPrefix))
            items.TryAdd(item.Label, item);

        foreach (string nativeAccessPath in TryResolveNativeVariableAccessPaths(prefix, result.SourceText, result.Origin, new HashSet<string>(StringComparer.Ordinal)))
        {
            foreach (CfgsCompletionItem item in TryGetNativePathCompletions(nativeAccessPath, result.SourceText, result.Origin))
            {
                if (MatchesCompletionPrefix(item.Label, typedPrefix))
                    items.TryAdd(item.Label, item);
            }
        }

        foreach (CfgsCompletionItem item in GetNativeInteropDotCompletions(result, prefix, typedPrefix))
            items.TryAdd(item.Label, item);

        foreach (CfgsCompletionItem item in GetImportedPluginDotCompletions(result, prefix, typedPrefix))
            items.TryAdd(item.Label, item);

        return items.Values.OrderBy(static item => item.Label, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IReadOnlyList<CfgsCompletionItem> GetCfgsVariableMemberCompletions(CfgsAnalysisResult result, string variableName, string typedPrefix)
    {
        CfgsSymbol? variableSymbol = result.SemanticModel.Symbols
            .Where(symbol =>
                symbol.Kind is 13 or 14 &&
                string.Equals(symbol.Name, variableName, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(symbol.ValueTypeQualifiedName))
            .OrderByDescending(symbol => symbol.SelectionRange.Start.Line)
            .FirstOrDefault();

        if (variableSymbol is null)
            return [];

        string typePrefix = variableSymbol.ValueTypeQualifiedName! + ".";
        Dictionary<string, CfgsCompletionItem> items = new(StringComparer.Ordinal);
        foreach (CfgsSymbol symbol in result.SemanticModel.Symbols.Concat(result.AllSymbols))
        {
            if (symbol.IsSynthetic ||
                string.IsNullOrWhiteSpace(symbol.QualifiedName) ||
                !symbol.QualifiedName.StartsWith(typePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            string memberName = symbol.QualifiedName[typePrefix.Length..];
            if (string.IsNullOrWhiteSpace(memberName) ||
                memberName.Contains('.', StringComparison.Ordinal) ||
                !MatchesCompletionPrefix(memberName, typedPrefix))
            {
                continue;
            }

            items.TryAdd(memberName, new CfgsCompletionItem(memberName, ToCompletionKind(symbol.Kind), symbol.Detail));
        }

        return items.Values.OrderBy(static item => item.Label, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private IReadOnlyList<CfgsCompletionItem> AddImportedPluginTopLevelCompletions(CfgsAnalysisResult result, IReadOnlyList<CfgsCompletionItem> baseItems)
    {
        Dictionary<string, CfgsCompletionItem> items = baseItems.ToDictionary(static item => item.Label, static item => item, StringComparer.Ordinal);

        foreach (PluginCompletionIndex plugin in GetImportedPluginIndexes(result))
        {
            foreach (string root in plugin.NamespaceRoots)
                items.TryAdd(root, new CfgsCompletionItem(root, 9, "plugin namespace"));

            foreach (BuiltinDescriptor builtin in plugin.Builtins)
                items.TryAdd(builtin.Name, new CfgsCompletionItem(builtin.Name, 3, $"plugin builtin: {FormatArity(builtin.Name, builtin.ArityMin, builtin.ArityMax)}"));
        }

        return items.Values.OrderBy(static item => item.Label, StringComparer.Ordinal).ToList();
    }

    private IReadOnlyList<CfgsCompletionItem> GetImportedPluginDotCompletions(CfgsAnalysisResult result, string prefix, string typedPrefix)
    {
        Dictionary<string, CfgsCompletionItem> items = new(StringComparer.Ordinal);
        foreach (PluginCompletionIndex plugin in GetImportedPluginIndexes(result))
        {
            foreach (NamespaceDescriptor descriptor in plugin.Namespaces)
            {
                AddPluginNamespaceCompletionItems(descriptor, prefix, typedPrefix, items);
            }
        }

        return items.Values.OrderBy(static item => item.Label, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void AddPluginNamespaceCompletionItems(
        NamespaceDescriptor descriptor,
        string prefix,
        string typedPrefix,
        Dictionary<string, CfgsCompletionItem> items)
    {
        if (string.Equals(descriptor.Path, prefix, StringComparison.Ordinal))
        {
            foreach (string member in descriptor.Members)
            {
                if (MatchesCompletionPrefix(member, typedPrefix))
                    items.TryAdd(member, new CfgsCompletionItem(member, 2, $"plugin namespace member: {descriptor.Path}.{member}"));
            }

            return;
        }

        string searchPrefix = prefix + ".";
        if (!descriptor.Path.StartsWith(searchPrefix, StringComparison.Ordinal))
            return;

        string remainder = descriptor.Path[searchPrefix.Length..];
        int dot = remainder.IndexOf('.');
        string child = dot < 0 ? remainder : remainder[..dot];
        if (!string.IsNullOrWhiteSpace(child) && MatchesCompletionPrefix(child, typedPrefix))
            items.TryAdd(child, new CfgsCompletionItem(child, 9, $"plugin namespace: {searchPrefix}{child}"));
    }

    private IReadOnlyList<CfgsCompletionItem> GetNativeInteropDotCompletions(CfgsAnalysisResult result, string prefix, string typedPrefix)
    {
        string[] parts = prefix.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return [];

        NativeImportInfo? import = GetNativeImports(result).FirstOrDefault(i => string.Equals(i.Alias, parts[0], StringComparison.Ordinal));
        if (import is null)
            return [];

        NativeAssemblyIndex? index = NativeAssemblyIndex.TryCreate(import.Value.Path);
        if (index is null)
            return [];

        string container = parts.Length == 1 ? string.Empty : string.Join(".", parts.Skip(1));
        if (container.StartsWith(NativeAssemblyIndex.NativePathPrefix, StringComparison.Ordinal))
            container = container[(NativeAssemblyIndex.NativePathPrefix.Length)..];
        return index.GetCompletions(container, typedPrefix);
    }

    public static IReadOnlyList<string> TryGetNativeReturnTypePaths(string expressionPath, string sourceText, string origin)
    {
        if (string.IsNullOrWhiteSpace(expressionPath))
            return [];

        string[] parts = expressionPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return [];

        string alias = parts[0];
        string? assemblyPath = GetNativeImportPath(sourceText, origin, alias);
        if (assemblyPath is null)
            return [];

        NativeAssemblyIndex? index = NativeAssemblyIndex.TryCreate(assemblyPath);
        if (index is null)
            return [];

        string memberPath = string.Join(".", parts.Skip(1));
        return index.GetReturnTypePaths(memberPath)
            .Select(path => NativeAssemblyIndex.CreateNativePath(alias, path))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public static IReadOnlyList<CfgsCompletionItem> TryGetNativePathCompletions(string nativeAccessPath, string sourceText, string origin)
    {
        if (string.IsNullOrWhiteSpace(nativeAccessPath))
            return [];

        int firstDot = nativeAccessPath.IndexOf('.');
        if (firstDot <= 0)
            return [];

        string alias = nativeAccessPath[..firstDot];
        string container = nativeAccessPath[(firstDot + 1)..];
        if (!container.StartsWith(NativeAssemblyIndex.NativePathPrefix, StringComparison.Ordinal))
            return [];

        string? assemblyPath = GetNativeImportPath(sourceText, origin, alias);
        if (assemblyPath is null)
            return [];

        NativeAssemblyIndex? index = NativeAssemblyIndex.TryCreate(assemblyPath);
        return index?.GetCompletions(container, typedPrefix: "") ?? [];
    }

    private static IReadOnlyList<string> TryResolveNativeVariableAccessPaths(string variableName, string sourceText, string origin, HashSet<string> seen)
    {
        if (string.IsNullOrWhiteSpace(variableName) || variableName.Contains('.', StringComparison.Ordinal) || !seen.Add(variableName))
            return [];

        List<string> results = [];
        foreach (string assignedExpression in FindAssignedExpressions(variableName, sourceText))
        {
            foreach (string resolved in TryResolveNativeExpressionAccessPaths(assignedExpression, sourceText, origin, seen))
                results.Add(resolved);
        }

        return results.Distinct(StringComparer.Ordinal).ToList();
    }

    private static IReadOnlyList<string> TryResolveNativeExpressionAccessPaths(string expression, string sourceText, string origin, HashSet<string> seen)
    {
        string normalized = expression.Trim();
        if (normalized.Length == 0)
            return [];

        Match callMatch = Regex.Match(normalized, @"^(?<target>[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s*\(");
        string targetPath = callMatch.Success ? callMatch.Groups["target"].Value : normalized;
        targetPath = targetPath.Trim().TrimEnd(';');

        List<string> direct = TryGetNativeReturnTypePaths(targetPath, sourceText, origin).ToList();
        if (direct.Count > 0)
            return direct;

        string[] parts = targetPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return [];

        string root = parts[0];
        string suffix = string.Join(".", parts.Skip(1));
        List<string> viaVariable = [];
        foreach (string rootAccessPath in TryResolveNativeVariableAccessPaths(root, sourceText, origin, seen))
        {
            string expressionPath = $"{rootAccessPath}.{suffix}";
            viaVariable.AddRange(TryGetNativeReturnTypePaths(expressionPath, sourceText, origin));
            if (viaVariable.Count == 0 && expressionPath.Contains(NativeAssemblyIndex.NativePathPrefix, StringComparison.Ordinal))
                viaVariable.Add(expressionPath);
        }

        return viaVariable.Distinct(StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<string> FindAssignedExpressions(string variableName, string sourceText)
    {
        string escaped = Regex.Escape(variableName);
        string pattern = $@"(?:^|[;\r\n])\s*(?:(?:var|const)\s+)?{escaped}\s*=\s*(?<expr>[^;\r\n]+)";
        foreach (Match match in Regex.Matches(sourceText, pattern, RegexOptions.CultureInvariant))
        {
            string expr = match.Groups["expr"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(expr))
                yield return expr;
        }
    }

    private IReadOnlyList<CfgsCompletionItem> GetImportPathCompletions(CfgsAnalysisResult result, ImportPathCompletionRequest request)
    {
        string typedPath = request.TypedPath.Replace('\\', '/');
        int separator = typedPath.LastIndexOf('/');
        string directoryPart = separator >= 0 ? typedPath[..(separator + 1)] : string.Empty;
        string segmentPrefix = separator >= 0 ? typedPath[(separator + 1)..] : typedPath;
        string? baseDirectory = ResolveCompletionDirectory(result.Origin, directoryPart);
        if (baseDirectory is null || !Directory.Exists(baseDirectory))
            return [];

        Dictionary<string, CfgsCompletionItem> items = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (string directory in Directory.EnumerateDirectories(baseDirectory))
            {
                string name = Path.GetFileName(directory);
                if (string.IsNullOrWhiteSpace(name) || !name.StartsWith(segmentPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                string insertText = name[segmentPrefix.Length..] + "/";
                items.TryAdd(name + "/", new CfgsCompletionItem(name + "/", 19, "directory", insertText));
            }

            foreach (string file in Directory.EnumerateFiles(baseDirectory))
            {
                string extension = Path.GetExtension(file);
                if (!string.Equals(extension, ".cfs", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(extension, ".cfb", StringComparison.OrdinalIgnoreCase))
                    continue;

                string name = Path.GetFileName(file);
                if (string.IsNullOrWhiteSpace(name) || !name.StartsWith(segmentPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                string insertText = name[segmentPrefix.Length..];
                int kind = string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase) ? 17 : 17;
                items.TryAdd(name, new CfgsCompletionItem(name, kind, extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ? "DLL import" : "CFGS source file", insertText));
            }
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }

        return items.Values.OrderBy(static item => item.Label, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private IReadOnlyList<CfgsCompletionItem> GetNamedImportCompletions(CfgsAnalysisResult result, NamedImportCompletionRequest request)
    {
        string? resolved = ResolveImportPath(result.Origin, request.ImportPath);
        if (resolved is null || !File.Exists(resolved) || !string.Equals(Path.GetExtension(resolved), ".cfs", StringComparison.OrdinalIgnoreCase))
            return [];

        HashSet<string> alreadyImported = request.AlreadyImported;
        return ExtractExportedSymbols(resolved)
            .Where(item => MatchesCompletionPrefix(item.Label, request.TypedPrefix) && !alreadyImported.Contains(item.Label))
            .OrderBy(static item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<CfgsCompletionItem> ExtractExportedSymbols(string path)
    {
        string source;
        try
        {
            source = File.ReadAllText(path);
        }
        catch
        {
            return [];
        }

        List<CfgsCompletionItem> result = [];
        string[] lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        foreach (string line in lines)
        {
            Match match = Regex.Match(
                line,
                @"^\s*export\s+(?:public\s+|private\s+|protected\s+)?(?:(?<async>async)\s+)?(?<kind>func|class|interface|enum|var|const)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)",
                RegexOptions.CultureInvariant);
            if (!match.Success)
                continue;

            string name = match.Groups["name"].Value;
            string kind = match.Groups["kind"].Value;
            int completionKind = kind switch
            {
                "func" => 3,
                "class" => 7,
                "interface" => 8,
                "enum" => 13,
                "const" => 21,
                _ => 6
            };
            result.Add(new CfgsCompletionItem(name, completionKind, $"exported {kind} from {Path.GetFileName(path)}"));
        }

        return result
            .GroupBy(static item => item.Label, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToList();
    }

    private IReadOnlyList<PluginCompletionIndex> GetImportedPluginIndexes(CfgsAnalysisResult result)
    {
        List<PluginCompletionIndex> indexes = [];
        foreach (string pluginPath in GetImportedPluginPaths(result.SourceText, result.Origin))
        {
            PluginCompletionIndex? index = PluginCompletionIndex.TryCreate(pluginPath);
            if (index is not null)
                indexes.Add(index);
        }

        return indexes;
    }

    private static IEnumerable<string> GetImportedPluginPaths(string sourceText, string origin)
    {
        foreach (Match match in Regex.Matches(sourceText, @"^\s*import\s+(?!native\b)[""'](?<path>[^""']+\.dll)[""']", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            string rawPath = match.Groups["path"].Value;
            string? resolved = ResolveImportPath(origin, rawPath);
            if (!string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved))
                yield return resolved!;
        }
    }

    private static IReadOnlyList<NativeImportInfo> GetNativeImports(CfgsAnalysisResult result)
    {
        List<NativeImportInfo> imports = [];
        foreach (Match match in Regex.Matches(result.SourceText, @"^\s*import\s+native\s+[""'](?<path>[^""']+)[""']\s+as\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            string rawPath = match.Groups["path"].Value;
            string? resolved = ResolveImportPath(result.Origin, rawPath);
            if (!string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved))
                imports.Add(new NativeImportInfo(match.Groups["alias"].Value, resolved!));
        }

        return imports;
    }

    private static string? GetNativeImportPath(string sourceText, string origin, string alias)
    {
        foreach (Match match in Regex.Matches(sourceText, @"^\s*import\s+native\s+[""'](?<path>[^""']+)[""']\s+as\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (!string.Equals(match.Groups["alias"].Value, alias, StringComparison.Ordinal))
                continue;

            string? resolved = ResolveImportPath(origin, match.Groups["path"].Value);
            if (!string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved))
                return resolved;
        }

        return null;
    }

    private static string? ResolveCompletionDirectory(string origin, string directoryPart)
    {
        string? sourceDir = Path.GetDirectoryName(origin);
        if (string.IsNullOrWhiteSpace(sourceDir))
            return null;

        string normalized = directoryPart.Replace('/', Path.DirectorySeparatorChar);
        string candidate = Path.IsPathFullyQualified(normalized)
            ? normalized
            : Path.Combine(sourceDir, normalized);
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(candidate);
        }
        catch
        {
            return null;
        }

        return fullPath;
    }

    private static string? ResolveImportPath(string origin, string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return null;

        if (Uri.TryCreate(rawPath, UriKind.Absolute, out Uri? uri) && uri.IsFile)
            return File.Exists(uri.LocalPath) ? Path.GetFullPath(uri.LocalPath) : null;

        if (Path.IsPathFullyQualified(rawPath))
        {
            string full = Path.GetFullPath(rawPath);
            return File.Exists(full) ? full : null;
        }

        string? sourceDir = Path.GetDirectoryName(origin);
        List<string> searchDirs = [];
        if (!string.IsNullOrWhiteSpace(sourceDir))
        {
            searchDirs.Add(sourceDir!);
            searchDirs.AddRange(GetPackagePluginDirectories(sourceDir!));
        }

        string? workingDirectory = FrontendPipeline.TryGetWorkingDirectory(origin);
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            searchDirs.Add(workingDirectory!);
            searchDirs.AddRange(GetPackagePluginDirectories(workingDirectory!));
        }

        searchDirs.Add(Directory.GetCurrentDirectory());
        searchDirs.Add(AppContext.BaseDirectory);
        searchDirs.Add(Path.Combine(AppContext.BaseDirectory, "plugins"));

        foreach (string dir in searchDirs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string candidate = Path.GetFullPath(Path.Combine(dir, rawPath.Replace('/', Path.DirectorySeparatorChar)));
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static IEnumerable<string> GetPackagePluginDirectories(string startDirectory)
    {
        string? current = startDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            string packagePluginDirectory = Path.Combine(current, ".cfgs", "plugins");
            if (Directory.Exists(packagePluginDirectory))
                yield return packagePluginDirectory;

            current = Path.GetDirectoryName(current);
        }
    }

    private static string GetLineText(string sourceText, int line)
    {
        string normalized = sourceText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        string[] lines = normalized.Split('\n');
        return line >= 0 && line < lines.Length ? lines[line] : string.Empty;
    }

    private static bool TryFindImportPathCompletionRequest(string sourceText, int line, int character, out ImportPathCompletionRequest request)
    {
        request = default;
        string lineText = GetLineText(sourceText, line);
        int cursor = Math.Clamp(character, 0, lineText.Length);
        int quoteStart = -1;
        char quote = '\0';
        for (int i = cursor - 1; i >= 0; i--)
        {
            if (lineText[i] is '"' or '\'')
            {
                quoteStart = i;
                quote = lineText[i];
                break;
            }
        }

        if (quoteStart < 0)
            return false;

        int quoteEnd = lineText.IndexOf(quote, quoteStart + 1);
        if (quoteEnd >= 0 && quoteEnd < cursor)
            return false;

        string beforeQuote = lineText[..quoteStart];
        if (!Regex.IsMatch(beforeQuote, @"\b(import|from|native)\s*$", RegexOptions.CultureInvariant))
            return false;

        request = new ImportPathCompletionRequest(lineText[(quoteStart + 1)..cursor]);
        return true;
    }

    private static bool TryFindNamedImportCompletionRequest(string sourceText, int line, int character, out NamedImportCompletionRequest request)
    {
        request = default;
        string lineText = GetLineText(sourceText, line);
        int cursor = Math.Clamp(character, 0, lineText.Length);
        int openBrace = lineText.LastIndexOf('{', Math.Max(0, cursor - 1));
        int closeBrace = lineText.IndexOf('}', openBrace >= 0 ? openBrace : 0);
        if (openBrace < 0 || closeBrace < 0 || cursor <= openBrace || cursor > closeBrace)
            return false;

        Match pathMatch = Regex.Match(lineText[(closeBrace + 1)..], @"\bfrom\s+[""'](?<path>[^""']+)[""']");
        if (!pathMatch.Success)
            return false;

        string importPath = pathMatch.Groups["path"].Value;
        string inside = lineText[(openBrace + 1)..cursor];
        int lastComma = inside.LastIndexOf(',');
        string current = lastComma >= 0 ? inside[(lastComma + 1)..] : inside;
        current = current.TrimStart();
        string typedPrefix = Regex.Match(current, @"[A-Za-z_][A-Za-z0-9_]*$").Value;

        HashSet<string> alreadyImported = new(StringComparer.Ordinal);
        string fullInside = lineText[(openBrace + 1)..closeBrace];
        foreach (Match nameMatch in Regex.Matches(fullInside, @"\b[A-Za-z_][A-Za-z0-9_]*\b"))
        {
            string value = nameMatch.Value;
            if (!string.Equals(value, "as", StringComparison.Ordinal))
                alreadyImported.Add(value);
        }

        if (!string.IsNullOrWhiteSpace(typedPrefix))
            alreadyImported.Remove(typedPrefix);

        request = new NamedImportCompletionRequest(importPath, typedPrefix, alreadyImported);
        return true;
    }

    private static bool MatchesCompletionPrefix(string value, string typedPrefix)
        => string.IsNullOrWhiteSpace(typedPrefix) || value.StartsWith(typedPrefix, StringComparison.OrdinalIgnoreCase);

    private static string FormatArity(string name, int min, int max)
        => max < 0 ? $"{name}({min}+ args)" : min == max ? $"{name}({min} args)" : $"{name}({min}..{max} args)";

    private static MemberCompletionRequest? TryFindMemberCompletionRequest(string sourceText, int line, int character)
    {
        string[] lines = sourceText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        if (line < 0 || line >= lines.Length)
            return null;

        string lineText = lines[line];
        int cursor = Math.Min(Math.Max(character, 0), lineText.Length);
        int memberStart = cursor;
        while (memberStart > 0 && IsIdentifierChar(lineText[memberStart - 1]))
            memberStart--;

        int dotIndex = memberStart - 1;
        if (dotIndex < 0 || dotIndex >= lineText.Length || lineText[dotIndex] != '.')
            return null;

        string typedPrefix = lineText[memberStart..cursor];
        return new MemberCompletionRequest(line, memberStart, typedPrefix);
    }

    private IReadOnlyList<CfgsCompletionItem> GetSemanticDotCompletions(CfgsAnalysisResult result, MemberCompletionRequest request)
    {
        List<CfgsResolvedCompletionTarget> candidates = result.SemanticModel.CompletionTargets
            .Where(target =>
                string.Equals(target.Uri, result.DocumentUri, GetPathComparison()) &&
                target.End.Line == request.TargetLine &&
                target.End.Character == request.TargetCharacter)
            .ToList();

        if (candidates.Count == 0)
            return [];

        return candidates
            .SelectMany(static target => target.Items)
            .Where(item =>
                string.IsNullOrWhiteSpace(request.TypedPrefix) ||
                item.Label.StartsWith(request.TypedPrefix, StringComparison.OrdinalIgnoreCase))
            .GroupBy(static item => item.Label, StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? GetDotPrefix(string sourceText, int line, int character)
    {
        string[] lines = sourceText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        if (line < 0 || line >= lines.Length)
            return null;

        string lineText = lines[line];
        int dotIndex = character - 1;
        if (dotIndex < 0 || dotIndex >= lineText.Length || lineText[dotIndex] != '.')
            return null;

        int end = dotIndex;
        int start = end - 1;
        while (start >= 0 && (char.IsLetterOrDigit(lineText[start]) || lineText[start] == '_' || lineText[start] == '.'))
            start--;
        start++;

        string prefix = lineText[start..end];
        return string.IsNullOrWhiteSpace(prefix) ? null : prefix;
    }

    private static string? GetDotContainerPrefix(string sourceText, int line, int memberStartCharacter)
    {
        string[] lines = sourceText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        if (line < 0 || line >= lines.Length)
            return null;

        string lineText = lines[line];
        int dotIndex = memberStartCharacter - 1;
        if (dotIndex < 0 || dotIndex >= lineText.Length || lineText[dotIndex] != '.')
            return null;

        int end = dotIndex;
        int start = end - 1;
        while (start >= 0 && (char.IsLetterOrDigit(lineText[start]) || lineText[start] == '_' || lineText[start] == '.'))
            start--;
        start++;

        string prefix = lineText[start..end];
        return string.IsNullOrWhiteSpace(prefix) ? null : prefix;
    }

    private IReadOnlyList<CfgsCompletionItem> GetDotCompletions(CfgsAnalysisResult result, string prefix, string typedPrefix = "")
    {
        string searchPrefix = prefix + ".";

        Dictionary<string, CfgsCompletionItem> items = new(StringComparer.Ordinal);

        if (StandardLibraryNamespaceMembers.TryGetValue(prefix, out CfgsCompletionItem[]? stdlibMembers))
        {
            foreach (CfgsCompletionItem item in stdlibMembers)
            {
                if (string.IsNullOrWhiteSpace(typedPrefix) ||
                    item.Label.StartsWith(typedPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    items.TryAdd(item.Label, item);
                }
            }
        }

        foreach (CfgsSymbol symbol in result.AllSymbols.Concat(result.SemanticModel.Symbols))
        {
            if (symbol.IsSynthetic)
                continue;

            if (!symbol.QualifiedName.StartsWith(searchPrefix, StringComparison.Ordinal))
                continue;

            string remainder = symbol.QualifiedName[searchPrefix.Length..];
            if (remainder.Contains('.', StringComparison.Ordinal))
                continue;

            if (!string.IsNullOrWhiteSpace(typedPrefix) &&
                !remainder.StartsWith(typedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            items.TryAdd(remainder, new CfgsCompletionItem(remainder, ToCompletionKind(symbol.Kind), symbol.Detail));
        }

        return items.Values.OrderBy(static item => item.Label, StringComparer.Ordinal).ToList();
    }

    private static bool IsIdentifierChar(char ch)
        => char.IsLetterOrDigit(ch) || ch == '_';

    private static bool IsQualifiedIdentifierChar(char ch)
        => IsIdentifierChar(ch) || ch == '.';

    private sealed class StringTupleComparer : IEqualityComparer<(string, string)>
    {
        public static readonly StringTupleComparer Instance = new();

        public bool Equals((string, string) x, (string, string) y)
            => string.Equals(x.Item1, y.Item1, StringComparison.Ordinal) &&
               string.Equals(x.Item2, y.Item2, GetPathComparison());

        public int GetHashCode((string, string) obj)
        {
            StringComparer ordinal = StringComparer.Ordinal;
            StringComparer pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            return HashCode.Combine(ordinal.GetHashCode(obj.Item1), pathComparer.GetHashCode(obj.Item2));
        }
    }

    private static void EnsureDocumentEntry(Dictionary<string, List<CfgsDiagnostic>> diagnosticsByUri, string documentUri)
    {
        if (!diagnosticsByUri.ContainsKey(documentUri))
            diagnosticsByUri[documentUri] = [];
    }

    private static void AddExceptionDiagnostic(
        Dictionary<string, List<CfgsDiagnostic>> diagnosticsByUri,
        SourceResolver sources,
        string fallbackDocumentUri,
        string? origin,
        int line,
        int column,
        string message,
        string code,
        int? spanLength = null)
    {
        string uri = sources.ToDocumentUri(origin, fallbackDocumentUri);
        RangeInfo range = TextLocator.CreateRange(
            sources.TryGetSource(origin, out string? text) ? text : null,
            line,
            column,
            null,
            spanLength);

        if (!diagnosticsByUri.TryGetValue(uri, out List<CfgsDiagnostic>? list))
        {
            list = [];
            diagnosticsByUri[uri] = list;
        }

        list.Add(new CfgsDiagnostic(range, message.Trim(), 1, "cfgs", code));
    }

    private static string InferDiagnosticCode(string category, string? message)
    {
        string m = (message ?? string.Empty).Trim().ToLowerInvariant();

        if (m.Contains(".cfb", StringComparison.Ordinal) ||
            m.Contains("binary", StringComparison.Ordinal) ||
            m.Contains("bytecode", StringComparison.Ordinal))
            return "CFGS5000";

        if (category.Equals("LexError", StringComparison.OrdinalIgnoreCase))
            return "CFGS0001";

        if (category.Equals("ParseError", StringComparison.OrdinalIgnoreCase))
        {
            if (m.Contains("expected", StringComparison.Ordinal))
                return "CFGS1001";
            if (m.Contains("import", StringComparison.Ordinal))
                return "CFGS4001";
            if (m.Contains("duplicate", StringComparison.Ordinal))
                return "CFGS1002";
            return "CFGS1000";
        }

        if (category.Equals("CompileError", StringComparison.OrdinalIgnoreCase))
        {
            if (m.Contains("dot access", StringComparison.Ordinal) ||
                m.Contains("bracket access", StringComparison.Ordinal))
                return "CFGS2003";
            if (m.Contains("receiver", StringComparison.Ordinal))
                return "CFGS2004";
            if (m.Contains("unknown", StringComparison.Ordinal))
                return "CFGS2001";
            if (m.Contains("inaccessible", StringComparison.Ordinal))
                return "CFGS2002";
            if (m.Contains("duplicate", StringComparison.Ordinal))
                return "CFGS2005";
            if (m.Contains("override", StringComparison.Ordinal))
                return "CFGS2006";
            if (m.Contains("assignment", StringComparison.Ordinal) ||
                m.Contains("assign target", StringComparison.Ordinal) ||
                m.Contains("read-only", StringComparison.Ordinal))
                return "CFGS2007";
            if (m.Contains("invalid static member access", StringComparison.Ordinal) ||
                m.Contains("invalid instance member access", StringComparison.Ordinal) ||
                m.Contains("member is static", StringComparison.Ordinal) ||
                m.Contains("member is instance", StringComparison.Ordinal))
                return "CFGS2008";
            if (m.Contains("reserved", StringComparison.Ordinal))
                return "CFGS2009";
            if (m.Contains("interface", StringComparison.Ordinal))
                return "CFGS2010";
            if (m.Contains("constructor", StringComparison.Ordinal) ||
                m.Contains("base ctor", StringComparison.Ordinal))
                return "CFGS2011";
            if (m.Contains("property", StringComparison.Ordinal) ||
                m.Contains("backing-field", StringComparison.Ordinal) ||
                m.Contains("accessor", StringComparison.Ordinal))
                return "CFGS2012";
            if (m.Contains("ambiguous", StringComparison.Ordinal))
                return "CFGS2013";
            if (m.Contains("async", StringComparison.Ordinal) ||
                m.Contains("await", StringComparison.Ordinal) ||
                m.Contains("yield", StringComparison.Ordinal))
                return "CFGS2014";
            if (m.Contains("argument", StringComparison.Ordinal) ||
                m.Contains("parameter", StringComparison.Ordinal) ||
                m.Contains("spread", StringComparison.Ordinal) ||
                m.Contains("rest", StringComparison.Ordinal) ||
                m.Contains("default", StringComparison.Ordinal))
                return "CFGS2015";
            if (m.Contains("namespace", StringComparison.Ordinal))
                return "CFGS2016";
            return "CFGS2000";
        }

        if (category.Equals("RuntimeError", StringComparison.OrdinalIgnoreCase))
        {
            if (m.Contains("undefined variable", StringComparison.Ordinal))
                return "CFGS3001";
            if (m.Contains("not indexable", StringComparison.Ordinal))
                return "CFGS3003";
            if (m.Contains("index", StringComparison.Ordinal))
                return "CFGS3002";
            if (m.Contains("argument", StringComparison.Ordinal))
                return "CFGS3004";
            return "CFGS3000";
        }

        return "CFGS9000";
    }

    private static string GetDocumentOrigin(string documentUri)
    {
        string? path = SourceResolver.TryGetLocalPath(documentUri);
        return path ?? documentUri;
    }

    private static Dictionary<string, T> CreateUriMap<T>()
        => OperatingSystem.IsWindows()
            ? new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, T>(StringComparer.Ordinal);

    private sealed class SymbolCollector
    {
        private readonly SourceResolver _sources;
        private readonly string _documentOrigin;
        private readonly string _documentUri;
        private readonly HashSet<(int Line, int Column, string RootName)> _syntheticNamespaceRoots = new();

        public SymbolCollector(SourceResolver sources, string documentOrigin, string documentUri)
        {
            _sources = sources;
            _documentOrigin = NormalizeOrigin(documentOrigin);
            _documentUri = documentUri;
        }

        public List<CfgsSymbol> DocumentSymbols { get; } = [];

        public List<CfgsSymbol> AllSymbols { get; } = [];

        public void Collect(IEnumerable<Stmt> statements)
        {
            foreach (Stmt stmt in statements)
            {
                if (stmt is BlockStmt block && block.IsNamespaceScope && TryCreateNamespaceSymbol(block, out CfgsSymbol? namespaceSymbol))
                {
                    AddFlattenedSymbol(namespaceSymbol!);
                    if (IsCurrentDocument(block.OriginFile))
                        DocumentSymbols.Add(namespaceSymbol!);
                    continue;
                }

                if (stmt is VarDecl syntheticRoot && IsSyntheticNamespaceRoot(syntheticRoot))
                    continue;

                if (TryCreateSymbol(stmt, null, out CfgsSymbol? symbol))
                {
                    AddFlattenedSymbol(symbol!);
                    if (IsCurrentDocument(symbol!.OriginFile))
                        DocumentSymbols.Add(symbol);
                }
            }
        }

        private void AddFlattenedSymbol(CfgsSymbol symbol)
        {
            AllSymbols.Add(symbol);

            foreach (CfgsSymbol child in symbol.Children)
                AddFlattenedSymbol(child);
        }

        private bool IsSyntheticNamespaceRoot(VarDecl decl)
            => decl.Value is DictExpr &&
               _syntheticNamespaceRoots.Contains((decl.Line, decl.Col, decl.Name));

        private bool TryCreateNamespaceSymbol(BlockStmt block, out CfgsSymbol? namespaceSymbol)
        {
            namespaceSymbol = null;

            if (!TryExtractNamespaceInfo(block, out string? namespacePath, out string? tempName))
                return false;

            string rootName = namespacePath!.Split('.')[0];
            _syntheticNamespaceRoots.Add((block.Line, block.Col, rootName));

            List<CfgsSymbol> children = [];
            foreach (Stmt stmt in EnumerateNamespaceBodyStatements(block, tempName!))
            {
                if (TryCreateSymbol(stmt, namespacePath, out CfgsSymbol? child))
                    children.Add(child!);
            }

            string? source = _sources.GetSourceText(block.OriginFile);
            RangeInfo selectionRange = TextLocator.CreateRange(source, block.Line, block.Col, namespacePath);
            namespaceSymbol = new CfgsSymbol(
                namespacePath,
                namespacePath,
                3,
                _sources.ToDocumentUri(block.OriginFile, _documentUri),
                block.OriginFile,
                selectionRange,
                selectionRange,
                "namespace",
                $"```cfgs\nnamespace {namespacePath}\n```",
                children,
                IsSynthetic: false);

            return true;
        }

        private IEnumerable<Stmt> EnumerateNamespaceBodyStatements(BlockStmt block, string tempName)
        {
            foreach (Stmt stmt in block.Statements.Skip(1))
            {
                if (stmt is AssignExprStmt assign &&
                    assign.Target is IndexExpr target &&
                    target.Target is VarExpr varExpr &&
                    string.Equals(varExpr.Name, tempName, StringComparison.Ordinal) &&
                    assign.Value is VarExpr)
                {
                    continue;
                }

                yield return stmt;
            }
        }

        private bool TryExtractNamespaceInfo(BlockStmt block, out string? namespacePath, out string? tempName)
        {
            namespacePath = null;
            tempName = null;

            if (block.Statements.FirstOrDefault() is not VarDecl tempDecl)
                return false;

            tempName = tempDecl.Name;
            namespacePath = TextLocator.TryExtractQualifiedPath(tempDecl.Value);
            return !string.IsNullOrWhiteSpace(namespacePath);
        }

        private bool TryCreateSymbol(Stmt stmt, string? containerName, out CfgsSymbol? symbol)
        {
            symbol = null;
            Stmt effective = stmt is ExportStmt export ? export.Inner : stmt;

            switch (effective)
            {
                case FuncDeclStmt func:
                    symbol = CreateFunctionSymbol(func, containerName, isMethod: false);
                    return true;

                case ClassDeclStmt @class:
                    symbol = CreateClassSymbol(@class, containerName);
                    return true;

                case InterfaceDeclStmt @interface:
                    symbol = CreateInterfaceSymbol(@interface, containerName);
                    return true;

                case EnumDeclStmt @enum:
                    symbol = CreateEnumSymbol(@enum, containerName);
                    return true;

                case VarDecl variable:
                    symbol = CreateSimpleSymbol(variable.Name, containerName, variable.Line, variable.Col, variable.OriginFile, 13, $"var {variable.Name}");
                    return true;

                case ConstDecl constant:
                    symbol = CreateSimpleSymbol(constant.Name, containerName, constant.Line, constant.Col, constant.OriginFile, 14, $"const {constant.Name}");
                    return true;

                default:
                    return false;
            }
        }

        private CfgsSymbol CreateClassSymbol(ClassDeclStmt classDecl, string? containerName)
        {
            string qualifiedName = Qualify(containerName, classDecl.Name);
            List<CfgsSymbol> children = [];

            foreach (KeyValuePair<string, Expr?> field in classDecl.Fields)
            {
                bool isConst = classDecl.ConstFields.Contains(field.Key);
                int kind = isConst ? 14 : 13;
                string prefix = isConst ? "const" : "var";
                children.Add(CreateSimpleSymbol(field.Key, qualifiedName, classDecl.Line, classDecl.Col, classDecl.OriginFile, kind, $"{prefix} {field.Key}"));
            }

            foreach (KeyValuePair<string, Expr?> field in classDecl.StaticFields)
            {
                bool isConst = classDecl.StaticConstFields.Contains(field.Key);
                int kind = isConst ? 14 : 13;
                string prefix = isConst ? "static const" : "static var";
                children.Add(CreateSimpleSymbol(field.Key, qualifiedName, classDecl.Line, classDecl.Col, classDecl.OriginFile, kind, $"{prefix} {field.Key}"));
            }

            children.AddRange(classDecl.Properties.Select(property => CreatePropertySymbol(property, qualifiedName)));
            children.AddRange(classDecl.StaticProperties.Select(property => CreatePropertySymbol(property, qualifiedName)));
            children.AddRange(classDecl.Methods.Select(method => CreateFunctionSymbol(method, qualifiedName, isMethod: true)));
            children.AddRange(classDecl.StaticMethods.Select(method => CreateFunctionSymbol(method, qualifiedName, isMethod: true)));
            children.AddRange(classDecl.Enums.Select(@enum => CreateEnumSymbol(@enum, qualifiedName)));
            children.AddRange(classDecl.NestedClasses.Select(nested => CreateClassSymbol(nested, qualifiedName)));

            string header = BuildClassHeader(classDecl);

            return CreateSymbol(
                classDecl.Name,
                qualifiedName,
                classDecl.Line,
                classDecl.Col,
                classDecl.OriginFile,
                5,
                header,
                header,
                children);
        }

        private CfgsSymbol CreateInterfaceSymbol(InterfaceDeclStmt interfaceDecl, string? containerName)
        {
            string qualifiedName = Qualify(containerName, interfaceDecl.Name);
            List<CfgsSymbol> children =
                interfaceDecl.Methods
                    .Select(method => CreateInterfaceMethodSymbol(method, qualifiedName))
                    .ToList();

            string header = BuildInterfaceHeader(interfaceDecl);

            return CreateSymbol(
                interfaceDecl.Name,
                qualifiedName,
                interfaceDecl.Line,
                interfaceDecl.Col,
                interfaceDecl.OriginFile,
                11,
                header,
                header,
                children);
        }

        private CfgsSymbol CreateEnumSymbol(EnumDeclStmt enumDecl, string? containerName)
        {
            string qualifiedName = Qualify(containerName, enumDecl.Name);
            List<CfgsSymbol> children =
                enumDecl.Members
                    .Select(member => CreateSimpleSymbol(member.Name, qualifiedName, member.Line, member.Col, member.OriginFile, 22, $"enum member {member.Name}"))
                    .ToList();

            return CreateSymbol(
                enumDecl.Name,
                qualifiedName,
                enumDecl.Line,
                enumDecl.Col,
                enumDecl.OriginFile,
                10,
                $"enum {enumDecl.Name}",
                $"enum {enumDecl.Name}",
                children);
        }

        private CfgsSymbol CreateInterfaceMethodSymbol(InterfaceMethodDecl method, string? containerName)
        {
            string qualifiedName = Qualify(containerName, method.Name);
            string signature = BuildInterfaceMethodSignature(method);
            return CreateSymbol(
                method.Name,
                qualifiedName,
                method.Line,
                method.Col,
                method.OriginFile,
                6,
                signature,
                signature,
                []);
        }

        private CfgsSymbol CreateFunctionSymbol(FuncDeclStmt func, string? containerName, bool isMethod)
        {
            string qualifiedName = Qualify(containerName, func.Name);
            string signature = SignatureDisplay.BuildFunctionLabel(func.Name, func.IsAsync, func.Parameters, func.ParameterSpecs, func.RestParameter);
            return CreateSymbol(
                func.Name,
                qualifiedName,
                func.Line,
                func.Col,
                func.OriginFile,
                isMethod ? 6 : 12,
                signature,
                signature,
                []);
        }

        private CfgsSymbol CreatePropertySymbol(PropertyDeclStmt property, string? containerName)
        {
            string qualifiedName = Qualify(containerName, property.Name);
            string detail = property.IsStatic ? $"static property {property.Name}" : $"property {property.Name}";
            return CreateSymbol(
                property.Name,
                qualifiedName,
                property.Line,
                property.Col,
                property.OriginFile,
                7,
                detail,
                detail,
                []);
        }

        private CfgsSymbol CreateSimpleSymbol(string name, string? containerName, int line, int column, string originFile, int kind, string detail)
            => CreateSymbol(name, Qualify(containerName, name), line, column, originFile, kind, detail, detail, []);

        private CfgsSymbol CreateSymbol(
            string name,
            string qualifiedName,
            int line,
            int column,
            string originFile,
            int kind,
            string detail,
            string hoverHeader,
            List<CfgsSymbol> children)
        {
            string? source = _sources.GetSourceText(originFile);
            RangeInfo selectionRange = TextLocator.CreateRange(source, line, column, name);
            string uri = _sources.ToDocumentUri(originFile, _documentUri);

            return new CfgsSymbol(
                name,
                qualifiedName,
                kind,
                uri,
                originFile,
                selectionRange,
                selectionRange,
                detail,
                CfgsHoverText.Create(hoverHeader, uri, qualifiedName, kind, detail),
                children,
                IsSynthetic: false);
        }

        private bool IsCurrentDocument(string originFile)
            => string.Equals(NormalizeOrigin(originFile), _documentOrigin, GetPathComparison());

        private static string Qualify(string? containerName, string name)
            => string.IsNullOrWhiteSpace(containerName) ? name : $"{containerName}.{name}";

        private static string BuildClassHeader(ClassDeclStmt classDecl)
        {
            StringBuilder sb = new();
            sb.Append($"class {classDecl.Name}({string.Join(", ", classDecl.Parameters)})");

            List<string> inheritedTypes = [];
            if (!string.IsNullOrWhiteSpace(classDecl.BaseName))
                inheritedTypes.Add(classDecl.BaseName);
            inheritedTypes.AddRange(classDecl.ImplementedInterfaces.Where(static iface => !string.IsNullOrWhiteSpace(iface)));

            if (inheritedTypes.Count > 0)
                sb.Append($" : {string.Join(", ", inheritedTypes)}");

            return sb.ToString();
        }

        private static string BuildInterfaceHeader(InterfaceDeclStmt interfaceDecl)
        {
            StringBuilder sb = new();
            sb.Append($"interface {interfaceDecl.Name}");
            if (interfaceDecl.BaseInterfaces.Count > 0)
                sb.Append($" : {string.Join(", ", interfaceDecl.BaseInterfaces)}");

            return sb.ToString();
        }

        private static string BuildInterfaceMethodSignature(InterfaceMethodDecl method)
            => SignatureDisplay.BuildFunctionLabel(method.Name, method.IsAsync, method.Parameters, parameterSpecs: null, method.RestParameter);

        private static StringComparison GetPathComparison()
            => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        private static string NormalizeOrigin(string origin)
        {
            if (Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri) && uri.IsFile)
                return Path.GetFullPath(uri.LocalPath);

            if (Path.IsPathFullyQualified(origin))
                return Path.GetFullPath(origin);

            return origin;
        }
    }

    private sealed class SourceResolver
    {
        private readonly string _documentUri;
        private readonly string _documentOrigin;
        private readonly Dictionary<string, string> _sourceCache = OperatingSystem.IsWindows()
            ? new(StringComparer.OrdinalIgnoreCase)
            : new(StringComparer.Ordinal);

        public SourceResolver(string documentUri, string documentOrigin, string documentText, IReadOnlyDictionary<string, string>? openDocumentSources)
        {
            _documentUri = documentUri;
            _documentOrigin = NormalizeOrigin(documentOrigin);
            _sourceCache[_documentOrigin] = documentText;
            _sourceCache[_documentUri] = documentText;
            RegisterOverlaySources(openDocumentSources);
        }

        public string? GetOverlaySourceText(string? origin)
        {
            if (string.IsNullOrWhiteSpace(origin))
                return null;

            string normalized = NormalizeOrigin(origin);
            return _sourceCache.TryGetValue(normalized, out string? cached) ? cached : null;
        }

        public bool TryGetSource(string? origin, out string? text)
        {
            text = GetSourceText(origin);
            return text is not null;
        }

        public string? GetSourceText(string? origin)
        {
            if (string.IsNullOrWhiteSpace(origin))
                return null;

            string normalized = NormalizeOrigin(origin);
            if (_sourceCache.TryGetValue(normalized, out string? cached))
                return cached;

            if (File.Exists(normalized))
            {
                string source = File.ReadAllText(normalized);
                _sourceCache[normalized] = source;
                return source;
            }

            return null;
        }

        public string ToDocumentUri(string? origin, string fallbackDocumentUri)
        {
            if (string.IsNullOrWhiteSpace(origin))
                return fallbackDocumentUri;

            string normalized = NormalizeOrigin(origin);
            if (string.Equals(normalized, _documentOrigin, GetPathComparison()))
                return _documentUri;

            if (Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri))
                return uri.IsFile ? new Uri(Path.GetFullPath(uri.LocalPath)).AbsoluteUri : uri.AbsoluteUri;

            if (Path.IsPathFullyQualified(origin) || File.Exists(origin))
                return new Uri(Path.GetFullPath(origin)).AbsoluteUri;

            return fallbackDocumentUri;
        }

        public static string? TryGetLocalPath(string uri)
        {
            if (Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed))
            {
                if (parsed.IsFile)
                    return Path.GetFullPath(parsed.LocalPath);

                return null;
            }

            if (Path.IsPathFullyQualified(uri))
                return Path.GetFullPath(uri);

            return null;
        }

        private static string NormalizeOrigin(string origin)
        {
            if (Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri) && uri.IsFile)
                return Path.GetFullPath(uri.LocalPath);

            if (Path.IsPathFullyQualified(origin))
                return Path.GetFullPath(origin);

            return origin;
        }

        private void RegisterOverlaySources(IReadOnlyDictionary<string, string>? openDocumentSources)
        {
            if (openDocumentSources is null)
                return;

            foreach ((string key, string value) in openDocumentSources)
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                _sourceCache[NormalizeOrigin(key)] = value;
            }
        }

        private static StringComparison GetPathComparison()
            => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

}

internal sealed record CfgsAnalysisResult(
    string DocumentUri,
    string Origin,
    string SourceText,
    IReadOnlyDictionary<string, List<CfgsDiagnostic>> DiagnosticsByUri,
    IReadOnlyList<CfgsSymbol> DocumentSymbols,
    IReadOnlyList<CfgsSymbol> AllSymbols,
    CfgsSemanticModel SemanticModel);

internal sealed record CfgsDiagnostic(
    RangeInfo Range,
    string Message,
    int Severity,
    string Source,
    string Code);

internal sealed record CfgsCompletionItem(
    string Label,
    int Kind,
    string Detail,
    string? InsertText = null);

internal readonly record struct ImportPathCompletionRequest(string TypedPath);

internal readonly record struct NamedImportCompletionRequest(
    string ImportPath,
    string TypedPrefix,
    HashSet<string> AlreadyImported);

internal readonly record struct NativeImportInfo(string Alias, string Path);

internal sealed class PluginCompletionIndex
{
    private static readonly Dictionary<string, PluginCompletionIndex?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object SyncRoot = new();

    public IReadOnlyList<BuiltinDescriptor> Builtins { get; }

    public IReadOnlyList<(Type ReceiverType, IntrinsicDescriptor Descriptor)> Intrinsics { get; }

    public IReadOnlyList<NamespaceDescriptor> Namespaces { get; }

    public IReadOnlyList<string> NamespaceRoots { get; }

    private PluginCompletionIndex(
        IReadOnlyList<BuiltinDescriptor> builtins,
        IReadOnlyList<(Type ReceiverType, IntrinsicDescriptor Descriptor)> intrinsics,
        IReadOnlyList<NamespaceDescriptor> namespaces)
    {
        Builtins = builtins;
        Intrinsics = intrinsics;
        Namespaces = namespaces;
        NamespaceRoots = namespaces
            .Select(static ns => ns.Path.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
            .Where(static root => !string.IsNullOrWhiteSpace(root))
            .Select(static root => root!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static root => root, StringComparer.Ordinal)
            .ToList();
    }

    public static PluginCompletionIndex? TryCreate(string path)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }

        lock (SyncRoot)
        {
            if (Cache.TryGetValue(fullPath, out PluginCompletionIndex? cached))
                return cached;

            try
            {
                BuiltinRegistry builtins = new();
                IntrinsicRegistry intrinsics = new();
                NamespaceRegistry namespaces = new();
                PluginLoader.LoadDll(fullPath, builtins, intrinsics, namespaces);
                PluginCompletionIndex index = new(builtins.Snapshot(), intrinsics.Snapshot(), namespaces.Snapshot());
                Cache[fullPath] = index;
                return index;
            }
            catch
            {
                Cache[fullPath] = null;
                return null;
            }
        }
    }
}

internal sealed class NativeAssemblyIndex
{
    public const string NativePathPrefix = "native::";

    private static readonly Dictionary<string, NativeAssemblyIndex?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object SyncRoot = new();

    private readonly Dictionary<string, Type> _typesByPath;
    private readonly HashSet<string> _namespaces;

    private NativeAssemblyIndex(IEnumerable<Type> types)
    {
        _typesByPath = new Dictionary<string, Type>(StringComparer.Ordinal);
        _namespaces = new HashSet<string>(StringComparer.Ordinal);

        foreach (Type type in types)
        {
            string path = GetCfgsTypePath(type);
            if (string.IsNullOrWhiteSpace(path))
                continue;

            _typesByPath[path] = type;
            string? ns = type.Namespace;
            while (!string.IsNullOrWhiteSpace(ns))
            {
                _namespaces.Add(ns!);
                int dot = ns!.LastIndexOf('.');
                ns = dot < 0 ? null : ns[..dot];
            }
        }
    }

    public static NativeAssemblyIndex? TryCreate(string assemblyPath)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(assemblyPath);
        }
        catch
        {
            return null;
        }

        lock (SyncRoot)
        {
            if (Cache.TryGetValue(fullPath, out NativeAssemblyIndex? cached))
                return cached;

            try
            {
                CompletionAssemblyLoadContext context = new(fullPath);
                Assembly assembly = context.LoadFromAssemblyPath(fullPath);
                Type[] types;
                try
                {
                    types = assembly.GetExportedTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(static type => type is not null).Cast<Type>().Where(static type => type.IsPublic || type.IsNestedPublic).ToArray();
                }

                NativeAssemblyIndex index = new(types);
                Cache[fullPath] = index;
                return index;
            }
            catch
            {
                Cache[fullPath] = null;
                return null;
            }
        }
    }

    public IReadOnlyList<CfgsCompletionItem> GetCompletions(string container, string typedPrefix)
    {
        bool preferInstanceMembers = container.Contains(NativePathPrefix, StringComparison.Ordinal);
        string nativeContainer = StripNativePathPrefix(container);
        Dictionary<string, CfgsCompletionItem> items = new(StringComparer.Ordinal);

        if (_typesByPath.TryGetValue(nativeContainer, out Type? type))
        {
            AddTypeMemberCompletions(type, typedPrefix, items, preferInstanceMembers);
            return items.Values.OrderBy(static item => item.Label, StringComparer.OrdinalIgnoreCase).ToList();
        }

        string searchPrefix = string.IsNullOrWhiteSpace(nativeContainer) ? string.Empty : nativeContainer + ".";
        foreach (string ns in _namespaces)
        {
            if (!ns.StartsWith(searchPrefix, StringComparison.Ordinal))
                continue;

            string remainder = ns[searchPrefix.Length..];
            if (string.IsNullOrWhiteSpace(remainder))
                continue;

            string child = GetDirectChild(remainder);
            if (MatchesNativeCompletionPrefix(child, typedPrefix))
                items.TryAdd(child, new CfgsCompletionItem(child, 9, $"native namespace: {searchPrefix}{child}"));
        }

        foreach ((string path, Type nativeType) in _typesByPath)
        {
            if (!path.StartsWith(searchPrefix, StringComparison.Ordinal))
                continue;

            string remainder = path[searchPrefix.Length..];
            if (string.IsNullOrWhiteSpace(remainder) || remainder.Contains('.', StringComparison.Ordinal))
                continue;

            if (MatchesNativeCompletionPrefix(remainder, typedPrefix))
                items.TryAdd(remainder, new CfgsCompletionItem(remainder, ToNativeCompletionKind(nativeType), $"native type: {nativeType.FullName ?? nativeType.Name}"));
        }

        return items.Values.OrderBy(static item => item.Label, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<string> GetReturnTypePaths(string memberPath)
    {
        bool preferInstanceMembers = memberPath.Contains(NativePathPrefix, StringComparison.Ordinal);
        string normalized = StripNativePathPrefix(memberPath);
        if (normalized.EndsWith(".new", StringComparison.Ordinal))
        {
            string typePath = normalized[..^4];
            return _typesByPath.ContainsKey(typePath) ? [typePath] : [];
        }

        foreach ((string typePath, Type type) in _typesByPath.OrderByDescending(static entry => entry.Key.Length))
        {
            string prefix = typePath + ".";
            if (!normalized.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            string memberName = normalized[prefix.Length..];
            if (memberName.Contains('.', StringComparison.Ordinal))
                continue;

            List<string> returnTypes = [];
            BindingFlags flags = BindingFlags.Public | (preferInstanceMembers ? BindingFlags.Instance : BindingFlags.Static);
            foreach (MemberInfo member in type.GetMember(memberName, flags))
            {
                Type? returnType = member switch
                {
                    MethodInfo method when !method.IsSpecialName => method.ReturnType,
                    PropertyInfo property => property.PropertyType,
                    FieldInfo field => field.FieldType,
                    _ => null
                };

                string? returnPath = TryGetKnownTypePath(returnType);
                if (!string.IsNullOrWhiteSpace(returnPath))
                    returnTypes.Add(returnPath!);
            }

            return returnTypes.Distinct(StringComparer.Ordinal).ToList();
        }

        return [];
    }

    public static string CreateNativePath(string alias, string typePath)
        => $"{alias}.{NativePathPrefix}{typePath}";

    private static void AddTypeMemberCompletions(Type type, string typedPrefix, Dictionary<string, CfgsCompletionItem> items, bool preferInstanceMembers)
    {
        if (!preferInstanceMembers &&
            type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length > 0 &&
            MatchesNativeCompletionPrefix("new", typedPrefix))
        {
            items.TryAdd("new", new CfgsCompletionItem("new", 2, $"constructor: {type.Name}.new(...)"));
        }

        foreach (Type nested in type.GetNestedTypes(BindingFlags.Public))
        {
            if (MatchesNativeCompletionPrefix(nested.Name, typedPrefix))
                items.TryAdd(nested.Name, new CfgsCompletionItem(nested.Name, ToNativeCompletionKind(nested), $"nested native type: {nested.FullName ?? nested.Name}"));
        }

        BindingFlags memberFlags = BindingFlags.Public | (preferInstanceMembers ? BindingFlags.Instance : BindingFlags.Static);
        foreach (MemberInfo member in type.GetMembers(memberFlags))
        {
            if (member is MethodInfo method && method.IsSpecialName)
                continue;

            string name = member.Name;
            if (!MatchesNativeCompletionPrefix(name, typedPrefix))
                continue;

            int kind = member switch
            {
                MethodInfo => 2,
                PropertyInfo => 10,
                FieldInfo => 5,
                EventInfo => 23,
                _ => 6
            };
            string mode = preferInstanceMembers ? "instance" : "static";
            items.TryAdd(name, new CfgsCompletionItem(name, kind, $"native {mode} {member.MemberType.ToString().ToLowerInvariant()}: {type.Name}.{name}"));
        }

        if (!preferInstanceMembers && type.IsEnum)
        {
            foreach (string name in Enum.GetNames(type))
            {
                if (MatchesNativeCompletionPrefix(name, typedPrefix))
                    items.TryAdd(name, new CfgsCompletionItem(name, 20, $"enum member: {type.Name}.{name}"));
            }
        }
    }

    private static string GetDirectChild(string remainder)
    {
        int dot = remainder.IndexOf('.');
        return dot < 0 ? remainder : remainder[..dot];
    }

    private static string GetCfgsTypePath(Type type)
    {
        string fullName = type.FullName ?? type.Name;
        return fullName.Replace('+', '.');
    }

    private string? TryGetKnownTypePath(Type? type)
    {
        if (type is null || type == typeof(void))
            return null;

        Type normalized = Nullable.GetUnderlyingType(type) ?? type;
        if (normalized.IsArray)
            normalized = normalized.GetElementType() ?? normalized;

        string path = GetCfgsTypePath(normalized);
        return _typesByPath.ContainsKey(path) ? path : null;
    }

    private static string StripNativePathPrefix(string path)
    {
        int marker = path.IndexOf(NativePathPrefix, StringComparison.Ordinal);
        return marker < 0 ? path : path[(marker + NativePathPrefix.Length)..];
    }

    private static int ToNativeCompletionKind(Type type)
        => type.IsEnum ? 13 : type.IsInterface ? 8 : 7;

    private static bool MatchesNativeCompletionPrefix(string value, string typedPrefix)
        => string.IsNullOrWhiteSpace(typedPrefix) || value.StartsWith(typedPrefix, StringComparison.OrdinalIgnoreCase);

    private sealed class CompletionAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public CompletionAssemblyLoadContext(string mainAssemblyPath)
            : base(isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            string? path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }
    }
}

internal readonly record struct MemberCompletionRequest(
    int TargetLine,
    int TargetCharacter,
    string TypedPrefix);

internal readonly record struct CfgsSemanticTokenSpan(
    int Line,
    int Character,
    int Length,
    int TokenType,
    int TokenModifiers);

internal sealed record CfgsTextEdit(
    string Uri,
    RangeInfo Range,
    string NewText);

internal sealed record CfgsRenameResult(
    IReadOnlyList<CfgsTextEdit> Edits);

internal sealed record CfgsPrepareRenameResult(
    RangeInfo Range,
    string Placeholder);

internal readonly record struct CfgsSymbolIdentity(
    string QualifiedName,
    int Kind,
    string Uri,
    RangeInfo SelectionRange)
{
    public static CfgsSymbolIdentity From(CfgsSymbol symbol)
        => new(symbol.QualifiedName, symbol.Kind, symbol.Uri, symbol.SelectionRange);

    public bool Matches(CfgsSymbol symbol)
        => Kind == symbol.Kind &&
           string.Equals(QualifiedName, symbol.QualifiedName, StringComparison.Ordinal) &&
           string.Equals(Uri, symbol.Uri, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) &&
           SelectionRange.Equals(symbol.SelectionRange);
}

internal sealed record CfgsSymbol(
    string Name,
    string QualifiedName,
    int Kind,
    string Uri,
    string OriginFile,
    RangeInfo Range,
    RangeInfo SelectionRange,
    string Detail,
    string HoverText,
    IReadOnlyList<CfgsSymbol> Children,
    bool IsSynthetic,
    string? Id = null,
    CfgsSignature? Signature = null,
    string? ValueTypeQualifiedName = null,
    string? CallTargetSymbolId = null);

internal readonly record struct PositionInfo(int Line, int Character);

internal readonly record struct RangeInfo(PositionInfo Start, PositionInfo End);

internal static class TextLocator
{
    public static RangeInfo CreateRange(string? sourceText, int line, int column, string? token, int? spanLength = null)
    {
        int startLine = Math.Max(line - 1, 0);
        int startCharacter = Math.Max(column - 1, 0);
        int endCharacter = startCharacter + Math.Max(spanLength ?? token?.Length ?? 1, 1);

        if (sourceText is null)
            return new RangeInfo(new PositionInfo(startLine, startCharacter), new PositionInfo(startLine, endCharacter));

        string[] lines = SplitLines(sourceText);
        if (startLine >= lines.Length)
            return new RangeInfo(new PositionInfo(startLine, startCharacter), new PositionInfo(startLine, endCharacter));

        string lineText = lines[startLine];
        if (!string.IsNullOrWhiteSpace(token))
        {
            int found = FindIdentifier(lineText, token, startCharacter);
            if (found >= 0)
            {
                startCharacter = found;
                endCharacter = found + token.Length;
            }
            else
            {
                (int lineIndex, int charIndex)? nearby = FindIdentifierNearby(lines, startLine, token);
                if (nearby is not null)
                {
                    startLine = nearby.Value.lineIndex;
                    lineText = lines[startLine];
                    startCharacter = nearby.Value.charIndex;
                    endCharacter = startCharacter + token.Length;
                }
            }
        }

        endCharacter = Math.Min(Math.Max(endCharacter, startCharacter + 1), Math.Max(lineText.Length, startCharacter + 1));
        return new RangeInfo(new PositionInfo(startLine, startCharacter), new PositionInfo(startLine, endCharacter));
    }

    public static string? GetIdentifierAt(string sourceText, int line, int character)
    {
        if (!TryGetLine(sourceText, line, out string lineText))
            return null;

        int index = ClampIndex(lineText, character);
        if (index == lineText.Length && index > 0)
            index--;

        if (!IsIdentifierChar(At(lineText, index)) && index > 0 && IsIdentifierChar(lineText[index - 1]))
            index--;

        if (!IsIdentifierChar(At(lineText, index)))
            return null;

        int start = index;
        while (start > 0 && IsIdentifierChar(lineText[start - 1]))
            start--;

        int end = index;
        while (end < lineText.Length && IsIdentifierChar(lineText[end]))
            end++;

        return lineText[start..end];
    }

    public static string? GetQualifiedIdentifierAt(string sourceText, int line, int character)
    {
        if (!TryGetLine(sourceText, line, out string lineText))
            return null;

        int index = ClampIndex(lineText, character);
        if (index == lineText.Length && index > 0)
            index--;

        char current = At(lineText, index);
        if (!IsIdentifierChar(current) && current != '.' && index > 0)
            index--;

        if (!IsIdentifierChar(At(lineText, index)) && At(lineText, index) != '.')
            return null;

        int start = index;
        while (start > 0 && IsQualifiedIdentifierChar(lineText[start - 1]))
            start--;

        int end = index;
        while (end < lineText.Length && IsQualifiedIdentifierChar(lineText[end]))
            end++;

        string candidate = lineText[start..end].Trim('.');
        if (string.IsNullOrWhiteSpace(candidate))
            return null;

        string[] parts = candidate.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.All(static part => part.All(IsIdentifierChar)) ? string.Join(".", parts) : null;
    }

    public static string? TryExtractQualifiedPath(Expr? expr)
    {
        return expr switch
        {
            VarExpr varExpr => varExpr.Name,
            IndexExpr indexExpr when indexExpr.Target is not null && indexExpr.Index is StringExpr stringExpr
                => AppendQualifiedPath(TryExtractQualifiedPath(indexExpr.Target), stringExpr.Value),
            _ => null
        };
    }

    private static string? AppendQualifiedPath(string? left, string right)
        => string.IsNullOrWhiteSpace(left) ? null : $"{left}.{right}";

    private static bool TryGetLine(string sourceText, int line, out string lineText)
    {
        string[] lines = SplitLines(sourceText);
        if (line < 0 || line >= lines.Length)
        {
            lineText = string.Empty;
            return false;
        }

        lineText = lines[line];
        return true;
    }

    private static string[] SplitLines(string sourceText)
        => sourceText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    private static int FindIdentifier(string lineText, string token, int minIndex)
    {
        if (string.IsNullOrEmpty(token))
            return -1;

        int searchIndex = Math.Min(Math.Max(minIndex, 0), lineText.Length);
        int found = FindValidIdentifierMatch(lineText, token, searchIndex, lineText.Length);
        if (found >= 0)
            return found;

        return searchIndex > 0
            ? FindValidIdentifierMatch(lineText, token, 0, searchIndex)
            : -1;
    }

    private static bool IsValidIdentifierMatch(string lineText, int start, int length)
    {
        if (start < 0)
            return false;

        bool leftOk = start == 0 || !IsIdentifierChar(lineText[start - 1]);
        int end = start + length;
        bool rightOk = end >= lineText.Length || !IsIdentifierChar(lineText[end]);
        return leftOk && rightOk;
    }

    private static (int lineIndex, int charIndex)? FindIdentifierNearby(string[] lines, int expectedLine, string token)
    {
        int bestDistance = int.MaxValue;
        int bestDirectionScore = int.MaxValue;
        (int lineIndex, int charIndex)? best = null;

        for (int i = 0; i < lines.Length; i++)
        {
            int found = FindIdentifier(lines[i], token, 0);
            if (found < 0)
                continue;

            int distance = Math.Abs(i - expectedLine);
            int directionScore = i < expectedLine ? 1 : 0;
            if (distance < bestDistance || (distance == bestDistance && directionScore < bestDirectionScore))
            {
                bestDistance = distance;
                bestDirectionScore = directionScore;
                best = (i, found);
            }
        }

        return best;
    }

    private static int FindValidIdentifierMatch(string lineText, string token, int startIndex, int endExclusive)
    {
        int limit = Math.Min(Math.Max(endExclusive, 0), lineText.Length);
        int searchIndex = Math.Min(Math.Max(startIndex, 0), limit);

        while (searchIndex <= limit)
        {
            int found = lineText.IndexOf(token, searchIndex, StringComparison.Ordinal);
            if (found < 0 || found >= limit)
                return -1;

            if (IsValidIdentifierMatch(lineText, found, token.Length))
                return found;

            searchIndex = found + 1;
        }

        return -1;
    }

    private static int ClampIndex(string text, int index)
        => Math.Min(Math.Max(index, 0), text.Length);

    private static char At(string text, int index)
        => index < 0 || index >= text.Length ? '\0' : text[index];

    private static bool IsQualifiedIdentifierChar(char ch)
        => IsIdentifierChar(ch) || ch == '.';

    private static bool IsIdentifierChar(char ch)
        => char.IsLetterOrDigit(ch) || ch == '_';
}

internal sealed record CfgsCallHierarchyItem(
    string Name,
    int Kind,
    string Uri,
    RangeInfo Range,
    RangeInfo SelectionRange,
    string Detail);

internal sealed record CfgsCallHierarchyIncomingCall(
    CfgsCallHierarchyItem From,
    IReadOnlyList<RangeInfo> FromRanges);

internal sealed record CfgsCallHierarchyOutgoingCall(
    CfgsCallHierarchyItem To,
    IReadOnlyList<RangeInfo> FromRanges);

internal sealed record CfgsInlayHint(
    PositionInfo Position,
    string Label,
    CfgsInlayHintKind Kind);

internal enum CfgsInlayHintKind
{
    Type = 1,
    Parameter = 2
}

internal sealed record CfgsCodeLens(
    RangeInfo Range,
    string CommandTitle);

internal sealed record CfgsDocumentLink(
    RangeInfo Range,
    string Target);

internal sealed record CfgsSelectionRange(
    RangeInfo Range,
    CfgsSelectionRange? Parent);
