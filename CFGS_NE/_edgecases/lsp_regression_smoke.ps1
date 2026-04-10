param()

$ErrorActionPreference = "Stop"
$script:NextRequestId = 0
$script:AnyFailed = $false

$edgeDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $edgeDir
$repoRoot = Split-Path -Parent $projectRoot
$serverDll = Join-Path $repoRoot "dist\Debug\net10.0\CFGS.Lsp.dll"
$artifactsRoot = Join-Path $repoRoot ".artifacts"
$workspaceRoot = Join-Path $artifactsRoot ("lsp_smoke_" + [Guid]::NewGuid().ToString("N"))

function Write-Result {
    param(
        [string]$Name,
        [bool]$Ok,
        [string]$Details = ""
    )

    if ($Ok) {
        Write-Host "[PASS] $Name" -ForegroundColor Green
        return
    }

    $script:AnyFailed = $true
    if ([string]::IsNullOrWhiteSpace($Details)) {
        Write-Host "[FAIL] $Name" -ForegroundColor Red
    } else {
        Write-Host "[FAIL] $Name - $Details" -ForegroundColor Red
    }
}

function Assert-True {
    param(
        [string]$Name,
        [bool]$Condition,
        [string]$Details
    )

    Write-Result -Name $Name -Ok $Condition -Details $Details
}

function Format-JsonCompact {
    param([object]$Value)

    if ($null -eq $Value) {
        return "<null>"
    }

    return ($Value | ConvertTo-Json -Depth 64 -Compress)
}

function Has-LspResultItems {
    param([object]$Value)

    if ($null -eq $Value) {
        return $false
    }

    if ($Value -is [System.Array]) {
        return $Value.Length -gt 0
    }

    if ($Value -is [System.Collections.ICollection]) {
        return $Value.Count -gt 0
    }

    return $true
}

function Get-UriResultCount {
    param(
        [object]$Value,
        [string]$Uri
    )

    if ($null -eq $Value) {
        return 0
    }

    $items = @($Value)
    $count = 0
    foreach ($item in $items) {
        if ($null -ne $item -and $item.uri -eq $Uri) {
            $count++
        }
    }

    return $count
}

function Get-WorkspaceEditChangeCount {
    param(
        [object]$WorkspaceEdit,
        [string]$Uri
    )

    if ($null -eq $WorkspaceEdit -or $null -eq $WorkspaceEdit.changes) {
        return 0
    }

    foreach ($property in $WorkspaceEdit.changes.PSObject.Properties) {
        if ($property.Name -ne $Uri) {
            continue
        }

        $value = $property.Value
        if ($value -is [System.Array]) {
            return $value.Length
        }

        if ($value -is [System.Collections.ICollection]) {
            return $value.Count
        }

        return 1
    }

    return 0
}

function Find-CallHierarchyItem {
    param(
        [object]$Calls,
        [string]$FromName
    )

    foreach ($call in @($Calls)) {
        if ($null -ne $call -and $null -ne $call.from -and $call.from.name -eq $FromName) {
            return $call
        }
    }

    return $null
}

function Get-SignatureLabels {
    param([object]$SignatureHelp)

    if ($null -eq $SignatureHelp -or $null -eq $SignatureHelp.signatures) {
        return @()
    }

    $labels = @()
    foreach ($signature in @($SignatureHelp.signatures)) {
        if ($null -ne $signature) {
            $labels += [string]$signature.label
        }
    }

    return $labels
}

function Get-CompletionLabels {
    param([object]$CompletionResult)

    if ($null -eq $CompletionResult) {
        return @()
    }

    $labels = @()
    foreach ($item in @($CompletionResult)) {
        if ($null -ne $item -and $null -ne $item.label) {
            $labels += [string]$item.label
        }
    }

    return $labels
}

function New-FileUri {
    param([string]$Path)
    return [Uri]::new([IO.Path]::GetFullPath($Path)).AbsoluteUri
}

function Find-Position {
    param(
        [string]$Text,
        [string]$Needle,
        [int]$Occurrence = 1,
        [int]$CharacterOffset = 0
    )

    $startIndex = 0
    $index = -1
    for ($i = 0; $i -lt $Occurrence; $i++) {
        $index = $Text.IndexOf($Needle, $startIndex, [StringComparison]::Ordinal)
        if ($index -lt 0) {
            throw "Needle '$Needle' not found."
        }

        $startIndex = $index + $Needle.Length
    }

    $absoluteIndex = $index + $CharacterOffset
    $prefix = $Text.Substring(0, $absoluteIndex).Replace("`r`n", "`n")
    $lines = $prefix -split "`n", -1
    return @{
        line = $lines.Count - 1
        character = $lines[-1].Length
    }
}

function Start-LspServer {
    if (-not (Test-Path -LiteralPath $serverDll)) {
        throw "Server DLL not found at '$serverDll'. Build CFGS.Lsp first."
    }

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = "dotnet"
    $startInfo.Arguments = "`"$serverDll`""
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WorkingDirectory = $repoRoot

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    $null = $process.Start()
    return $process
}

function Send-LspPayload {
    param(
        [System.Diagnostics.Process]$Process,
        [object]$Payload
    )

    $json = $Payload | ConvertTo-Json -Depth 64 -Compress
    $jsonBytes = [Text.Encoding]::UTF8.GetBytes($json)
    $headerBytes = [Text.Encoding]::ASCII.GetBytes("Content-Length: $($jsonBytes.Length)`r`n`r`n")

    $stdin = $Process.StandardInput.BaseStream
    $stdin.Write($headerBytes, 0, $headerBytes.Length)
    $stdin.Write($jsonBytes, 0, $jsonBytes.Length)
    $stdin.Flush()
}

function Read-LspMessage {
    param([System.Diagnostics.Process]$Process)

    $stdout = $Process.StandardOutput.BaseStream
    $headerBytes = New-Object 'System.Collections.Generic.List[byte]'

    while ($true) {
        $current = $stdout.ReadByte()
        if ($current -lt 0) {
            throw "Unexpected end of LSP output."
        }

        $headerBytes.Add([byte]$current)
        $count = $headerBytes.Count
        if ($count -ge 4 -and
            $headerBytes[$count - 4] -eq 13 -and
            $headerBytes[$count - 3] -eq 10 -and
            $headerBytes[$count - 2] -eq 13 -and
            $headerBytes[$count - 1] -eq 10) {
            break
        }
    }

    $header = [Text.Encoding]::ASCII.GetString($headerBytes.ToArray())
    if ($header -notmatch "Content-Length:\s*(\d+)") {
        throw "Missing Content-Length header."
    }

    $contentLength = [int]$matches[1]
    $bodyBytes = New-Object byte[] $contentLength
    $offset = 0
    while ($offset -lt $contentLength) {
        $read = $stdout.Read($bodyBytes, $offset, $contentLength - $offset)
        if ($read -le 0) {
            throw "Unexpected end of LSP body."
        }

        $offset += $read
    }

    return [Text.Encoding]::UTF8.GetString($bodyBytes)
}

function Wait-LspResponse {
    param(
        [System.Diagnostics.Process]$Process,
        [int]$Id
    )

    while ($true) {
        $message = (Read-LspMessage -Process $Process) | ConvertFrom-Json
        if ($null -ne $message.id -and [int]$message.id -eq $Id) {
            if ($null -ne $message.error) {
                throw "LSP error: $($message.error.message)"
            }

            return $message.result
        }
    }
}

function Send-LspRequest {
    param(
        [System.Diagnostics.Process]$Process,
        [string]$Method,
        [object]$Params
    )

    $script:NextRequestId++
    $id = $script:NextRequestId
    Send-LspPayload -Process $Process -Payload ([ordered]@{
        jsonrpc = "2.0"
        id = $id
        method = $Method
        params = $Params
    })

    return Wait-LspResponse -Process $Process -Id $id
}

function Send-LspNotification {
    param(
        [System.Diagnostics.Process]$Process,
        [string]$Method,
        [object]$Params
    )

    Send-LspPayload -Process $Process -Payload ([ordered]@{
        jsonrpc = "2.0"
        method = $Method
        params = $Params
    })
}

function Open-Document {
    param(
        [System.Diagnostics.Process]$Process,
        [string]$Path,
        [string]$Text
    )

    Send-LspNotification -Process $Process -Method "textDocument/didOpen" -Params ([ordered]@{
        textDocument = [ordered]@{
            uri = (New-FileUri $Path)
            languageId = "cfgs"
            version = 1
            text = $Text
        }
    })

    # Drain publishDiagnostics notifications after didOpen so the server stdout pipe
    # cannot fill up before the first explicit request in long smoke runs.
    $null = Send-LspRequest -Process $Process -Method "textDocument/documentSymbol" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $Path) }
    })
}

New-Item -ItemType Directory -Force -Path $workspaceRoot | Out-Null

$usingText = @'
class Handle() {
    var value = 1;
    func destroy() {}
}

func work() {
    using (var h = new Handle()) {
        h.value;
    }
}
'@

$outerText = @'
class Outer(seed) {
    var seed = 0;

    class Inner() {
        func total() {
            return outer.seed;
        }
    }
}
'@

$ifaceText = @'
export interface Worker {
    func ping();
}
'@

$implText = @'
import { Worker } from "./iface.cfs";

class WorkerImpl() : Worker {
    func ping() {
        return null;
    }
}
'@

$importsText = 'import "./iface.cfs";'

$signatureText = @'
func sum([a, b], c = 3, *rest) {
    return a + b + c;
}

sum([1, 2], 4);
'@

$implicitMemberText = @'
class Counter() {
    var value = 1;

    func total() {
        return value;
    }
}
'@

$keywordHoverText = @'
var bla = 123;

func blabla() {
    return bla;
}
'@

$moduleText = @'
export func add_base(v) {
    return v + 1;
}

export func scale_twice(x) {
    return x * 2;
}

export enum Flag {
    On
}
'@

$namespaceConsumerText = @'
import * as Tools from "./module_case.cfs";

func run() {
    var flag = Tools["Flag"]["On"];
    return Tools["add_base"](5);
}
'@

$completionChainText = @'
import * as Tools from "./module_case.cfs";

class Worker() {
    func run(v) {
        return v;
    }

    static func build() {
        return new Worker();
    }
}

func make_worker() {
    return new Worker();
}

func make_box() {
    return {
        "worker": new Worker(),
        "nested": { "worker": new Worker() }
    };
}

func exercise_completion() {
    var direct = new Worker();
    Tools.add_base(1);
    Worker.build();
    direct.run(2);
    make_worker().run(3);
    make_box()["worker"].run(4);
    make_box()["nested"]["worker"].run(5);
    Worker.build().run(6);
}
'@

$urlImportText = 'import "https://example.com/module.cfs";'

$callableAliasText = @'
import { add_base as plus } from "./module_case.cfs";

func run_alias() {
    var local = plus;
    return local(5) + plus(1);
}
'@

$callableRebindText = @'
import { add_base as plus, scale_twice as twice } from "./module_case.cfs";

func run_rebind() {
    var local = plus;
    local = twice;

    var forwarded = null;
    forwarded = local;

    return forwarded(5) + local(1);
}
'@

$containerAliasText = @'
import { add_base as plus, scale_twice as twice } from "./module_case.cfs";

class CallableBox() {
    var fn = null;
}

func run_member_boxes() {
    var first = new CallableBox();
    var second = new CallableBox();
    first.fn = plus;
    second.fn = twice;
    return first.fn(5) + second.fn(1);
}

func run_dynamic_box() {
    var bag = {};
    bag["fn"] = plus;
    var forwarded = bag;
    var local = forwarded["fn"];
    return forwarded["fn"](7) + local(2);
}

func run_nested_container() {
    var root = {};
    root["inner"] = {};
    var forwarded = root["inner"];
    forwarded["fn"] = plus;
    return root["inner"]["fn"](4);
}
'@

$branchFlowText = @'
import { add_base as plus, scale_twice as twice } from "./module_case.cfs";

func choose_callable(flag) {
    if (flag) {
        return plus;
    }

    return twice;
}

func run_choose_callable(flag) {
    var picked = choose_callable(flag);
    return picked(3);
}

func run_branch_merge(flag) {
    var picked = plus;
    if (flag) {
        picked = twice;
    }

    return picked(4);
}

func build_plus_box() {
    var bag = {};
    bag["fn"] = plus;
    return bag;
}

func build_twice_box() {
    var bag = {};
    bag["fn"] = twice;
    return bag;
}

func choose_box(flag) {
    if (flag) {
        return build_plus_box();
    }

    return build_twice_box();
}

func run_choose_box(flag) {
    var picked = choose_box(flag);
    return picked["fn"](2);
}
'@

$loopTryFlowText = @'
import { add_base as plus, scale_twice as twice } from "./module_case.cfs";

func run_while_merge(flag) {
    var picked = plus;
    while (flag) {
        picked = twice;
        break;
    }

    return picked(7);
}

func run_do_while_override() {
    var picked = plus;
    do {
        picked = twice;
    } while (false);

    return picked(8);
}

func run_for_merge(flag) {
    var picked = plus;
    for (; flag; flag = false;) {
        picked = twice;
    }

    return picked(9);
}

func run_foreach_merge(items) {
    var picked = plus;
    foreach (var value in items) {
        picked = twice;
        break;
    }

    return picked(10);
}

func run_try_catch_merge(flag) {
    var picked = plus;
    try {
        if (flag) {
            throw "boom";
        }

        picked = plus;
    } catch (e) {
        picked = twice;
    }

    return picked(11);
}

func run_try_finally_forward(flag) {
    var picked = plus;
    var forwarded = plus;
    try {
        if (flag) {
            picked = twice;
        }
    } finally {
        forwarded = picked;
    }

    return forwarded(12);
}

func run_try_finally_override(flag) {
    var picked = plus;
    try {
        if (flag) {
            picked = plus;
        }
    } finally {
        picked = twice;
    }

    return picked(13);
}
'@

$collectionFlowText = @'
import { add_base as plus, scale_twice as twice } from "./module_case.cfs";

func build_handler_array() {
    return [plus, twice];
}

func run_array_literal_calls() {
    var handlers = [plus, twice];
    return handlers[0](14) + handlers[1](15);
}

func run_array_assignment_calls() {
    var handlers = [];
    handlers[0] = plus;
    handlers[1] = twice;
    return handlers[0](16) + handlers[1](17);
}

func run_returned_array_calls() {
    var handlers = build_handler_array();
    return handlers[0](18) + handlers[1](19);
}

func choose_literal_box(flag) {
    if (flag) {
        return {"fn": plus};
    }

    return {"fn": twice};
}

func run_returned_dict_literal(flag) {
    var bag = choose_literal_box(flag);
    return bag["fn"](20);
}
'@

$loopFixpointFlowText = @'
import { add_base as plus, scale_twice as twice } from "./module_case.cfs";

func run_while_fixpoint(flag) {
    var picked = plus;
    var current = plus;
    while (flag) {
        picked = current;
        current = twice;
    }

    return picked(25);
}

func run_do_while_fixpoint(flag) {
    var picked = plus;
    var current = plus;
    do {
        picked = current;
        current = twice;
    } while (flag);

    return picked(26);
}

func run_for_fixpoint(flag) {
    var picked = plus;
    var current = plus;
    for (; flag;) {
        picked = current;
        current = twice;
    }

    return picked(27);
}

func run_foreach_fixpoint(items) {
    var picked = plus;
    var current = plus;
    foreach (var value in items) {
        picked = current;
        current = twice;
    }

    return picked(28);
}
'@

$loopControlFlowText = @'
import { add_base as plus, scale_twice as twice } from "./module_case.cfs";

func run_break_stops_tail(flag) {
    var picked = plus;
    while (flag) {
        picked = twice;
        break;
        picked = plus;
    }

    return picked(29);
}

func run_nested_break(flag) {
    var picked = plus;
    while (flag) {
        if (flag) {
            picked = twice;
            break;
        }

        picked = plus;
    }

    return picked(30);
}

func run_do_while_continue_exact(flag) {
    var picked = plus;
    do {
        picked = twice;
        continue;
        picked = plus;
    } while (flag);

    return picked(31);
}

func choose_do_while_return() {
    do {
        return twice;
        return plus;
    } while (false);

    return plus;
}

func run_do_while_return() {
    var picked = choose_do_while_return();
    return picked(32);
}

func run_try_break_finally(flag) {
    var picked = plus;
    var forwarded = plus;
    while (flag) {
        try {
            picked = twice;
            break;
        } finally {
            forwarded = picked;
        }
    }

    return forwarded(33);
}

func run_try_continue_finally(flag) {
    var picked = plus;
    var forwarded = plus;
    do {
        try {
            picked = twice;
            continue;
        } finally {
            forwarded = picked;
        }

        picked = plus;
    } while (flag);

    return forwarded(34);
}

func choose_try_finally_return() {
    try {
        return plus;
    } finally {
        return twice;
    }
}

func run_try_finally_return() {
    var picked = choose_try_finally_return();
    return picked(35);
}
'@

$usingPath = Join-Path $workspaceRoot "using_case.cfs"
$outerPath = Join-Path $workspaceRoot "outer_case.cfs"
$ifacePath = Join-Path $workspaceRoot "iface.cfs"
$implPath = Join-Path $workspaceRoot "impl.cfs"
$importsPath = Join-Path $workspaceRoot "imports_case.cfs"
$signaturePath = Join-Path $workspaceRoot "signature_case.cfs"
$implicitMemberPath = Join-Path $workspaceRoot "implicit_member_case.cfs"
$keywordHoverPath = Join-Path $workspaceRoot "keyword_hover_case.cfs"
$modulePath = Join-Path $workspaceRoot "module_case.cfs"
$namespaceConsumerPath = Join-Path $workspaceRoot "namespace_consumer_case.cfs"
$completionChainPath = Join-Path $workspaceRoot "completion_chain_case.cfs"
$urlImportPath = Join-Path $workspaceRoot "url_import_case.cfs"
$callableAliasPath = Join-Path $workspaceRoot "callable_alias_case.cfs"
$callableRebindPath = Join-Path $workspaceRoot "callable_rebind_case.cfs"
$containerAliasPath = Join-Path $workspaceRoot "container_alias_case.cfs"
$branchFlowPath = Join-Path $workspaceRoot "branch_flow_case.cfs"
$loopTryFlowPath = Join-Path $workspaceRoot "loop_try_flow_case.cfs"
$collectionFlowPath = Join-Path $workspaceRoot "collection_flow_case.cfs"
$loopFixpointFlowPath = Join-Path $workspaceRoot "loop_fixpoint_flow_case.cfs"
$loopControlFlowPath = Join-Path $workspaceRoot "loop_control_flow_case.cfs"

[IO.File]::WriteAllText($usingPath, $usingText)
[IO.File]::WriteAllText($outerPath, $outerText)
[IO.File]::WriteAllText($ifacePath, $ifaceText)
[IO.File]::WriteAllText($implPath, $implText)
[IO.File]::WriteAllText($importsPath, $importsText)
[IO.File]::WriteAllText($signaturePath, $signatureText)
[IO.File]::WriteAllText($implicitMemberPath, $implicitMemberText)
[IO.File]::WriteAllText($keywordHoverPath, $keywordHoverText)
[IO.File]::WriteAllText($modulePath, $moduleText)
[IO.File]::WriteAllText($namespaceConsumerPath, $namespaceConsumerText)
[IO.File]::WriteAllText($completionChainPath, $completionChainText)
[IO.File]::WriteAllText($urlImportPath, $urlImportText)
[IO.File]::WriteAllText($callableAliasPath, $callableAliasText)
[IO.File]::WriteAllText($callableRebindPath, $callableRebindText)
[IO.File]::WriteAllText($containerAliasPath, $containerAliasText)
[IO.File]::WriteAllText($branchFlowPath, $branchFlowText)
[IO.File]::WriteAllText($loopTryFlowPath, $loopTryFlowText)
[IO.File]::WriteAllText($collectionFlowPath, $collectionFlowText)
[IO.File]::WriteAllText($loopFixpointFlowPath, $loopFixpointFlowText)
[IO.File]::WriteAllText($loopControlFlowPath, $loopControlFlowText)

$process = Start-LspServer
try {
    $null = Send-LspRequest -Process $process -Method "initialize" -Params ([ordered]@{
        processId = $PID
        rootUri = (New-FileUri $workspaceRoot)
        capabilities = @{}
    })

    Send-LspNotification -Process $process -Method "initialized" -Params @{}

    Open-Document -Process $process -Path $usingPath -Text $usingText
    Open-Document -Process $process -Path $outerPath -Text $outerText
    Open-Document -Process $process -Path $ifacePath -Text $ifaceText
    Open-Document -Process $process -Path $implPath -Text $implText
    Open-Document -Process $process -Path $importsPath -Text $importsText
    Open-Document -Process $process -Path $signaturePath -Text $signatureText
    Open-Document -Process $process -Path $implicitMemberPath -Text $implicitMemberText
    Open-Document -Process $process -Path $keywordHoverPath -Text $keywordHoverText
    Open-Document -Process $process -Path $modulePath -Text $moduleText
    Open-Document -Process $process -Path $namespaceConsumerPath -Text $namespaceConsumerText
    Open-Document -Process $process -Path $completionChainPath -Text $completionChainText
    Open-Document -Process $process -Path $urlImportPath -Text $urlImportText
    Open-Document -Process $process -Path $callableAliasPath -Text $callableAliasText
    Open-Document -Process $process -Path $callableRebindPath -Text $callableRebindText
    Open-Document -Process $process -Path $containerAliasPath -Text $containerAliasText
    Open-Document -Process $process -Path $branchFlowPath -Text $branchFlowText
    Open-Document -Process $process -Path $loopTryFlowPath -Text $loopTryFlowText
    Open-Document -Process $process -Path $collectionFlowPath -Text $collectionFlowText
    Open-Document -Process $process -Path $loopFixpointFlowPath -Text $loopFixpointFlowText
    Open-Document -Process $process -Path $loopControlFlowPath -Text $loopControlFlowText

    $usingHPos = Find-Position -Text $usingText -Needle "h.value"
    $usingTypeDefs = Send-LspRequest -Process $process -Method "textDocument/typeDefinition" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $usingPath) }
        position = $usingHPos
    })
    Assert-True -Name "lsp_using_type_definition" -Condition (Has-LspResultItems $usingTypeDefs) -Details "Expected type definition for using binding 'h'. Actual: $(Format-JsonCompact $usingTypeDefs)"

    $outerSeedPos = Find-Position -Text $outerText -Needle "outer.seed" -CharacterOffset 6
    $outerDefs = Send-LspRequest -Process $process -Method "textDocument/definition" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $outerPath) }
        position = $outerSeedPos
    })
    Assert-True -Name "lsp_outer_member_definition" -Condition (Has-LspResultItems $outerDefs) -Details "Expected definition for 'outer.seed'. Actual: $(Format-JsonCompact $outerDefs)"

    $ifacePingPos = Find-Position -Text $ifaceText -Needle "ping();"
    $ifaceDefs = Send-LspRequest -Process $process -Method "textDocument/definition" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $ifacePath) }
        position = $ifacePingPos
    })
    $implSymbols = Send-LspRequest -Process $process -Method "textDocument/documentSymbol" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $implPath) }
    })
    $implementations = Send-LspRequest -Process $process -Method "textDocument/implementation" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $ifacePath) }
        position = $ifacePingPos
    })
    $implUri = New-FileUri $implPath
    $hasImplementation = $false
    foreach ($item in $implementations) {
        if ($item.uri -eq $implUri) {
            $hasImplementation = $true
            break
        }
    }
    Assert-True -Name "lsp_workspace_implementation" -Condition $hasImplementation -Details "Expected implementation result in impl.cfs. Definition anchor: $(Format-JsonCompact $ifaceDefs). Impl symbols: $(Format-JsonCompact $implSymbols). Actual implementations: $(Format-JsonCompact $implementations)"

    $documentLinks = Send-LspRequest -Process $process -Method "textDocument/documentLink" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $importsPath) }
    })
    $hasImportLink = $false
    $ifaceUri = New-FileUri $ifacePath
    foreach ($link in $documentLinks) {
        if ($link.target -eq $ifaceUri) {
            $hasImportLink = $true
            break
        }
    }
    Assert-True -Name "lsp_bare_import_document_link" -Condition $hasImportLink -Details "Expected document link for bare import."

    $signaturePos = Find-Position -Text $signatureText -Needle "sum([1, 2], 4);" -CharacterOffset 11
    $signatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $signaturePath) }
        position = $signaturePos
    })
    $signatureLabel = if ($signatureHelp.signatures.Count -gt 0) { [string]$signatureHelp.signatures[0].label } else { "" }
    $signatureOk = $signatureLabel.Contains("[a, b]") -and (-not $signatureLabel.Contains("__arg_ds_"))
    Assert-True -Name "lsp_destructure_signature_display" -Condition $signatureOk -Details "Expected destructuring parameter display in signature help, got '$signatureLabel'."

    $implicitMemberPos = Find-Position -Text $implicitMemberText -Needle "return value;" -CharacterOffset 7
    $implicitMemberDefs = Send-LspRequest -Process $process -Method "textDocument/definition" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $implicitMemberPath) }
        position = $implicitMemberPos
    })
    Assert-True -Name "lsp_implicit_member_definition" -Condition (Has-LspResultItems $implicitMemberDefs) -Details "Expected definition for implicit member 'value'. Actual: $(Format-JsonCompact $implicitMemberDefs)"

    $funcKeywordPos = Find-Position -Text $keywordHoverText -Needle "func blabla()" -CharacterOffset 1
    $funcNamePos = Find-Position -Text $keywordHoverText -Needle "func blabla()" -CharacterOffset ([string]'func bla').Length
    $funcKeywordHover = Send-LspRequest -Process $process -Method "textDocument/hover" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $keywordHoverPath) }
        position = $funcKeywordPos
    })
    $funcNameHover = Send-LspRequest -Process $process -Method "textDocument/hover" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $keywordHoverPath) }
        position = $funcNamePos
    })
    Assert-True -Name "lsp_keyword_hover_ignores_func" -Condition ($null -eq $funcKeywordHover) -Details "Expected no hover on 'func' keyword. Actual: $(Format-JsonCompact $funcKeywordHover)"
    Assert-True -Name "lsp_function_name_hover_resolves_function" -Condition (($null -ne $funcNameHover) -and $funcNameHover.contents.value -like '*func blabla()*') -Details "Expected hover on function name to resolve function symbol. Actual: $(Format-JsonCompact $funcNameHover)"

    $moduleAddPos = Find-Position -Text $moduleText -Needle "add_base(v)"
    $moduleScalePos = Find-Position -Text $moduleText -Needle "scale_twice(x)"
    $namespaceAliasAddPos = Find-Position -Text $namespaceConsumerText -Needle 'Tools["add_base"]' -CharacterOffset 7
    $namespaceAliasFlagPos = Find-Position -Text $namespaceConsumerText -Needle 'Tools["Flag"]["On"]' -CharacterOffset 7
    $namespaceAliasEnumMemberPos = Find-Position -Text $namespaceConsumerText -Needle 'Tools["Flag"]["On"]' -CharacterOffset 15
    $toolsCompletionPos = Find-Position -Text $completionChainText -Needle "Tools.add_base(1)" -CharacterOffset ([string]'Tools.').Length
    $workerStaticCompletionPos = Find-Position -Text $completionChainText -Needle "Worker.build()" -CharacterOffset ([string]'Worker.').Length
    $directCompletionPos = Find-Position -Text $completionChainText -Needle "direct.run(2)" -CharacterOffset ([string]'direct.').Length
    $directPartialCompletionPos = Find-Position -Text $completionChainText -Needle "direct.run(2)" -CharacterOffset ([string]'direct.ru').Length
    $returnedWorkerCompletionPos = Find-Position -Text $completionChainText -Needle 'make_worker().run(3)' -CharacterOffset ([string]'make_worker().').Length
    $indexedWorkerCompletionPos = Find-Position -Text $completionChainText -Needle 'make_box()["worker"].run(4)' -CharacterOffset ([string]'make_box()["worker"].').Length
    $nestedIndexedWorkerCompletionPos = Find-Position -Text $completionChainText -Needle 'make_box()["nested"]["worker"].run(5)' -CharacterOffset ([string]'make_box()["nested"]["worker"].').Length
    $staticBuilderCallCompletionPos = Find-Position -Text $completionChainText -Needle 'Worker.build().run(6)' -CharacterOffset ([string]'Worker.build().').Length

    $namespaceAliasAddDefs = Send-LspRequest -Process $process -Method "textDocument/definition" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $namespaceConsumerPath) }
        position = $namespaceAliasAddPos
    })
    Assert-True -Name "lsp_namespace_alias_function_definition" -Condition (Has-LspResultItems $namespaceAliasAddDefs) -Details "Expected definition for namespace alias function access. Actual: $(Format-JsonCompact $namespaceAliasAddDefs)"

    $namespaceAliasFlagDefs = Send-LspRequest -Process $process -Method "textDocument/definition" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $namespaceConsumerPath) }
        position = $namespaceAliasFlagPos
    })
    Assert-True -Name "lsp_namespace_alias_enum_definition" -Condition (Has-LspResultItems $namespaceAliasFlagDefs) -Details "Expected definition for namespace alias enum access. Actual: $(Format-JsonCompact $namespaceAliasFlagDefs)"

    $namespaceAliasEnumMemberDefs = Send-LspRequest -Process $process -Method "textDocument/definition" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $namespaceConsumerPath) }
        position = $namespaceAliasEnumMemberPos
    })
    Assert-True -Name "lsp_namespace_alias_enum_member_definition" -Condition (Has-LspResultItems $namespaceAliasEnumMemberDefs) -Details "Expected definition for namespace alias enum member access. Actual: $(Format-JsonCompact $namespaceAliasEnumMemberDefs)"

    $moduleReferences = Send-LspRequest -Process $process -Method "textDocument/references" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $modulePath) }
        position = $moduleAddPos
        context = @{ includeDeclaration = $true }
    })
    $namespaceConsumerUri = New-FileUri $namespaceConsumerPath
    $namespaceReferenceCount = Get-UriResultCount -Value $moduleReferences -Uri $namespaceConsumerUri
    Assert-True -Name "lsp_workspace_references_namespace_alias" -Condition ($namespaceReferenceCount -ge 1) -Details "Expected namespace-alias reference in consumer file. Actual: $(Format-JsonCompact $moduleReferences)"

    $renamePayload = Send-LspRequest -Process $process -Method "textDocument/rename" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $modulePath) }
        position = $moduleAddPos
        newName = "plus_one"
    })
    $moduleRenameCount = Get-WorkspaceEditChangeCount -WorkspaceEdit $renamePayload -Uri (New-FileUri $modulePath)
    $consumerRenameCount = Get-WorkspaceEditChangeCount -WorkspaceEdit $renamePayload -Uri $namespaceConsumerUri
    $renameOk = $moduleRenameCount -ge 1 -and $consumerRenameCount -ge 1
    Assert-True -Name "lsp_workspace_rename_namespace_alias" -Condition $renameOk -Details "Expected rename edits in source and namespace-alias consumer reference. Actual: $(Format-JsonCompact $renamePayload)"

    $aliasRenamePayload = Send-LspRequest -Process $process -Method "textDocument/rename" -Params ([ordered]@{
        textDocument = @{ uri = $namespaceConsumerUri }
        position = $namespaceAliasAddPos
        newName = "plus_one"
    })
    $aliasRenameModuleCount = Get-WorkspaceEditChangeCount -WorkspaceEdit $aliasRenamePayload -Uri (New-FileUri $modulePath)
    $aliasRenameConsumerCount = Get-WorkspaceEditChangeCount -WorkspaceEdit $aliasRenamePayload -Uri $namespaceConsumerUri
    $aliasRenameOk = $aliasRenameModuleCount -ge 1 -and $aliasRenameConsumerCount -ge 1
    Assert-True -Name "lsp_alias_position_workspace_rename" -Condition $aliasRenameOk -Details "Expected rename from namespace-alias position to propagate to source and consumer. Actual: $(Format-JsonCompact $aliasRenamePayload)"

    $callHierarchyItems = Send-LspRequest -Process $process -Method "textDocument/prepareCallHierarchy" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $modulePath) }
        position = $moduleAddPos
    })
    $callHierarchyItem = if (Has-LspResultItems $callHierarchyItems) { @($callHierarchyItems)[0] } else { $null }
    $incomingCalls = if ($null -ne $callHierarchyItem) {
        Send-LspRequest -Process $process -Method "callHierarchy/incomingCalls" -Params ([ordered]@{
            item = $callHierarchyItem
        })
    } else {
        $null
    }
    $runIncoming = Find-CallHierarchyItem -Calls $incomingCalls -FromName "run"
    $incomingRangeCount = if ($null -ne $runIncoming) { @($runIncoming.fromRanges).Count } else { 0 }
    Assert-True -Name "lsp_call_hierarchy_namespace_alias" -Condition ($incomingRangeCount -ge 1) -Details "Expected incoming call hierarchy for namespace-alias call site. Item: $(Format-JsonCompact $callHierarchyItems). Incoming: $(Format-JsonCompact $incomingCalls)"

    $runPos = Find-Position -Text $namespaceConsumerText -Needle "run()"
    $runCallHierarchyItems = Send-LspRequest -Process $process -Method "textDocument/prepareCallHierarchy" -Params ([ordered]@{
        textDocument = @{ uri = $namespaceConsumerUri }
        position = $runPos
    })
    $runCallHierarchyItem = if (Has-LspResultItems $runCallHierarchyItems) { @($runCallHierarchyItems)[0] } else { $null }
    $outgoingCalls = if ($null -ne $runCallHierarchyItem) {
        Send-LspRequest -Process $process -Method "callHierarchy/outgoingCalls" -Params ([ordered]@{
            item = $runCallHierarchyItem
        })
    } else {
        $null
    }
    $hasOutgoingAddBase = $false
    foreach ($call in @($outgoingCalls)) {
        if ($null -ne $call -and $null -ne $call.to -and $call.to.name -eq "add_base") {
            $hasOutgoingAddBase = $true
            break
        }
    }
    Assert-True -Name "lsp_outgoing_call_hierarchy_namespace_alias" -Condition $hasOutgoingAddBase -Details "Expected outgoing call hierarchy entry for namespace-alias function call. Actual: $(Format-JsonCompact $outgoingCalls)"

    $inlayHints = Send-LspRequest -Process $process -Method "textDocument/inlayHint" -Params ([ordered]@{
        textDocument = @{ uri = $namespaceConsumerUri }
        range = @{
            start = @{ line = 0; character = 0 }
            end = @{ line = 20; character = 0 }
        }
    })
    $hasParamHint = $false
    foreach ($hint in @($inlayHints)) {
        if ($null -ne $hint -and [string]$hint.label -eq "v:") {
            $hasParamHint = $true
            break
        }
    }
    Assert-True -Name "lsp_inlay_hint_namespace_alias_call" -Condition $hasParamHint -Details "Expected parameter inlay hint for namespace-alias function call. Actual: $(Format-JsonCompact $inlayHints)"

    $toolsCompletion = Send-LspRequest -Process $process -Method "textDocument/completion" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $completionChainPath) }
        position = $toolsCompletionPos
    })
    $toolsCompletionLabels = @(Get-CompletionLabels -CompletionResult $toolsCompletion)
    Assert-True -Name "lsp_semantic_completion_namespace_alias" -Condition (($toolsCompletionLabels -contains "add_base") -and ($toolsCompletionLabels -contains "scale_twice")) -Details "Expected semantic completion for namespace alias to include module exports. Actual: $(Format-JsonCompact $toolsCompletion)"

    $workerStaticCompletion = Send-LspRequest -Process $process -Method "textDocument/completion" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $completionChainPath) }
        position = $workerStaticCompletionPos
    })
    $workerStaticCompletionLabels = @(Get-CompletionLabels -CompletionResult $workerStaticCompletion)
    Assert-True -Name "lsp_semantic_completion_static_context" -Condition (($workerStaticCompletionLabels -contains "build") -and (-not ($workerStaticCompletionLabels -contains "run"))) -Details "Expected static completion on Worker. to include build but exclude instance member run. Actual: $(Format-JsonCompact $workerStaticCompletion)"

    $directCompletion = Send-LspRequest -Process $process -Method "textDocument/completion" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $completionChainPath) }
        position = $directCompletionPos
    })
    $directCompletionLabels = @(Get-CompletionLabels -CompletionResult $directCompletion)
    Assert-True -Name "lsp_semantic_completion_instance_context" -Condition (($directCompletionLabels -contains "run") -and (-not ($directCompletionLabels -contains "build"))) -Details "Expected instance completion on Worker value to include run but exclude build. Actual: $(Format-JsonCompact $directCompletion)"

    $directPartialCompletion = Send-LspRequest -Process $process -Method "textDocument/completion" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $completionChainPath) }
        position = $directPartialCompletionPos
    })
    $directPartialCompletionLabels = @(Get-CompletionLabels -CompletionResult $directPartialCompletion)
    Assert-True -Name "lsp_semantic_completion_partial_prefix" -Condition (($directPartialCompletionLabels -contains "run") -and $directPartialCompletionLabels.Count -eq 1) -Details "Expected semantic completion to honor typed member prefix 'ru'. Actual: $(Format-JsonCompact $directPartialCompletion)"

    $returnedWorkerCompletion = Send-LspRequest -Process $process -Method "textDocument/completion" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $completionChainPath) }
        position = $returnedWorkerCompletionPos
    })
    $returnedWorkerCompletionLabels = @(Get-CompletionLabels -CompletionResult $returnedWorkerCompletion)
    Assert-True -Name "lsp_semantic_completion_call_chain" -Condition (($returnedWorkerCompletionLabels -contains "run") -and (-not ($returnedWorkerCompletionLabels -contains "build"))) -Details "Expected completion after call-returned Worker value to include run only. Actual: $(Format-JsonCompact $returnedWorkerCompletion)"

    $indexedWorkerCompletion = Send-LspRequest -Process $process -Method "textDocument/completion" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $completionChainPath) }
        position = $indexedWorkerCompletionPos
    })
    $indexedWorkerCompletionLabels = @(Get-CompletionLabels -CompletionResult $indexedWorkerCompletion)
    Assert-True -Name "lsp_semantic_completion_index_chain" -Condition ($indexedWorkerCompletionLabels -contains "run") -Details "Expected completion after returned dict index chain to include run. Actual: $(Format-JsonCompact $indexedWorkerCompletion)"

    $nestedIndexedWorkerCompletion = Send-LspRequest -Process $process -Method "textDocument/completion" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $completionChainPath) }
        position = $nestedIndexedWorkerCompletionPos
    })
    $nestedIndexedWorkerCompletionLabels = @(Get-CompletionLabels -CompletionResult $nestedIndexedWorkerCompletion)
    Assert-True -Name "lsp_semantic_completion_nested_index_chain" -Condition ($nestedIndexedWorkerCompletionLabels -contains "run") -Details "Expected completion after nested returned dict index chain to include run. Actual: $(Format-JsonCompact $nestedIndexedWorkerCompletion)"

    $staticBuilderCallCompletion = Send-LspRequest -Process $process -Method "textDocument/completion" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $completionChainPath) }
        position = $staticBuilderCallCompletionPos
    })
    $staticBuilderCallCompletionLabels = @(Get-CompletionLabels -CompletionResult $staticBuilderCallCompletion)
    Assert-True -Name "lsp_semantic_completion_static_builder_call_chain" -Condition (($staticBuilderCallCompletionLabels -contains "run") -and (-not ($staticBuilderCallCompletionLabels -contains "build"))) -Details "Expected completion after static builder call chain to resolve to instance members only. Actual: $(Format-JsonCompact $staticBuilderCallCompletion)"

    $localAliasCallPos = Find-Position -Text $callableAliasText -Needle "local(5)" -CharacterOffset 6
    $plusAliasCallPos = Find-Position -Text $callableAliasText -Needle "plus(1)" -CharacterOffset 5
    $runAliasPos = Find-Position -Text $callableAliasText -Needle "run_alias()"
    $rebindForwardedCallPos = Find-Position -Text $callableRebindText -Needle "forwarded(5)" -CharacterOffset 10
    $rebindLocalCallPos = Find-Position -Text $callableRebindText -Needle "local(1)" -CharacterOffset 6
    $runRebindPos = Find-Position -Text $callableRebindText -Needle "run_rebind()"
    $firstMemberCallPos = Find-Position -Text $containerAliasText -Needle "first.fn(5)" -CharacterOffset 9
    $secondMemberCallPos = Find-Position -Text $containerAliasText -Needle "second.fn(1)" -CharacterOffset 10
    $dynamicForwardedCallPos = Find-Position -Text $containerAliasText -Needle 'forwarded["fn"](7)' -CharacterOffset 16
    $nestedContainerCallPos = Find-Position -Text $containerAliasText -Needle 'root["inner"]["fn"](4)' -CharacterOffset 20
    $runMemberBoxesPos = Find-Position -Text $containerAliasText -Needle "run_member_boxes()"
    $runDynamicBoxPos = Find-Position -Text $containerAliasText -Needle "run_dynamic_box()"
    $runNestedContainerPos = Find-Position -Text $containerAliasText -Needle "run_nested_container()"
    $chooseCallableCallPos = Find-Position -Text $branchFlowText -Needle "picked(3)" -CharacterOffset 7
    $branchMergeCallPos = Find-Position -Text $branchFlowText -Needle "picked(4)" -CharacterOffset 7
    $chooseBoxCallPos = Find-Position -Text $branchFlowText -Needle 'picked["fn"](2)' -CharacterOffset 13
    $runChooseCallablePos = Find-Position -Text $branchFlowText -Needle "run_choose_callable(flag)"
    $runBranchMergePos = Find-Position -Text $branchFlowText -Needle "run_branch_merge(flag)"
    $runChooseBoxPos = Find-Position -Text $branchFlowText -Needle "run_choose_box(flag)"
    $whileMergeCallPos = Find-Position -Text $loopTryFlowText -Needle "picked(7)" -CharacterOffset 7
    $doWhileOverrideCallPos = Find-Position -Text $loopTryFlowText -Needle "picked(8)" -CharacterOffset 7
    $forMergeCallPos = Find-Position -Text $loopTryFlowText -Needle "picked(9)" -CharacterOffset 7
    $foreachMergeCallPos = Find-Position -Text $loopTryFlowText -Needle "picked(10)" -CharacterOffset 7
    $tryCatchMergeCallPos = Find-Position -Text $loopTryFlowText -Needle "picked(11)" -CharacterOffset 7
    $tryFinallyForwardCallPos = Find-Position -Text $loopTryFlowText -Needle "forwarded(12)" -CharacterOffset 10
    $tryFinallyOverrideCallPos = Find-Position -Text $loopTryFlowText -Needle "picked(13)" -CharacterOffset 7
    $runWhileMergePos = Find-Position -Text $loopTryFlowText -Needle "run_while_merge(flag)"
    $runDoWhileOverridePos = Find-Position -Text $loopTryFlowText -Needle "run_do_while_override()"
    $runForMergePos = Find-Position -Text $loopTryFlowText -Needle "run_for_merge(flag)"
    $runForeachMergePos = Find-Position -Text $loopTryFlowText -Needle "run_foreach_merge(items)"
    $runTryCatchMergePos = Find-Position -Text $loopTryFlowText -Needle "run_try_catch_merge(flag)"
    $runTryFinallyForwardPos = Find-Position -Text $loopTryFlowText -Needle "run_try_finally_forward(flag)"
    $runTryFinallyOverridePos = Find-Position -Text $loopTryFlowText -Needle "run_try_finally_override(flag)"
    $arrayLiteralFirstCallPos = Find-Position -Text $collectionFlowText -Needle "handlers[0](14)" -CharacterOffset 12
    $arrayLiteralSecondCallPos = Find-Position -Text $collectionFlowText -Needle "handlers[1](15)" -CharacterOffset 12
    $arrayAssignmentFirstCallPos = Find-Position -Text $collectionFlowText -Needle "handlers[0](16)" -CharacterOffset 12
    $arrayAssignmentSecondCallPos = Find-Position -Text $collectionFlowText -Needle "handlers[1](17)" -CharacterOffset 12
    $returnedArrayFirstCallPos = Find-Position -Text $collectionFlowText -Needle "handlers[0](18)" -CharacterOffset 12
    $returnedArraySecondCallPos = Find-Position -Text $collectionFlowText -Needle "handlers[1](19)" -CharacterOffset 12
    $returnedDictLiteralCallPos = Find-Position -Text $collectionFlowText -Needle 'bag["fn"](20)' -CharacterOffset 10
    $runArrayLiteralPos = Find-Position -Text $collectionFlowText -Needle "run_array_literal_calls()"
    $runArrayAssignmentPos = Find-Position -Text $collectionFlowText -Needle "run_array_assignment_calls()"
    $runReturnedArrayPos = Find-Position -Text $collectionFlowText -Needle "run_returned_array_calls()"
    $runReturnedDictLiteralPos = Find-Position -Text $collectionFlowText -Needle "run_returned_dict_literal(flag)"
    $whileFixpointCallPos = Find-Position -Text $loopFixpointFlowText -Needle "picked(25)" -CharacterOffset 7
    $doWhileFixpointCallPos = Find-Position -Text $loopFixpointFlowText -Needle "picked(26)" -CharacterOffset 7
    $forFixpointCallPos = Find-Position -Text $loopFixpointFlowText -Needle "picked(27)" -CharacterOffset 7
    $foreachFixpointCallPos = Find-Position -Text $loopFixpointFlowText -Needle "picked(28)" -CharacterOffset 7
    $runWhileFixpointPos = Find-Position -Text $loopFixpointFlowText -Needle "run_while_fixpoint(flag)"
    $runDoWhileFixpointPos = Find-Position -Text $loopFixpointFlowText -Needle "run_do_while_fixpoint(flag)"
    $runForFixpointPos = Find-Position -Text $loopFixpointFlowText -Needle "run_for_fixpoint(flag)"
    $runForeachFixpointPos = Find-Position -Text $loopFixpointFlowText -Needle "run_foreach_fixpoint(items)"
    $breakStopsTailCallPos = Find-Position -Text $loopControlFlowText -Needle "picked(29)" -CharacterOffset 7
    $nestedBreakCallPos = Find-Position -Text $loopControlFlowText -Needle "picked(30)" -CharacterOffset 7
    $doWhileContinueExactCallPos = Find-Position -Text $loopControlFlowText -Needle "picked(31)" -CharacterOffset 7
    $doWhileReturnCallPos = Find-Position -Text $loopControlFlowText -Needle "picked(32)" -CharacterOffset 7
    $tryBreakFinallyCallPos = Find-Position -Text $loopControlFlowText -Needle "forwarded(33)" -CharacterOffset 10
    $tryContinueFinallyCallPos = Find-Position -Text $loopControlFlowText -Needle "forwarded(34)" -CharacterOffset 10
    $tryFinallyReturnCallPos = Find-Position -Text $loopControlFlowText -Needle "picked(35)" -CharacterOffset 7
    $runBreakStopsTailPos = Find-Position -Text $loopControlFlowText -Needle "run_break_stops_tail(flag)"
    $runNestedBreakPos = Find-Position -Text $loopControlFlowText -Needle "run_nested_break(flag)"
    $runDoWhileContinueExactPos = Find-Position -Text $loopControlFlowText -Needle "run_do_while_continue_exact(flag)"
    $runDoWhileReturnPos = Find-Position -Text $loopControlFlowText -Needle "run_do_while_return()"
    $runTryBreakFinallyPos = Find-Position -Text $loopControlFlowText -Needle "run_try_break_finally(flag)"
    $runTryContinueFinallyPos = Find-Position -Text $loopControlFlowText -Needle "run_try_continue_finally(flag)"
    $runTryFinallyReturnPos = Find-Position -Text $loopControlFlowText -Needle "run_try_finally_return()"

    $localAliasSignatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $callableAliasPath) }
        position = $localAliasCallPos
    })
    $localAliasSignatureLabel = if ($localAliasSignatureHelp.signatures.Count -gt 0) { [string]$localAliasSignatureHelp.signatures[0].label } else { "" }
    Assert-True -Name "lsp_local_callable_alias_signature_help" -Condition ($localAliasSignatureLabel.Contains("v")) -Details "Expected signature help for local callable alias. Actual: $(Format-JsonCompact $localAliasSignatureHelp)"

    $plusAliasSignatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $callableAliasPath) }
        position = $plusAliasCallPos
    })
    $plusAliasSignatureLabel = if ($plusAliasSignatureHelp.signatures.Count -gt 0) { [string]$plusAliasSignatureHelp.signatures[0].label } else { "" }
    Assert-True -Name "lsp_import_callable_alias_signature_help" -Condition ($plusAliasSignatureLabel.Contains("v")) -Details "Expected signature help for import callable alias. Actual: $(Format-JsonCompact $plusAliasSignatureHelp)"

    $aliasInlayHints = Send-LspRequest -Process $process -Method "textDocument/inlayHint" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $callableAliasPath) }
        range = @{
            start = @{ line = 0; character = 0 }
            end = @{ line = 20; character = 0 }
        }
    })
    $aliasParamHintCount = 0
    foreach ($hint in @($aliasInlayHints)) {
        if ($null -ne $hint -and [string]$hint.label -eq "v:") {
            $aliasParamHintCount++
        }
    }
    Assert-True -Name "lsp_callable_alias_inlay_hints" -Condition ($aliasParamHintCount -ge 2) -Details "Expected inlay hints for both callable alias call sites. Actual: $(Format-JsonCompact $aliasInlayHints)"

    $incomingCallsAfterAlias = Send-LspRequest -Process $process -Method "callHierarchy/incomingCalls" -Params ([ordered]@{
        item = $callHierarchyItem
    })
    $runAliasIncoming = Find-CallHierarchyItem -Calls $incomingCallsAfterAlias -FromName "run_alias"
    $runAliasIncomingCount = if ($null -ne $runAliasIncoming) { @($runAliasIncoming.fromRanges).Count } else { 0 }
    Assert-True -Name "lsp_callable_alias_incoming_call_hierarchy" -Condition ($runAliasIncomingCount -ge 2) -Details "Expected incoming call hierarchy for direct and local callable aliases. Actual: $(Format-JsonCompact $incomingCallsAfterAlias)"

    $runAliasCallHierarchyItems = Send-LspRequest -Process $process -Method "textDocument/prepareCallHierarchy" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $callableAliasPath) }
        position = $runAliasPos
    })
    $runAliasCallHierarchyItem = if (Has-LspResultItems $runAliasCallHierarchyItems) { @($runAliasCallHierarchyItems)[0] } else { $null }
    $outgoingAliasCalls = if ($null -ne $runAliasCallHierarchyItem) {
        Send-LspRequest -Process $process -Method "callHierarchy/outgoingCalls" -Params ([ordered]@{
            item = $runAliasCallHierarchyItem
        })
    } else {
        $null
    }
    $hasOutgoingAliasAddBase = $false
    foreach ($call in @($outgoingAliasCalls)) {
        if ($null -ne $call -and $null -ne $call.to -and $call.to.name -eq "add_base") {
            $hasOutgoingAliasAddBase = $true
            break
        }
    }
    Assert-True -Name "lsp_callable_alias_outgoing_call_hierarchy" -Condition $hasOutgoingAliasAddBase -Details "Expected outgoing call hierarchy entry for callable aliases. Actual: $(Format-JsonCompact $outgoingAliasCalls)"

    $rebindForwardedSignatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $callableRebindPath) }
        position = $rebindForwardedCallPos
    })
    $rebindForwardedSignatureLabel = if ($rebindForwardedSignatureHelp.signatures.Count -gt 0) { [string]$rebindForwardedSignatureHelp.signatures[0].label } else { "" }
    Assert-True -Name "lsp_callable_rebind_forwarded_signature_help" -Condition ($rebindForwardedSignatureLabel.Contains("x")) -Details "Expected signature help for forwarded rebinding alias to resolve to scale_twice(x). Actual: $(Format-JsonCompact $rebindForwardedSignatureHelp)"

    $rebindLocalSignatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $callableRebindPath) }
        position = $rebindLocalCallPos
    })
    $rebindLocalSignatureLabel = if ($rebindLocalSignatureHelp.signatures.Count -gt 0) { [string]$rebindLocalSignatureHelp.signatures[0].label } else { "" }
    Assert-True -Name "lsp_callable_rebind_local_signature_help" -Condition ($rebindLocalSignatureLabel.Contains("x")) -Details "Expected signature help for rebound local alias to resolve to scale_twice(x). Actual: $(Format-JsonCompact $rebindLocalSignatureHelp)"

    $rebindInlayHints = Send-LspRequest -Process $process -Method "textDocument/inlayHint" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $callableRebindPath) }
        range = @{
            start = @{ line = 0; character = 0 }
            end = @{ line = 30; character = 0 }
        }
    })
    $rebindParamHintCount = 0
    foreach ($hint in @($rebindInlayHints)) {
        if ($null -ne $hint -and [string]$hint.label -eq "x:") {
            $rebindParamHintCount++
        }
    }
    Assert-True -Name "lsp_callable_rebind_inlay_hints" -Condition ($rebindParamHintCount -ge 2) -Details "Expected inlay hints for rebound callable aliases. Actual: $(Format-JsonCompact $rebindInlayHints)"

    $scaleCallHierarchyItems = Send-LspRequest -Process $process -Method "textDocument/prepareCallHierarchy" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $modulePath) }
        position = $moduleScalePos
    })
    $scaleCallHierarchyItem = if (Has-LspResultItems $scaleCallHierarchyItems) { @($scaleCallHierarchyItems)[0] } else { $null }
    $scaleIncomingCalls = if ($null -ne $scaleCallHierarchyItem) {
        Send-LspRequest -Process $process -Method "callHierarchy/incomingCalls" -Params ([ordered]@{
            item = $scaleCallHierarchyItem
        })
    } else {
        $null
    }
    $runRebindIncoming = Find-CallHierarchyItem -Calls $scaleIncomingCalls -FromName "run_rebind"
    $runRebindIncomingCount = if ($null -ne $runRebindIncoming) { @($runRebindIncoming.fromRanges).Count } else { 0 }
    Assert-True -Name "lsp_callable_rebind_incoming_call_hierarchy" -Condition ($runRebindIncomingCount -ge 2) -Details "Expected incoming call hierarchy for rebound callable aliases. Actual: $(Format-JsonCompact $scaleIncomingCalls)"

    $runRebindCallHierarchyItems = Send-LspRequest -Process $process -Method "textDocument/prepareCallHierarchy" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $callableRebindPath) }
        position = $runRebindPos
    })
    $runRebindCallHierarchyItem = if (Has-LspResultItems $runRebindCallHierarchyItems) { @($runRebindCallHierarchyItems)[0] } else { $null }
    $outgoingRebindCalls = if ($null -ne $runRebindCallHierarchyItem) {
        Send-LspRequest -Process $process -Method "callHierarchy/outgoingCalls" -Params ([ordered]@{
            item = $runRebindCallHierarchyItem
        })
    } else {
        $null
    }
    $hasOutgoingScaleTwice = $false
    foreach ($call in @($outgoingRebindCalls)) {
        if ($null -ne $call -and $null -ne $call.to -and $call.to.name -eq "scale_twice") {
            $hasOutgoingScaleTwice = $true
            break
        }
    }
    Assert-True -Name "lsp_callable_rebind_outgoing_call_hierarchy" -Condition $hasOutgoingScaleTwice -Details "Expected outgoing call hierarchy entry for rebound callable aliases. Actual: $(Format-JsonCompact $outgoingRebindCalls)"

    $chooseCallableSignatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $branchFlowPath) }
        position = $chooseCallableCallPos
    })
    $chooseCallableSignatureLabels = @(Get-SignatureLabels -SignatureHelp $chooseCallableSignatureHelp)
    $hasChooseCallableV = $false
    $hasChooseCallableX = $false
    foreach ($label in $chooseCallableSignatureLabels) {
        if ($label.Contains("v")) {
            $hasChooseCallableV = $true
        }

        if ($label.Contains("x")) {
            $hasChooseCallableX = $true
        }
    }
    Assert-True -Name "lsp_branch_return_callable_signature_help" -Condition ($hasChooseCallableV -and $hasChooseCallableX -and $chooseCallableSignatureLabels.Count -ge 2) -Details "Expected ambiguous signature help for callable returned from branch. Actual: $(Format-JsonCompact $chooseCallableSignatureHelp)"

    $branchMergeSignatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $branchFlowPath) }
        position = $branchMergeCallPos
    })
    $branchMergeSignatureLabels = @(Get-SignatureLabels -SignatureHelp $branchMergeSignatureHelp)
    $hasBranchMergeV = $false
    $hasBranchMergeX = $false
    foreach ($label in $branchMergeSignatureLabels) {
        if ($label.Contains("v")) {
            $hasBranchMergeV = $true
        }

        if ($label.Contains("x")) {
            $hasBranchMergeX = $true
        }
    }
    Assert-True -Name "lsp_branch_merge_callable_signature_help" -Condition ($hasBranchMergeV -and $hasBranchMergeX -and $branchMergeSignatureLabels.Count -ge 2) -Details "Expected merged signature help for branch-reassigned callable. Actual: $(Format-JsonCompact $branchMergeSignatureHelp)"

    $chooseBoxSignatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $branchFlowPath) }
        position = $chooseBoxCallPos
    })
    $chooseBoxSignatureLabels = @(Get-SignatureLabels -SignatureHelp $chooseBoxSignatureHelp)
    $hasChooseBoxV = $false
    $hasChooseBoxX = $false
    foreach ($label in $chooseBoxSignatureLabels) {
        if ($label.Contains("v")) {
            $hasChooseBoxV = $true
        }

        if ($label.Contains("x")) {
            $hasChooseBoxX = $true
        }
    }
    Assert-True -Name "lsp_branch_return_container_signature_help" -Condition ($hasChooseBoxV -and $hasChooseBoxX -and $chooseBoxSignatureLabels.Count -ge 2) -Details "Expected merged signature help for callable reached through returned container branches. Actual: $(Format-JsonCompact $chooseBoxSignatureHelp)"

    $branchInlayHints = Send-LspRequest -Process $process -Method "textDocument/inlayHint" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $branchFlowPath) }
        range = @{
            start = @{ line = 0; character = 0 }
            end = @{ line = 80; character = 0 }
        }
    })
    $hasAmbiguousParamHint = $false
    foreach ($hint in @($branchInlayHints)) {
        if ($null -eq $hint) {
            continue
        }

        if ([string]$hint.label -eq "v:" -or [string]$hint.label -eq "x:") {
            $hasAmbiguousParamHint = $true
            break
        }
    }
    Assert-True -Name "lsp_branch_ambiguous_inlay_hints_suppressed" -Condition (-not $hasAmbiguousParamHint) -Details "Expected no parameter inlay hints for ambiguous branch-resolved call sites. Actual: $(Format-JsonCompact $branchInlayHints)"

    $incomingCallsAfterBranch = Send-LspRequest -Process $process -Method "callHierarchy/incomingCalls" -Params ([ordered]@{
        item = $callHierarchyItem
    })
    $runChooseCallableIncoming = Find-CallHierarchyItem -Calls $incomingCallsAfterBranch -FromName "run_choose_callable"
    $runBranchMergeIncoming = Find-CallHierarchyItem -Calls $incomingCallsAfterBranch -FromName "run_branch_merge"
    $runChooseBoxIncoming = Find-CallHierarchyItem -Calls $incomingCallsAfterBranch -FromName "run_choose_box"
    $runChooseCallableIncomingCount = if ($null -ne $runChooseCallableIncoming) { @($runChooseCallableIncoming.fromRanges).Count } else { 0 }
    $runBranchMergeIncomingCount = if ($null -ne $runBranchMergeIncoming) { @($runBranchMergeIncoming.fromRanges).Count } else { 0 }
    $runChooseBoxIncomingCount = if ($null -ne $runChooseBoxIncoming) { @($runChooseBoxIncoming.fromRanges).Count } else { 0 }
    Assert-True -Name "lsp_branch_return_callable_incoming_add_base" -Condition ($runChooseCallableIncomingCount -ge 1) -Details "Expected add_base incoming call hierarchy through branch-returned callable. Actual: $(Format-JsonCompact $incomingCallsAfterBranch)"
    Assert-True -Name "lsp_branch_merge_callable_incoming_add_base" -Condition ($runBranchMergeIncomingCount -ge 1) -Details "Expected add_base incoming call hierarchy through branch merge. Actual: $(Format-JsonCompact $incomingCallsAfterBranch)"
    Assert-True -Name "lsp_branch_return_container_incoming_add_base" -Condition ($runChooseBoxIncomingCount -ge 1) -Details "Expected add_base incoming call hierarchy through returned container branch. Actual: $(Format-JsonCompact $incomingCallsAfterBranch)"

    $scaleIncomingAfterBranch = Send-LspRequest -Process $process -Method "callHierarchy/incomingCalls" -Params ([ordered]@{
        item = $scaleCallHierarchyItem
    })
    $runChooseCallableScaleIncoming = Find-CallHierarchyItem -Calls $scaleIncomingAfterBranch -FromName "run_choose_callable"
    $runBranchMergeScaleIncoming = Find-CallHierarchyItem -Calls $scaleIncomingAfterBranch -FromName "run_branch_merge"
    $runChooseBoxScaleIncoming = Find-CallHierarchyItem -Calls $scaleIncomingAfterBranch -FromName "run_choose_box"
    $runChooseCallableScaleIncomingCount = if ($null -ne $runChooseCallableScaleIncoming) { @($runChooseCallableScaleIncoming.fromRanges).Count } else { 0 }
    $runBranchMergeScaleIncomingCount = if ($null -ne $runBranchMergeScaleIncoming) { @($runBranchMergeScaleIncoming.fromRanges).Count } else { 0 }
    $runChooseBoxScaleIncomingCount = if ($null -ne $runChooseBoxScaleIncoming) { @($runChooseBoxScaleIncoming.fromRanges).Count } else { 0 }
    Assert-True -Name "lsp_branch_return_callable_incoming_scale_twice" -Condition ($runChooseCallableScaleIncomingCount -ge 1) -Details "Expected scale_twice incoming call hierarchy through branch-returned callable. Actual: $(Format-JsonCompact $scaleIncomingAfterBranch)"
    Assert-True -Name "lsp_branch_merge_callable_incoming_scale_twice" -Condition ($runBranchMergeScaleIncomingCount -ge 1) -Details "Expected scale_twice incoming call hierarchy through branch merge. Actual: $(Format-JsonCompact $scaleIncomingAfterBranch)"
    Assert-True -Name "lsp_branch_return_container_incoming_scale_twice" -Condition ($runChooseBoxScaleIncomingCount -ge 1) -Details "Expected scale_twice incoming call hierarchy through returned container branch. Actual: $(Format-JsonCompact $scaleIncomingAfterBranch)"

    $runChooseCallableCallHierarchyItems = Send-LspRequest -Process $process -Method "textDocument/prepareCallHierarchy" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $branchFlowPath) }
        position = $runChooseCallablePos
    })
    $runChooseCallableCallHierarchyItem = if (Has-LspResultItems $runChooseCallableCallHierarchyItems) { @($runChooseCallableCallHierarchyItems)[0] } else { $null }
    $outgoingChooseCallableCalls = if ($null -ne $runChooseCallableCallHierarchyItem) {
        Send-LspRequest -Process $process -Method "callHierarchy/outgoingCalls" -Params ([ordered]@{
            item = $runChooseCallableCallHierarchyItem
        })
    } else {
        $null
    }
    $hasOutgoingChooseCallableAddBase = $false
    $hasOutgoingChooseCallableScaleTwice = $false
    foreach ($call in @($outgoingChooseCallableCalls)) {
        if ($null -eq $call -or $null -eq $call.to) {
            continue
        }

        if ($call.to.name -eq "add_base") {
            $hasOutgoingChooseCallableAddBase = $true
        }

        if ($call.to.name -eq "scale_twice") {
            $hasOutgoingChooseCallableScaleTwice = $true
        }
    }
    Assert-True -Name "lsp_branch_return_callable_outgoing_call_hierarchy" -Condition ($hasOutgoingChooseCallableAddBase -and $hasOutgoingChooseCallableScaleTwice) -Details "Expected outgoing call hierarchy entries for both branch-returned callable targets. Actual: $(Format-JsonCompact $outgoingChooseCallableCalls)"

    $runBranchMergeCallHierarchyItems = Send-LspRequest -Process $process -Method "textDocument/prepareCallHierarchy" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $branchFlowPath) }
        position = $runBranchMergePos
    })
    $runBranchMergeCallHierarchyItem = if (Has-LspResultItems $runBranchMergeCallHierarchyItems) { @($runBranchMergeCallHierarchyItems)[0] } else { $null }
    $outgoingBranchMergeCalls = if ($null -ne $runBranchMergeCallHierarchyItem) {
        Send-LspRequest -Process $process -Method "callHierarchy/outgoingCalls" -Params ([ordered]@{
            item = $runBranchMergeCallHierarchyItem
        })
    } else {
        $null
    }
    $hasOutgoingBranchMergeAddBase = $false
    $hasOutgoingBranchMergeScaleTwice = $false
    foreach ($call in @($outgoingBranchMergeCalls)) {
        if ($null -eq $call -or $null -eq $call.to) {
            continue
        }

        if ($call.to.name -eq "add_base") {
            $hasOutgoingBranchMergeAddBase = $true
        }

        if ($call.to.name -eq "scale_twice") {
            $hasOutgoingBranchMergeScaleTwice = $true
        }
    }
    Assert-True -Name "lsp_branch_merge_callable_outgoing_call_hierarchy" -Condition ($hasOutgoingBranchMergeAddBase -and $hasOutgoingBranchMergeScaleTwice) -Details "Expected outgoing call hierarchy entries for both branch-merged callable targets. Actual: $(Format-JsonCompact $outgoingBranchMergeCalls)"

    $runChooseBoxCallHierarchyItems = Send-LspRequest -Process $process -Method "textDocument/prepareCallHierarchy" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $branchFlowPath) }
        position = $runChooseBoxPos
    })
    $runChooseBoxCallHierarchyItem = if (Has-LspResultItems $runChooseBoxCallHierarchyItems) { @($runChooseBoxCallHierarchyItems)[0] } else { $null }
    $outgoingChooseBoxCalls = if ($null -ne $runChooseBoxCallHierarchyItem) {
        Send-LspRequest -Process $process -Method "callHierarchy/outgoingCalls" -Params ([ordered]@{
            item = $runChooseBoxCallHierarchyItem
        })
    } else {
        $null
    }
    $hasOutgoingChooseBoxAddBase = $false
    $hasOutgoingChooseBoxScaleTwice = $false
    foreach ($call in @($outgoingChooseBoxCalls)) {
        if ($null -eq $call -or $null -eq $call.to) {
            continue
        }

        if ($call.to.name -eq "add_base") {
            $hasOutgoingChooseBoxAddBase = $true
        }

        if ($call.to.name -eq "scale_twice") {
            $hasOutgoingChooseBoxScaleTwice = $true
        }
    }
    Assert-True -Name "lsp_branch_return_container_outgoing_call_hierarchy" -Condition ($hasOutgoingChooseBoxAddBase -and $hasOutgoingChooseBoxScaleTwice) -Details "Expected outgoing call hierarchy entries for both returned-container branch targets. Actual: $(Format-JsonCompact $outgoingChooseBoxCalls)"

    $whileMergeSignatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopTryFlowPath) }
        position = $whileMergeCallPos
    })
    $whileMergeSignatureLabels = @(Get-SignatureLabels -SignatureHelp $whileMergeSignatureHelp)
    Assert-True -Name "lsp_while_merge_signature_help" -Condition ($whileMergeSignatureLabels.Count -ge 2 -and ($whileMergeSignatureLabels -match "v").Count -ge 1 -and ($whileMergeSignatureLabels -match "x").Count -ge 1) -Details "Expected merged signature help for while-loop flow. Actual: $(Format-JsonCompact $whileMergeSignatureHelp)"

    $doWhileOverrideSignatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopTryFlowPath) }
        position = $doWhileOverrideCallPos
    })
    $doWhileOverrideSignatureLabels = @(Get-SignatureLabels -SignatureHelp $doWhileOverrideSignatureHelp)
    Assert-True -Name "lsp_do_while_override_signature_help" -Condition ($doWhileOverrideSignatureLabels.Count -eq 1 -and $doWhileOverrideSignatureLabels[0].Contains("x")) -Details "Expected exact signature help for do-while override flow. Actual: $(Format-JsonCompact $doWhileOverrideSignatureHelp)"

    $forMergeSignatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopTryFlowPath) }
        position = $forMergeCallPos
    })
    $forMergeSignatureLabels = @(Get-SignatureLabels -SignatureHelp $forMergeSignatureHelp)
    Assert-True -Name "lsp_for_merge_signature_help" -Condition ($forMergeSignatureLabels.Count -ge 2 -and ($forMergeSignatureLabels -match "v").Count -ge 1 -and ($forMergeSignatureLabels -match "x").Count -ge 1) -Details "Expected merged signature help for for-loop flow. Actual: $(Format-JsonCompact $forMergeSignatureHelp)"

    $foreachMergeSignatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopTryFlowPath) }
        position = $foreachMergeCallPos
    })
    $foreachMergeSignatureLabels = @(Get-SignatureLabels -SignatureHelp $foreachMergeSignatureHelp)
    Assert-True -Name "lsp_foreach_merge_signature_help" -Condition ($foreachMergeSignatureLabels.Count -ge 2 -and ($foreachMergeSignatureLabels -match "v").Count -ge 1 -and ($foreachMergeSignatureLabels -match "x").Count -ge 1) -Details "Expected merged signature help for foreach flow. Actual: $(Format-JsonCompact $foreachMergeSignatureHelp)"

    $tryCatchMergeSignatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopTryFlowPath) }
        position = $tryCatchMergeCallPos
    })
    $tryCatchMergeSignatureLabels = @(Get-SignatureLabels -SignatureHelp $tryCatchMergeSignatureHelp)
    Assert-True -Name "lsp_try_catch_merge_signature_help" -Condition ($tryCatchMergeSignatureLabels.Count -ge 2 -and ($tryCatchMergeSignatureLabels -match "v").Count -ge 1 -and ($tryCatchMergeSignatureLabels -match "x").Count -ge 1) -Details "Expected merged signature help for try/catch flow. Actual: $(Format-JsonCompact $tryCatchMergeSignatureHelp)"

    $tryFinallyForwardSignatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopTryFlowPath) }
        position = $tryFinallyForwardCallPos
    })
    $tryFinallyForwardSignatureLabels = @(Get-SignatureLabels -SignatureHelp $tryFinallyForwardSignatureHelp)
    Assert-True -Name "lsp_try_finally_forward_signature_help" -Condition ($tryFinallyForwardSignatureLabels.Count -ge 2 -and ($tryFinallyForwardSignatureLabels -match "v").Count -ge 1 -and ($tryFinallyForwardSignatureLabels -match "x").Count -ge 1) -Details "Expected merged signature help for finally-forwarded flow. Actual: $(Format-JsonCompact $tryFinallyForwardSignatureHelp)"

    $tryFinallyOverrideSignatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopTryFlowPath) }
        position = $tryFinallyOverrideCallPos
    })
    $tryFinallyOverrideSignatureLabels = @(Get-SignatureLabels -SignatureHelp $tryFinallyOverrideSignatureHelp)
    Assert-True -Name "lsp_try_finally_override_signature_help" -Condition ($tryFinallyOverrideSignatureLabels.Count -eq 1 -and $tryFinallyOverrideSignatureLabels[0].Contains("x")) -Details "Expected exact signature help for finally override flow. Actual: $(Format-JsonCompact $tryFinallyOverrideSignatureHelp)"

    $loopTryInlayHints = Send-LspRequest -Process $process -Method "textDocument/inlayHint" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopTryFlowPath) }
        range = @{
            start = @{ line = 0; character = 0 }
            end = @{ line = 120; character = 0 }
        }
    })
    $loopTryXHintCount = 0
    $loopTryVHintCount = 0
    foreach ($hint in @($loopTryInlayHints)) {
        if ($null -eq $hint) {
            continue
        }

        if ([string]$hint.label -eq "x:") {
            $loopTryXHintCount++
        }

        if ([string]$hint.label -eq "v:") {
            $loopTryVHintCount++
        }
    }
    Assert-True -Name "lsp_loop_try_inlay_hint_precision" -Condition ($loopTryXHintCount -eq 2 -and $loopTryVHintCount -eq 0) -Details "Expected inlay hints only for exact x-resolved do-while/finally override calls. Actual: $(Format-JsonCompact $loopTryInlayHints)"

    $runWhileMergeCallHierarchyItems = Send-LspRequest -Process $process -Method "textDocument/prepareCallHierarchy" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopTryFlowPath) }
        position = $runWhileMergePos
    })
    $runWhileMergeCallHierarchyItem = if (Has-LspResultItems $runWhileMergeCallHierarchyItems) { @($runWhileMergeCallHierarchyItems)[0] } else { $null }
    $outgoingWhileMergeCalls = if ($null -ne $runWhileMergeCallHierarchyItem) {
        Send-LspRequest -Process $process -Method "callHierarchy/outgoingCalls" -Params ([ordered]@{
            item = $runWhileMergeCallHierarchyItem
        })
    } else {
        $null
    }
    $hasOutgoingWhileAddBase = $false
    $hasOutgoingWhileScaleTwice = $false
    foreach ($call in @($outgoingWhileMergeCalls)) {
        if ($null -eq $call -or $null -eq $call.to) {
            continue
        }

        if ($call.to.name -eq "add_base") {
            $hasOutgoingWhileAddBase = $true
        }

        if ($call.to.name -eq "scale_twice") {
            $hasOutgoingWhileScaleTwice = $true
        }
    }
    Assert-True -Name "lsp_while_merge_outgoing_call_hierarchy" -Condition ($hasOutgoingWhileAddBase -and $hasOutgoingWhileScaleTwice) -Details "Expected outgoing call hierarchy entries for both while-loop merged targets. Actual: $(Format-JsonCompact $outgoingWhileMergeCalls)"

    $runDoWhileOverrideCallHierarchyItems = Send-LspRequest -Process $process -Method "textDocument/prepareCallHierarchy" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopTryFlowPath) }
        position = $runDoWhileOverridePos
    })
    $runDoWhileOverrideCallHierarchyItem = if (Has-LspResultItems $runDoWhileOverrideCallHierarchyItems) { @($runDoWhileOverrideCallHierarchyItems)[0] } else { $null }
    $outgoingDoWhileOverrideCalls = if ($null -ne $runDoWhileOverrideCallHierarchyItem) {
        Send-LspRequest -Process $process -Method "callHierarchy/outgoingCalls" -Params ([ordered]@{
            item = $runDoWhileOverrideCallHierarchyItem
        })
    } else {
        $null
    }
    $hasOutgoingDoWhileScaleTwice = $false
    $hasOutgoingDoWhileAddBase = $false
    foreach ($call in @($outgoingDoWhileOverrideCalls)) {
        if ($null -eq $call -or $null -eq $call.to) {
            continue
        }

        if ($call.to.name -eq "scale_twice") {
            $hasOutgoingDoWhileScaleTwice = $true
        }

        if ($call.to.name -eq "add_base") {
            $hasOutgoingDoWhileAddBase = $true
        }
    }
    Assert-True -Name "lsp_do_while_override_outgoing_call_hierarchy" -Condition ($hasOutgoingDoWhileScaleTwice -and (-not $hasOutgoingDoWhileAddBase)) -Details "Expected outgoing call hierarchy to resolve only to scale_twice for do-while override. Actual: $(Format-JsonCompact $outgoingDoWhileOverrideCalls)"

    $runForMergeCallHierarchyItems = Send-LspRequest -Process $process -Method "textDocument/prepareCallHierarchy" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopTryFlowPath) }
        position = $runForMergePos
    })
    $runForMergeCallHierarchyItem = if (Has-LspResultItems $runForMergeCallHierarchyItems) { @($runForMergeCallHierarchyItems)[0] } else { $null }
    $outgoingForMergeCalls = if ($null -ne $runForMergeCallHierarchyItem) {
        Send-LspRequest -Process $process -Method "callHierarchy/outgoingCalls" -Params ([ordered]@{
            item = $runForMergeCallHierarchyItem
        })
    } else {
        $null
    }
    $hasOutgoingForAddBase = $false
    $hasOutgoingForScaleTwice = $false
    foreach ($call in @($outgoingForMergeCalls)) {
        if ($null -eq $call -or $null -eq $call.to) {
            continue
        }

        if ($call.to.name -eq "add_base") {
            $hasOutgoingForAddBase = $true
        }

        if ($call.to.name -eq "scale_twice") {
            $hasOutgoingForScaleTwice = $true
        }
    }
    Assert-True -Name "lsp_for_merge_outgoing_call_hierarchy" -Condition ($hasOutgoingForAddBase -and $hasOutgoingForScaleTwice) -Details "Expected outgoing call hierarchy entries for both for-loop merged targets. Actual: $(Format-JsonCompact $outgoingForMergeCalls)"

    $runForeachMergeCallHierarchyItems = Send-LspRequest -Process $process -Method "textDocument/prepareCallHierarchy" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopTryFlowPath) }
        position = $runForeachMergePos
    })
    $runForeachMergeCallHierarchyItem = if (Has-LspResultItems $runForeachMergeCallHierarchyItems) { @($runForeachMergeCallHierarchyItems)[0] } else { $null }
    $outgoingForeachMergeCalls = if ($null -ne $runForeachMergeCallHierarchyItem) {
        Send-LspRequest -Process $process -Method "callHierarchy/outgoingCalls" -Params ([ordered]@{
            item = $runForeachMergeCallHierarchyItem
        })
    } else {
        $null
    }
    $hasOutgoingForeachAddBase = $false
    $hasOutgoingForeachScaleTwice = $false
    foreach ($call in @($outgoingForeachMergeCalls)) {
        if ($null -eq $call -or $null -eq $call.to) {
            continue
        }

        if ($call.to.name -eq "add_base") {
            $hasOutgoingForeachAddBase = $true
        }

        if ($call.to.name -eq "scale_twice") {
            $hasOutgoingForeachScaleTwice = $true
        }
    }
    Assert-True -Name "lsp_foreach_merge_outgoing_call_hierarchy" -Condition ($hasOutgoingForeachAddBase -and $hasOutgoingForeachScaleTwice) -Details "Expected outgoing call hierarchy entries for both foreach merged targets. Actual: $(Format-JsonCompact $outgoingForeachMergeCalls)"

    $runTryCatchMergeCallHierarchyItems = Send-LspRequest -Process $process -Method "textDocument/prepareCallHierarchy" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopTryFlowPath) }
        position = $runTryCatchMergePos
    })
    $runTryCatchMergeCallHierarchyItem = if (Has-LspResultItems $runTryCatchMergeCallHierarchyItems) { @($runTryCatchMergeCallHierarchyItems)[0] } else { $null }
    $outgoingTryCatchMergeCalls = if ($null -ne $runTryCatchMergeCallHierarchyItem) {
        Send-LspRequest -Process $process -Method "callHierarchy/outgoingCalls" -Params ([ordered]@{
            item = $runTryCatchMergeCallHierarchyItem
        })
    } else {
        $null
    }
    $hasOutgoingTryCatchAddBase = $false
    $hasOutgoingTryCatchScaleTwice = $false
    foreach ($call in @($outgoingTryCatchMergeCalls)) {
        if ($null -eq $call -or $null -eq $call.to) {
            continue
        }

        if ($call.to.name -eq "add_base") {
            $hasOutgoingTryCatchAddBase = $true
        }

        if ($call.to.name -eq "scale_twice") {
            $hasOutgoingTryCatchScaleTwice = $true
        }
    }
    Assert-True -Name "lsp_try_catch_merge_outgoing_call_hierarchy" -Condition ($hasOutgoingTryCatchAddBase -and $hasOutgoingTryCatchScaleTwice) -Details "Expected outgoing call hierarchy entries for both try/catch merged targets. Actual: $(Format-JsonCompact $outgoingTryCatchMergeCalls)"

    $runTryFinallyForwardCallHierarchyItems = Send-LspRequest -Process $process -Method "textDocument/prepareCallHierarchy" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopTryFlowPath) }
        position = $runTryFinallyForwardPos
    })
    $runTryFinallyForwardCallHierarchyItem = if (Has-LspResultItems $runTryFinallyForwardCallHierarchyItems) { @($runTryFinallyForwardCallHierarchyItems)[0] } else { $null }
    $outgoingTryFinallyForwardCalls = if ($null -ne $runTryFinallyForwardCallHierarchyItem) {
        Send-LspRequest -Process $process -Method "callHierarchy/outgoingCalls" -Params ([ordered]@{
            item = $runTryFinallyForwardCallHierarchyItem
        })
    } else {
        $null
    }
    $hasOutgoingTryFinallyForwardAddBase = $false
    $hasOutgoingTryFinallyForwardScaleTwice = $false
    foreach ($call in @($outgoingTryFinallyForwardCalls)) {
        if ($null -eq $call -or $null -eq $call.to) {
            continue
        }

        if ($call.to.name -eq "add_base") {
            $hasOutgoingTryFinallyForwardAddBase = $true
        }

        if ($call.to.name -eq "scale_twice") {
            $hasOutgoingTryFinallyForwardScaleTwice = $true
        }
    }
    Assert-True -Name "lsp_try_finally_forward_outgoing_call_hierarchy" -Condition ($hasOutgoingTryFinallyForwardAddBase -and $hasOutgoingTryFinallyForwardScaleTwice) -Details "Expected outgoing call hierarchy entries for both finally-forwarded targets. Actual: $(Format-JsonCompact $outgoingTryFinallyForwardCalls)"

    $runTryFinallyOverrideCallHierarchyItems = Send-LspRequest -Process $process -Method "textDocument/prepareCallHierarchy" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopTryFlowPath) }
        position = $runTryFinallyOverridePos
    })
    $runTryFinallyOverrideCallHierarchyItem = if (Has-LspResultItems $runTryFinallyOverrideCallHierarchyItems) { @($runTryFinallyOverrideCallHierarchyItems)[0] } else { $null }
    $outgoingTryFinallyOverrideCalls = if ($null -ne $runTryFinallyOverrideCallHierarchyItem) {
        Send-LspRequest -Process $process -Method "callHierarchy/outgoingCalls" -Params ([ordered]@{
            item = $runTryFinallyOverrideCallHierarchyItem
        })
    } else {
        $null
    }
    $hasOutgoingTryFinallyOverrideScaleTwice = $false
    $hasOutgoingTryFinallyOverrideAddBase = $false
    foreach ($call in @($outgoingTryFinallyOverrideCalls)) {
        if ($null -eq $call -or $null -eq $call.to) {
            continue
        }

        if ($call.to.name -eq "scale_twice") {
            $hasOutgoingTryFinallyOverrideScaleTwice = $true
        }

        if ($call.to.name -eq "add_base") {
            $hasOutgoingTryFinallyOverrideAddBase = $true
        }
    }
    Assert-True -Name "lsp_try_finally_override_outgoing_call_hierarchy" -Condition ($hasOutgoingTryFinallyOverrideScaleTwice -and (-not $hasOutgoingTryFinallyOverrideAddBase)) -Details "Expected outgoing call hierarchy to resolve only to scale_twice for finally override. Actual: $(Format-JsonCompact $outgoingTryFinallyOverrideCalls)"

    $arrayLiteralFirstSignatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $collectionFlowPath) }
        position = $arrayLiteralFirstCallPos
    })
    $arrayLiteralFirstSignatureLabel = if ($arrayLiteralFirstSignatureHelp.signatures.Count -gt 0) { [string]$arrayLiteralFirstSignatureHelp.signatures[0].label } else { "" }
    Assert-True -Name "lsp_array_literal_first_signature_help" -Condition ($arrayLiteralFirstSignatureLabel.Contains("v")) -Details "Expected array literal index 0 call to resolve to add_base(v). Actual: $(Format-JsonCompact $arrayLiteralFirstSignatureHelp)"

    $arrayLiteralSecondSignatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $collectionFlowPath) }
        position = $arrayLiteralSecondCallPos
    })
    $arrayLiteralSecondSignatureLabel = if ($arrayLiteralSecondSignatureHelp.signatures.Count -gt 0) { [string]$arrayLiteralSecondSignatureHelp.signatures[0].label } else { "" }
    Assert-True -Name "lsp_array_literal_second_signature_help" -Condition ($arrayLiteralSecondSignatureLabel.Contains("x")) -Details "Expected array literal index 1 call to resolve to scale_twice(x). Actual: $(Format-JsonCompact $arrayLiteralSecondSignatureHelp)"

    $arrayAssignmentFirstSignatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $collectionFlowPath) }
        position = $arrayAssignmentFirstCallPos
    })
    $arrayAssignmentFirstSignatureLabel = if ($arrayAssignmentFirstSignatureHelp.signatures.Count -gt 0) { [string]$arrayAssignmentFirstSignatureHelp.signatures[0].label } else { "" }
    Assert-True -Name "lsp_array_assignment_first_signature_help" -Condition ($arrayAssignmentFirstSignatureLabel.Contains("v")) -Details "Expected indexed assignment call to resolve array slot 0 to add_base(v). Actual: $(Format-JsonCompact $arrayAssignmentFirstSignatureHelp)"

    $arrayAssignmentSecondSignatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $collectionFlowPath) }
        position = $arrayAssignmentSecondCallPos
    })
    $arrayAssignmentSecondSignatureLabel = if ($arrayAssignmentSecondSignatureHelp.signatures.Count -gt 0) { [string]$arrayAssignmentSecondSignatureHelp.signatures[0].label } else { "" }
    Assert-True -Name "lsp_array_assignment_second_signature_help" -Condition ($arrayAssignmentSecondSignatureLabel.Contains("x")) -Details "Expected indexed assignment call to resolve array slot 1 to scale_twice(x). Actual: $(Format-JsonCompact $arrayAssignmentSecondSignatureHelp)"

    $returnedArrayFirstSignatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $collectionFlowPath) }
        position = $returnedArrayFirstCallPos
    })
    $returnedArrayFirstSignatureLabel = if ($returnedArrayFirstSignatureHelp.signatures.Count -gt 0) { [string]$returnedArrayFirstSignatureHelp.signatures[0].label } else { "" }
    Assert-True -Name "lsp_returned_array_first_signature_help" -Condition ($returnedArrayFirstSignatureLabel.Contains("v")) -Details "Expected returned array slot 0 call to resolve to add_base(v). Actual: $(Format-JsonCompact $returnedArrayFirstSignatureHelp)"

    $returnedArraySecondSignatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $collectionFlowPath) }
        position = $returnedArraySecondCallPos
    })
    $returnedArraySecondSignatureLabel = if ($returnedArraySecondSignatureHelp.signatures.Count -gt 0) { [string]$returnedArraySecondSignatureHelp.signatures[0].label } else { "" }
    Assert-True -Name "lsp_returned_array_second_signature_help" -Condition ($returnedArraySecondSignatureLabel.Contains("x")) -Details "Expected returned array slot 1 call to resolve to scale_twice(x). Actual: $(Format-JsonCompact $returnedArraySecondSignatureHelp)"

    $returnedDictLiteralSignatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $collectionFlowPath) }
        position = $returnedDictLiteralCallPos
    })
    $returnedDictLiteralSignatureLabels = @(Get-SignatureLabels -SignatureHelp $returnedDictLiteralSignatureHelp)
    $hasReturnedDictLiteralV = $false
    $hasReturnedDictLiteralX = $false
    foreach ($label in $returnedDictLiteralSignatureLabels) {
        if ($label.Contains("v")) {
            $hasReturnedDictLiteralV = $true
        }

        if ($label.Contains("x")) {
            $hasReturnedDictLiteralX = $true
        }
    }
    Assert-True -Name "lsp_returned_dict_literal_signature_help" -Condition ($hasReturnedDictLiteralV -and $hasReturnedDictLiteralX -and $returnedDictLiteralSignatureLabels.Count -ge 2) -Details "Expected ambiguous signature help for callable reached through returned dict literals. Actual: $(Format-JsonCompact $returnedDictLiteralSignatureHelp)"

    $collectionInlayHints = Send-LspRequest -Process $process -Method "textDocument/inlayHint" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $collectionFlowPath) }
        range = @{
            start = @{ line = 0; character = 0 }
            end = @{ line = 80; character = 0 }
        }
    })
    $collectionVHintCount = 0
    $collectionXHintCount = 0
    foreach ($hint in @($collectionInlayHints)) {
        if ($null -eq $hint) {
            continue
        }

        if ([string]$hint.label -eq "v:") {
            $collectionVHintCount++
        }

        if ([string]$hint.label -eq "x:") {
            $collectionXHintCount++
        }
    }
    Assert-True -Name "lsp_collection_inlay_hint_precision" -Condition ($collectionVHintCount -eq 3 -and $collectionXHintCount -eq 3) -Details "Expected exact inlay hints for array-index call sites and no ambiguous hints for returned dict literals. Actual: $(Format-JsonCompact $collectionInlayHints)"

    $runArrayLiteralCallHierarchyItems = Send-LspRequest -Process $process -Method "textDocument/prepareCallHierarchy" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $collectionFlowPath) }
        position = $runArrayLiteralPos
    })
    $runArrayLiteralCallHierarchyItem = if (Has-LspResultItems $runArrayLiteralCallHierarchyItems) { @($runArrayLiteralCallHierarchyItems)[0] } else { $null }
    $outgoingArrayLiteralCalls = if ($null -ne $runArrayLiteralCallHierarchyItem) {
        Send-LspRequest -Process $process -Method "callHierarchy/outgoingCalls" -Params ([ordered]@{
            item = $runArrayLiteralCallHierarchyItem
        })
    } else {
        $null
    }
    $hasOutgoingArrayLiteralAddBase = $false
    $hasOutgoingArrayLiteralScaleTwice = $false
    foreach ($call in @($outgoingArrayLiteralCalls)) {
        if ($null -eq $call -or $null -eq $call.to) {
            continue
        }

        if ($call.to.name -eq "add_base") {
            $hasOutgoingArrayLiteralAddBase = $true
        }

        if ($call.to.name -eq "scale_twice") {
            $hasOutgoingArrayLiteralScaleTwice = $true
        }
    }
    Assert-True -Name "lsp_array_literal_outgoing_call_hierarchy" -Condition ($hasOutgoingArrayLiteralAddBase -and $hasOutgoingArrayLiteralScaleTwice) -Details "Expected outgoing call hierarchy entries for array literal call targets. Actual: $(Format-JsonCompact $outgoingArrayLiteralCalls)"

    $runArrayAssignmentCallHierarchyItems = Send-LspRequest -Process $process -Method "textDocument/prepareCallHierarchy" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $collectionFlowPath) }
        position = $runArrayAssignmentPos
    })
    $runArrayAssignmentCallHierarchyItem = if (Has-LspResultItems $runArrayAssignmentCallHierarchyItems) { @($runArrayAssignmentCallHierarchyItems)[0] } else { $null }
    $outgoingArrayAssignmentCalls = if ($null -ne $runArrayAssignmentCallHierarchyItem) {
        Send-LspRequest -Process $process -Method "callHierarchy/outgoingCalls" -Params ([ordered]@{
            item = $runArrayAssignmentCallHierarchyItem
        })
    } else {
        $null
    }
    $hasOutgoingArrayAssignmentAddBase = $false
    $hasOutgoingArrayAssignmentScaleTwice = $false
    foreach ($call in @($outgoingArrayAssignmentCalls)) {
        if ($null -eq $call -or $null -eq $call.to) {
            continue
        }

        if ($call.to.name -eq "add_base") {
            $hasOutgoingArrayAssignmentAddBase = $true
        }

        if ($call.to.name -eq "scale_twice") {
            $hasOutgoingArrayAssignmentScaleTwice = $true
        }
    }
    Assert-True -Name "lsp_array_assignment_outgoing_call_hierarchy" -Condition ($hasOutgoingArrayAssignmentAddBase -and $hasOutgoingArrayAssignmentScaleTwice) -Details "Expected outgoing call hierarchy entries for indexed array assignment targets. Actual: $(Format-JsonCompact $outgoingArrayAssignmentCalls)"

    $runReturnedArrayCallHierarchyItems = Send-LspRequest -Process $process -Method "textDocument/prepareCallHierarchy" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $collectionFlowPath) }
        position = $runReturnedArrayPos
    })
    $runReturnedArrayCallHierarchyItem = if (Has-LspResultItems $runReturnedArrayCallHierarchyItems) { @($runReturnedArrayCallHierarchyItems)[0] } else { $null }
    $outgoingReturnedArrayCalls = if ($null -ne $runReturnedArrayCallHierarchyItem) {
        Send-LspRequest -Process $process -Method "callHierarchy/outgoingCalls" -Params ([ordered]@{
            item = $runReturnedArrayCallHierarchyItem
        })
    } else {
        $null
    }
    $hasOutgoingReturnedArrayAddBase = $false
    $hasOutgoingReturnedArrayScaleTwice = $false
    foreach ($call in @($outgoingReturnedArrayCalls)) {
        if ($null -eq $call -or $null -eq $call.to) {
            continue
        }

        if ($call.to.name -eq "add_base") {
            $hasOutgoingReturnedArrayAddBase = $true
        }

        if ($call.to.name -eq "scale_twice") {
            $hasOutgoingReturnedArrayScaleTwice = $true
        }
    }
    Assert-True -Name "lsp_returned_array_outgoing_call_hierarchy" -Condition ($hasOutgoingReturnedArrayAddBase -and $hasOutgoingReturnedArrayScaleTwice) -Details "Expected outgoing call hierarchy entries for returned array call targets. Actual: $(Format-JsonCompact $outgoingReturnedArrayCalls)"

    $runReturnedDictLiteralCallHierarchyItems = Send-LspRequest -Process $process -Method "textDocument/prepareCallHierarchy" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $collectionFlowPath) }
        position = $runReturnedDictLiteralPos
    })
    $runReturnedDictLiteralCallHierarchyItem = if (Has-LspResultItems $runReturnedDictLiteralCallHierarchyItems) { @($runReturnedDictLiteralCallHierarchyItems)[0] } else { $null }
    $outgoingReturnedDictLiteralCalls = if ($null -ne $runReturnedDictLiteralCallHierarchyItem) {
        Send-LspRequest -Process $process -Method "callHierarchy/outgoingCalls" -Params ([ordered]@{
            item = $runReturnedDictLiteralCallHierarchyItem
        })
    } else {
        $null
    }
    $hasOutgoingReturnedDictLiteralAddBase = $false
    $hasOutgoingReturnedDictLiteralScaleTwice = $false
    foreach ($call in @($outgoingReturnedDictLiteralCalls)) {
        if ($null -eq $call -or $null -eq $call.to) {
            continue
        }

        if ($call.to.name -eq "add_base") {
            $hasOutgoingReturnedDictLiteralAddBase = $true
        }

        if ($call.to.name -eq "scale_twice") {
            $hasOutgoingReturnedDictLiteralScaleTwice = $true
        }
    }
    Assert-True -Name "lsp_returned_dict_literal_outgoing_call_hierarchy" -Condition ($hasOutgoingReturnedDictLiteralAddBase -and $hasOutgoingReturnedDictLiteralScaleTwice) -Details "Expected outgoing call hierarchy entries for returned dict literal targets. Actual: $(Format-JsonCompact $outgoingReturnedDictLiteralCalls)"

    $whileFixpointSignatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopFixpointFlowPath) }
        position = $whileFixpointCallPos
    })
    $whileFixpointSignatureLabels = @(Get-SignatureLabels -SignatureHelp $whileFixpointSignatureHelp)
    Assert-True -Name "lsp_while_fixpoint_signature_help" -Condition ($whileFixpointSignatureLabels.Count -ge 2 -and ($whileFixpointSignatureLabels -match "v").Count -ge 1 -and ($whileFixpointSignatureLabels -match "x").Count -ge 1) -Details "Expected fixpoint signature help for while-loop flow. Actual: $(Format-JsonCompact $whileFixpointSignatureHelp)"

    $doWhileFixpointSignatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopFixpointFlowPath) }
        position = $doWhileFixpointCallPos
    })
    $doWhileFixpointSignatureLabels = @(Get-SignatureLabels -SignatureHelp $doWhileFixpointSignatureHelp)
    Assert-True -Name "lsp_do_while_fixpoint_signature_help" -Condition ($doWhileFixpointSignatureLabels.Count -ge 2 -and ($doWhileFixpointSignatureLabels -match "v").Count -ge 1 -and ($doWhileFixpointSignatureLabels -match "x").Count -ge 1) -Details "Expected fixpoint signature help for do-while flow. Actual: $(Format-JsonCompact $doWhileFixpointSignatureHelp)"

    $forFixpointSignatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopFixpointFlowPath) }
        position = $forFixpointCallPos
    })
    $forFixpointSignatureLabels = @(Get-SignatureLabels -SignatureHelp $forFixpointSignatureHelp)
    Assert-True -Name "lsp_for_fixpoint_signature_help" -Condition ($forFixpointSignatureLabels.Count -ge 2 -and ($forFixpointSignatureLabels -match "v").Count -ge 1 -and ($forFixpointSignatureLabels -match "x").Count -ge 1) -Details "Expected fixpoint signature help for for-loop flow. Actual: $(Format-JsonCompact $forFixpointSignatureHelp)"

    $foreachFixpointSignatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopFixpointFlowPath) }
        position = $foreachFixpointCallPos
    })
    $foreachFixpointSignatureLabels = @(Get-SignatureLabels -SignatureHelp $foreachFixpointSignatureHelp)
    Assert-True -Name "lsp_foreach_fixpoint_signature_help" -Condition ($foreachFixpointSignatureLabels.Count -ge 2 -and ($foreachFixpointSignatureLabels -match "v").Count -ge 1 -and ($foreachFixpointSignatureLabels -match "x").Count -ge 1) -Details "Expected fixpoint signature help for foreach flow. Actual: $(Format-JsonCompact $foreachFixpointSignatureHelp)"

    $loopFixpointInlayHints = Send-LspRequest -Process $process -Method "textDocument/inlayHint" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopFixpointFlowPath) }
        range = @{
            start = @{ line = 0; character = 0 }
            end = @{ line = 120; character = 0 }
        }
    })
    $loopFixpointVHintCount = 0
    $loopFixpointXHintCount = 0
    foreach ($hint in @($loopFixpointInlayHints)) {
        if ($null -eq $hint) {
            continue
        }

        if ([string]$hint.label -eq "v:") {
            $loopFixpointVHintCount++
        }

        if ([string]$hint.label -eq "x:") {
            $loopFixpointXHintCount++
        }
    }
    Assert-True -Name "lsp_loop_fixpoint_inlay_hint_suppressed" -Condition ($loopFixpointVHintCount -eq 0 -and $loopFixpointXHintCount -eq 0) -Details "Expected no parameter inlay hints for ambiguous multi-iteration loop fixpoint calls. Actual: $(Format-JsonCompact $loopFixpointInlayHints)"

    $runWhileFixpointCallHierarchyItems = Send-LspRequest -Process $process -Method "textDocument/prepareCallHierarchy" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopFixpointFlowPath) }
        position = $runWhileFixpointPos
    })
    $runWhileFixpointCallHierarchyItem = if (Has-LspResultItems $runWhileFixpointCallHierarchyItems) { @($runWhileFixpointCallHierarchyItems)[0] } else { $null }
    $outgoingWhileFixpointCalls = if ($null -ne $runWhileFixpointCallHierarchyItem) {
        Send-LspRequest -Process $process -Method "callHierarchy/outgoingCalls" -Params ([ordered]@{
            item = $runWhileFixpointCallHierarchyItem
        })
    } else {
        $null
    }
    $hasOutgoingWhileFixpointAddBase = $false
    $hasOutgoingWhileFixpointScaleTwice = $false
    foreach ($call in @($outgoingWhileFixpointCalls)) {
        if ($null -eq $call -or $null -eq $call.to) {
            continue
        }

        if ($call.to.name -eq "add_base") {
            $hasOutgoingWhileFixpointAddBase = $true
        }

        if ($call.to.name -eq "scale_twice") {
            $hasOutgoingWhileFixpointScaleTwice = $true
        }
    }
    Assert-True -Name "lsp_while_fixpoint_outgoing_call_hierarchy" -Condition ($hasOutgoingWhileFixpointAddBase -and $hasOutgoingWhileFixpointScaleTwice) -Details "Expected outgoing call hierarchy entries for multi-iteration while-loop targets. Actual: $(Format-JsonCompact $outgoingWhileFixpointCalls)"

    $runDoWhileFixpointCallHierarchyItems = Send-LspRequest -Process $process -Method "textDocument/prepareCallHierarchy" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopFixpointFlowPath) }
        position = $runDoWhileFixpointPos
    })
    $runDoWhileFixpointCallHierarchyItem = if (Has-LspResultItems $runDoWhileFixpointCallHierarchyItems) { @($runDoWhileFixpointCallHierarchyItems)[0] } else { $null }
    $outgoingDoWhileFixpointCalls = if ($null -ne $runDoWhileFixpointCallHierarchyItem) {
        Send-LspRequest -Process $process -Method "callHierarchy/outgoingCalls" -Params ([ordered]@{
            item = $runDoWhileFixpointCallHierarchyItem
        })
    } else {
        $null
    }
    $hasOutgoingDoWhileFixpointAddBase = $false
    $hasOutgoingDoWhileFixpointScaleTwice = $false
    foreach ($call in @($outgoingDoWhileFixpointCalls)) {
        if ($null -eq $call -or $null -eq $call.to) {
            continue
        }

        if ($call.to.name -eq "add_base") {
            $hasOutgoingDoWhileFixpointAddBase = $true
        }

        if ($call.to.name -eq "scale_twice") {
            $hasOutgoingDoWhileFixpointScaleTwice = $true
        }
    }
    Assert-True -Name "lsp_do_while_fixpoint_outgoing_call_hierarchy" -Condition ($hasOutgoingDoWhileFixpointAddBase -and $hasOutgoingDoWhileFixpointScaleTwice) -Details "Expected outgoing call hierarchy entries for multi-iteration do-while targets. Actual: $(Format-JsonCompact $outgoingDoWhileFixpointCalls)"

    $runForFixpointCallHierarchyItems = Send-LspRequest -Process $process -Method "textDocument/prepareCallHierarchy" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopFixpointFlowPath) }
        position = $runForFixpointPos
    })
    $runForFixpointCallHierarchyItem = if (Has-LspResultItems $runForFixpointCallHierarchyItems) { @($runForFixpointCallHierarchyItems)[0] } else { $null }
    $outgoingForFixpointCalls = if ($null -ne $runForFixpointCallHierarchyItem) {
        Send-LspRequest -Process $process -Method "callHierarchy/outgoingCalls" -Params ([ordered]@{
            item = $runForFixpointCallHierarchyItem
        })
    } else {
        $null
    }
    $hasOutgoingForFixpointAddBase = $false
    $hasOutgoingForFixpointScaleTwice = $false
    foreach ($call in @($outgoingForFixpointCalls)) {
        if ($null -eq $call -or $null -eq $call.to) {
            continue
        }

        if ($call.to.name -eq "add_base") {
            $hasOutgoingForFixpointAddBase = $true
        }

        if ($call.to.name -eq "scale_twice") {
            $hasOutgoingForFixpointScaleTwice = $true
        }
    }
    Assert-True -Name "lsp_for_fixpoint_outgoing_call_hierarchy" -Condition ($hasOutgoingForFixpointAddBase -and $hasOutgoingForFixpointScaleTwice) -Details "Expected outgoing call hierarchy entries for multi-iteration for-loop targets. Actual: $(Format-JsonCompact $outgoingForFixpointCalls)"

    $runForeachFixpointCallHierarchyItems = Send-LspRequest -Process $process -Method "textDocument/prepareCallHierarchy" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopFixpointFlowPath) }
        position = $runForeachFixpointPos
    })
    $runForeachFixpointCallHierarchyItem = if (Has-LspResultItems $runForeachFixpointCallHierarchyItems) { @($runForeachFixpointCallHierarchyItems)[0] } else { $null }
    $outgoingForeachFixpointCalls = if ($null -ne $runForeachFixpointCallHierarchyItem) {
        Send-LspRequest -Process $process -Method "callHierarchy/outgoingCalls" -Params ([ordered]@{
            item = $runForeachFixpointCallHierarchyItem
        })
    } else {
        $null
    }
    $hasOutgoingForeachFixpointAddBase = $false
    $hasOutgoingForeachFixpointScaleTwice = $false
    foreach ($call in @($outgoingForeachFixpointCalls)) {
        if ($null -eq $call -or $null -eq $call.to) {
            continue
        }

        if ($call.to.name -eq "add_base") {
            $hasOutgoingForeachFixpointAddBase = $true
        }

        if ($call.to.name -eq "scale_twice") {
            $hasOutgoingForeachFixpointScaleTwice = $true
        }
    }
    Assert-True -Name "lsp_foreach_fixpoint_outgoing_call_hierarchy" -Condition ($hasOutgoingForeachFixpointAddBase -and $hasOutgoingForeachFixpointScaleTwice) -Details "Expected outgoing call hierarchy entries for multi-iteration foreach targets. Actual: $(Format-JsonCompact $outgoingForeachFixpointCalls)"

    $breakStopsTailSignatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopControlFlowPath) }
        position = $breakStopsTailCallPos
    })
    $breakStopsTailSignatureLabels = @(Get-SignatureLabels -SignatureHelp $breakStopsTailSignatureHelp)
    Assert-True -Name "lsp_break_stops_tail_signature_help" -Condition ($breakStopsTailSignatureLabels.Count -ge 2 -and ($breakStopsTailSignatureLabels -match "v").Count -ge 1 -and ($breakStopsTailSignatureLabels -match "x").Count -ge 1) -Details "Expected break to preserve pre-break callable and skip tail assignment. Actual: $(Format-JsonCompact $breakStopsTailSignatureHelp)"

    $nestedBreakSignatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopControlFlowPath) }
        position = $nestedBreakCallPos
    })
    $nestedBreakSignatureLabels = @(Get-SignatureLabels -SignatureHelp $nestedBreakSignatureHelp)
    Assert-True -Name "lsp_nested_break_signature_help" -Condition ($nestedBreakSignatureLabels.Count -ge 2 -and ($nestedBreakSignatureLabels -match "v").Count -ge 1 -and ($nestedBreakSignatureLabels -match "x").Count -ge 1) -Details "Expected nested break inside if-block to propagate out of loop body. Actual: $(Format-JsonCompact $nestedBreakSignatureHelp)"

    $doWhileContinueExactSignatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopControlFlowPath) }
        position = $doWhileContinueExactCallPos
    })
    $doWhileContinueExactSignatureLabels = @(Get-SignatureLabels -SignatureHelp $doWhileContinueExactSignatureHelp)
    Assert-True -Name "lsp_do_while_continue_exact_signature_help" -Condition ($doWhileContinueExactSignatureLabels.Count -eq 1 -and $doWhileContinueExactSignatureLabels[0].Contains("x")) -Details "Expected continue to skip trailing assignment in do-while body. Actual: $(Format-JsonCompact $doWhileContinueExactSignatureHelp)"

    $doWhileReturnSignatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopControlFlowPath) }
        position = $doWhileReturnCallPos
    })
    $doWhileReturnSignatureLabels = @(Get-SignatureLabels -SignatureHelp $doWhileReturnSignatureHelp)
    Assert-True -Name "lsp_do_while_return_exact_signature_help" -Condition ($doWhileReturnSignatureLabels.Count -eq 1 -and $doWhileReturnSignatureLabels[0].Contains("x")) -Details "Expected early return in do-while body to suppress later returns. Actual: $(Format-JsonCompact $doWhileReturnSignatureHelp)"

    $tryBreakFinallySignatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopControlFlowPath) }
        position = $tryBreakFinallyCallPos
    })
    $tryBreakFinallySignatureLabels = @(Get-SignatureLabels -SignatureHelp $tryBreakFinallySignatureHelp)
    Assert-True -Name "lsp_try_break_finally_signature_help" -Condition ($tryBreakFinallySignatureLabels.Count -ge 2 -and ($tryBreakFinallySignatureLabels -match "v").Count -ge 1 -and ($tryBreakFinallySignatureLabels -match "x").Count -ge 1) -Details "Expected break inside try/finally to preserve pre-loop callable and forward the break path through finally. Actual: $(Format-JsonCompact $tryBreakFinallySignatureHelp)"

    $tryContinueFinallySignatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopControlFlowPath) }
        position = $tryContinueFinallyCallPos
    })
    $tryContinueFinallySignatureLabels = @(Get-SignatureLabels -SignatureHelp $tryContinueFinallySignatureHelp)
    Assert-True -Name "lsp_try_continue_finally_signature_help" -Condition ($tryContinueFinallySignatureLabels.Count -eq 1 -and $tryContinueFinallySignatureLabels[0].Contains("x")) -Details "Expected continue inside try/finally to skip trailing assignments and forward exact callable state. Actual: $(Format-JsonCompact $tryContinueFinallySignatureHelp)"

    $tryFinallyReturnSignatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopControlFlowPath) }
        position = $tryFinallyReturnCallPos
    })
    $tryFinallyReturnSignatureLabels = @(Get-SignatureLabels -SignatureHelp $tryFinallyReturnSignatureHelp)
    Assert-True -Name "lsp_try_finally_return_override_signature_help" -Condition ($tryFinallyReturnSignatureLabels.Count -eq 1 -and $tryFinallyReturnSignatureLabels[0].Contains("x")) -Details "Expected finally-return override to suppress the earlier try-return callable. Actual: $(Format-JsonCompact $tryFinallyReturnSignatureHelp)"

    $loopControlInlayHints = Send-LspRequest -Process $process -Method "textDocument/inlayHint" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopControlFlowPath) }
        range = @{
            start = @{ line = 0; character = 0 }
            end = @{ line = 180; character = 0 }
        }
    })
    $loopControlVHintCount = 0
    $loopControlXHintCount = 0
    foreach ($hint in @($loopControlInlayHints)) {
        if ($null -eq $hint) {
            continue
        }

        if ([string]$hint.label -eq "v:") {
            $loopControlVHintCount++
        }

        if ([string]$hint.label -eq "x:") {
            $loopControlXHintCount++
        }
    }
    Assert-True -Name "lsp_loop_control_inlay_hint_precision" -Condition ($loopControlVHintCount -eq 0 -and $loopControlXHintCount -eq 4) -Details "Expected inlay hints only for exact continue/return/finally-controlled call sites. Actual: $(Format-JsonCompact $loopControlInlayHints)"

    $runBreakStopsTailCallHierarchyItems = Send-LspRequest -Process $process -Method "textDocument/prepareCallHierarchy" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopControlFlowPath) }
        position = $runBreakStopsTailPos
    })
    $runBreakStopsTailCallHierarchyItem = if (Has-LspResultItems $runBreakStopsTailCallHierarchyItems) { @($runBreakStopsTailCallHierarchyItems)[0] } else { $null }
    $outgoingBreakStopsTailCalls = if ($null -ne $runBreakStopsTailCallHierarchyItem) {
        Send-LspRequest -Process $process -Method "callHierarchy/outgoingCalls" -Params ([ordered]@{
            item = $runBreakStopsTailCallHierarchyItem
        })
    } else {
        $null
    }
    $hasOutgoingBreakStopsTailAddBase = $false
    $hasOutgoingBreakStopsTailScaleTwice = $false
    foreach ($call in @($outgoingBreakStopsTailCalls)) {
        if ($null -eq $call -or $null -eq $call.to) {
            continue
        }

        if ($call.to.name -eq "add_base") {
            $hasOutgoingBreakStopsTailAddBase = $true
        }

        if ($call.to.name -eq "scale_twice") {
            $hasOutgoingBreakStopsTailScaleTwice = $true
        }
    }
    Assert-True -Name "lsp_break_stops_tail_outgoing_call_hierarchy" -Condition ($hasOutgoingBreakStopsTailAddBase -and $hasOutgoingBreakStopsTailScaleTwice) -Details "Expected outgoing call hierarchy entries for break-controlled loop exit. Actual: $(Format-JsonCompact $outgoingBreakStopsTailCalls)"

    $runNestedBreakCallHierarchyItems = Send-LspRequest -Process $process -Method "textDocument/prepareCallHierarchy" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopControlFlowPath) }
        position = $runNestedBreakPos
    })
    $runNestedBreakCallHierarchyItem = if (Has-LspResultItems $runNestedBreakCallHierarchyItems) { @($runNestedBreakCallHierarchyItems)[0] } else { $null }
    $outgoingNestedBreakCalls = if ($null -ne $runNestedBreakCallHierarchyItem) {
        Send-LspRequest -Process $process -Method "callHierarchy/outgoingCalls" -Params ([ordered]@{
            item = $runNestedBreakCallHierarchyItem
        })
    } else {
        $null
    }
    $hasOutgoingNestedBreakAddBase = $false
    $hasOutgoingNestedBreakScaleTwice = $false
    foreach ($call in @($outgoingNestedBreakCalls)) {
        if ($null -eq $call -or $null -eq $call.to) {
            continue
        }

        if ($call.to.name -eq "add_base") {
            $hasOutgoingNestedBreakAddBase = $true
        }

        if ($call.to.name -eq "scale_twice") {
            $hasOutgoingNestedBreakScaleTwice = $true
        }
    }
    Assert-True -Name "lsp_nested_break_outgoing_call_hierarchy" -Condition ($hasOutgoingNestedBreakAddBase -and $hasOutgoingNestedBreakScaleTwice) -Details "Expected outgoing call hierarchy entries for nested break propagation. Actual: $(Format-JsonCompact $outgoingNestedBreakCalls)"

    $runDoWhileContinueExactCallHierarchyItems = Send-LspRequest -Process $process -Method "textDocument/prepareCallHierarchy" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopControlFlowPath) }
        position = $runDoWhileContinueExactPos
    })
    $runDoWhileContinueExactCallHierarchyItem = if (Has-LspResultItems $runDoWhileContinueExactCallHierarchyItems) { @($runDoWhileContinueExactCallHierarchyItems)[0] } else { $null }
    $outgoingDoWhileContinueExactCalls = if ($null -ne $runDoWhileContinueExactCallHierarchyItem) {
        Send-LspRequest -Process $process -Method "callHierarchy/outgoingCalls" -Params ([ordered]@{
            item = $runDoWhileContinueExactCallHierarchyItem
        })
    } else {
        $null
    }
    $hasOutgoingDoWhileContinueExactScaleTwice = $false
    $hasOutgoingDoWhileContinueExactAddBase = $false
    foreach ($call in @($outgoingDoWhileContinueExactCalls)) {
        if ($null -eq $call -or $null -eq $call.to) {
            continue
        }

        if ($call.to.name -eq "scale_twice") {
            $hasOutgoingDoWhileContinueExactScaleTwice = $true
        }

        if ($call.to.name -eq "add_base") {
            $hasOutgoingDoWhileContinueExactAddBase = $true
        }
    }
    Assert-True -Name "lsp_do_while_continue_exact_outgoing_call_hierarchy" -Condition ($hasOutgoingDoWhileContinueExactScaleTwice -and (-not $hasOutgoingDoWhileContinueExactAddBase)) -Details "Expected outgoing call hierarchy to resolve only to scale_twice for continue-controlled do-while flow. Actual: $(Format-JsonCompact $outgoingDoWhileContinueExactCalls)"

    $runDoWhileReturnCallHierarchyItems = Send-LspRequest -Process $process -Method "textDocument/prepareCallHierarchy" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopControlFlowPath) }
        position = $runDoWhileReturnPos
    })
    $runDoWhileReturnCallHierarchyItem = if (Has-LspResultItems $runDoWhileReturnCallHierarchyItems) { @($runDoWhileReturnCallHierarchyItems)[0] } else { $null }
    $outgoingDoWhileReturnCalls = if ($null -ne $runDoWhileReturnCallHierarchyItem) {
        Send-LspRequest -Process $process -Method "callHierarchy/outgoingCalls" -Params ([ordered]@{
            item = $runDoWhileReturnCallHierarchyItem
        })
    } else {
        $null
    }
    $hasOutgoingDoWhileReturnScaleTwice = $false
    $hasOutgoingDoWhileReturnAddBase = $false
    foreach ($call in @($outgoingDoWhileReturnCalls)) {
        if ($null -eq $call -or $null -eq $call.to) {
            continue
        }

        if ($call.to.name -eq "scale_twice") {
            $hasOutgoingDoWhileReturnScaleTwice = $true
        }

        if ($call.to.name -eq "add_base") {
            $hasOutgoingDoWhileReturnAddBase = $true
        }
    }
    Assert-True -Name "lsp_do_while_return_exact_outgoing_call_hierarchy" -Condition ($hasOutgoingDoWhileReturnScaleTwice -and (-not $hasOutgoingDoWhileReturnAddBase)) -Details "Expected outgoing call hierarchy to resolve only to scale_twice for early-returned callable. Actual: $(Format-JsonCompact $outgoingDoWhileReturnCalls)"

    $runTryBreakFinallyCallHierarchyItems = Send-LspRequest -Process $process -Method "textDocument/prepareCallHierarchy" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopControlFlowPath) }
        position = $runTryBreakFinallyPos
    })
    $runTryBreakFinallyCallHierarchyItem = if (Has-LspResultItems $runTryBreakFinallyCallHierarchyItems) { @($runTryBreakFinallyCallHierarchyItems)[0] } else { $null }
    $outgoingTryBreakFinallyCalls = if ($null -ne $runTryBreakFinallyCallHierarchyItem) {
        Send-LspRequest -Process $process -Method "callHierarchy/outgoingCalls" -Params ([ordered]@{
            item = $runTryBreakFinallyCallHierarchyItem
        })
    } else {
        $null
    }
    $hasOutgoingTryBreakFinallyAddBase = $false
    $hasOutgoingTryBreakFinallyScaleTwice = $false
    foreach ($call in @($outgoingTryBreakFinallyCalls)) {
        if ($null -eq $call -or $null -eq $call.to) {
            continue
        }

        if ($call.to.name -eq "add_base") {
            $hasOutgoingTryBreakFinallyAddBase = $true
        }

        if ($call.to.name -eq "scale_twice") {
            $hasOutgoingTryBreakFinallyScaleTwice = $true
        }
    }
    Assert-True -Name "lsp_try_break_finally_outgoing_call_hierarchy" -Condition ($hasOutgoingTryBreakFinallyAddBase -and $hasOutgoingTryBreakFinallyScaleTwice) -Details "Expected outgoing call hierarchy entries for break forwarded through finally. Actual: $(Format-JsonCompact $outgoingTryBreakFinallyCalls)"

    $runTryContinueFinallyCallHierarchyItems = Send-LspRequest -Process $process -Method "textDocument/prepareCallHierarchy" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopControlFlowPath) }
        position = $runTryContinueFinallyPos
    })
    $runTryContinueFinallyCallHierarchyItem = if (Has-LspResultItems $runTryContinueFinallyCallHierarchyItems) { @($runTryContinueFinallyCallHierarchyItems)[0] } else { $null }
    $outgoingTryContinueFinallyCalls = if ($null -ne $runTryContinueFinallyCallHierarchyItem) {
        Send-LspRequest -Process $process -Method "callHierarchy/outgoingCalls" -Params ([ordered]@{
            item = $runTryContinueFinallyCallHierarchyItem
        })
    } else {
        $null
    }
    $hasOutgoingTryContinueFinallyScaleTwice = $false
    $hasOutgoingTryContinueFinallyAddBase = $false
    foreach ($call in @($outgoingTryContinueFinallyCalls)) {
        if ($null -eq $call -or $null -eq $call.to) {
            continue
        }

        if ($call.to.name -eq "scale_twice") {
            $hasOutgoingTryContinueFinallyScaleTwice = $true
        }

        if ($call.to.name -eq "add_base") {
            $hasOutgoingTryContinueFinallyAddBase = $true
        }
    }
    Assert-True -Name "lsp_try_continue_finally_outgoing_call_hierarchy" -Condition ($hasOutgoingTryContinueFinallyScaleTwice -and (-not $hasOutgoingTryContinueFinallyAddBase)) -Details "Expected outgoing call hierarchy to resolve only to scale_twice for continue forwarded through finally. Actual: $(Format-JsonCompact $outgoingTryContinueFinallyCalls)"

    $runTryFinallyReturnCallHierarchyItems = Send-LspRequest -Process $process -Method "textDocument/prepareCallHierarchy" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $loopControlFlowPath) }
        position = $runTryFinallyReturnPos
    })
    $runTryFinallyReturnCallHierarchyItem = if (Has-LspResultItems $runTryFinallyReturnCallHierarchyItems) { @($runTryFinallyReturnCallHierarchyItems)[0] } else { $null }
    $outgoingTryFinallyReturnCalls = if ($null -ne $runTryFinallyReturnCallHierarchyItem) {
        Send-LspRequest -Process $process -Method "callHierarchy/outgoingCalls" -Params ([ordered]@{
            item = $runTryFinallyReturnCallHierarchyItem
        })
    } else {
        $null
    }
    $hasOutgoingTryFinallyReturnScaleTwice = $false
    $hasOutgoingTryFinallyReturnAddBase = $false
    foreach ($call in @($outgoingTryFinallyReturnCalls)) {
        if ($null -eq $call -or $null -eq $call.to) {
            continue
        }

        if ($call.to.name -eq "scale_twice") {
            $hasOutgoingTryFinallyReturnScaleTwice = $true
        }

        if ($call.to.name -eq "add_base") {
            $hasOutgoingTryFinallyReturnAddBase = $true
        }
    }
    Assert-True -Name "lsp_try_finally_return_override_outgoing_call_hierarchy" -Condition ($hasOutgoingTryFinallyReturnScaleTwice -and (-not $hasOutgoingTryFinallyReturnAddBase)) -Details "Expected outgoing call hierarchy to resolve only to scale_twice for finally-return override. Actual: $(Format-JsonCompact $outgoingTryFinallyReturnCalls)"

    $firstMemberSignatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $containerAliasPath) }
        position = $firstMemberCallPos
    })
    $firstMemberSignatureLabel = if ($firstMemberSignatureHelp.signatures.Count -gt 0) { [string]$firstMemberSignatureHelp.signatures[0].label } else { "" }
    Assert-True -Name "lsp_member_instance_signature_help_first" -Condition ($firstMemberSignatureLabel.Contains("v")) -Details "Expected first instance member call to resolve to add_base(v). Actual: $(Format-JsonCompact $firstMemberSignatureHelp)"

    $secondMemberSignatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $containerAliasPath) }
        position = $secondMemberCallPos
    })
    $secondMemberSignatureLabel = if ($secondMemberSignatureHelp.signatures.Count -gt 0) { [string]$secondMemberSignatureHelp.signatures[0].label } else { "" }
    Assert-True -Name "lsp_member_instance_signature_help_second" -Condition ($secondMemberSignatureLabel.Contains("x")) -Details "Expected second instance member call to resolve to scale_twice(x). Actual: $(Format-JsonCompact $secondMemberSignatureHelp)"

    $dynamicForwardedSignatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $containerAliasPath) }
        position = $dynamicForwardedCallPos
    })
    $dynamicForwardedSignatureLabel = if ($dynamicForwardedSignatureHelp.signatures.Count -gt 0) { [string]$dynamicForwardedSignatureHelp.signatures[0].label } else { "" }
    Assert-True -Name "lsp_dynamic_container_signature_help" -Condition ($dynamicForwardedSignatureLabel.Contains("v")) -Details "Expected dynamic container member call to resolve to add_base(v). Actual: $(Format-JsonCompact $dynamicForwardedSignatureHelp)"

    $nestedContainerSignatureHelp = Send-LspRequest -Process $process -Method "textDocument/signatureHelp" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $containerAliasPath) }
        position = $nestedContainerCallPos
    })
    $nestedContainerSignatureLabel = if ($nestedContainerSignatureHelp.signatures.Count -gt 0) { [string]$nestedContainerSignatureHelp.signatures[0].label } else { "" }
    Assert-True -Name "lsp_nested_container_signature_help" -Condition ($nestedContainerSignatureLabel.Contains("v")) -Details "Expected nested container member call to resolve through forwarded container alias. Actual: $(Format-JsonCompact $nestedContainerSignatureHelp)"

    $containerInlayHints = Send-LspRequest -Process $process -Method "textDocument/inlayHint" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $containerAliasPath) }
        range = @{
            start = @{ line = 0; character = 0 }
            end = @{ line = 40; character = 0 }
        }
    })
    $hasVHint = $false
    $hasXHint = $false
    foreach ($hint in @($containerInlayHints)) {
        if ($null -eq $hint) {
            continue
        }

        if ([string]$hint.label -eq "v:") {
            $hasVHint = $true
        }

        if ([string]$hint.label -eq "x:") {
            $hasXHint = $true
        }
    }
    Assert-True -Name "lsp_container_alias_inlay_hints" -Condition ($hasVHint -and $hasXHint) -Details "Expected distinct inlay hints for instance-specific and dynamic container calls. Actual: $(Format-JsonCompact $containerInlayHints)"

    $scaleIncomingAfterContainer = Send-LspRequest -Process $process -Method "callHierarchy/incomingCalls" -Params ([ordered]@{
        item = $scaleCallHierarchyItem
    })
    $memberBoxesIncoming = Find-CallHierarchyItem -Calls $scaleIncomingAfterContainer -FromName "run_member_boxes"
    $memberBoxesIncomingCount = if ($null -ne $memberBoxesIncoming) { @($memberBoxesIncoming.fromRanges).Count } else { 0 }
    Assert-True -Name "lsp_member_instance_incoming_call_hierarchy" -Condition ($memberBoxesIncomingCount -ge 1) -Details "Expected instance-specific member call to resolve to scale_twice. Actual: $(Format-JsonCompact $scaleIncomingAfterContainer)"

    $runMemberBoxesCallHierarchyItems = Send-LspRequest -Process $process -Method "textDocument/prepareCallHierarchy" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $containerAliasPath) }
        position = $runMemberBoxesPos
    })
    $runMemberBoxesCallHierarchyItem = if (Has-LspResultItems $runMemberBoxesCallHierarchyItems) { @($runMemberBoxesCallHierarchyItems)[0] } else { $null }
    $outgoingMemberCalls = if ($null -ne $runMemberBoxesCallHierarchyItem) {
        Send-LspRequest -Process $process -Method "callHierarchy/outgoingCalls" -Params ([ordered]@{
            item = $runMemberBoxesCallHierarchyItem
        })
    } else {
        $null
    }
    $hasOutgoingMemberAddBase = $false
    $hasOutgoingMemberScaleTwice = $false
    foreach ($call in @($outgoingMemberCalls)) {
        if ($null -eq $call -or $null -eq $call.to) {
            continue
        }

        if ($call.to.name -eq "add_base") {
            $hasOutgoingMemberAddBase = $true
        }

        if ($call.to.name -eq "scale_twice") {
            $hasOutgoingMemberScaleTwice = $true
        }
    }
    Assert-True -Name "lsp_member_instance_outgoing_call_hierarchy" -Condition ($hasOutgoingMemberAddBase -and $hasOutgoingMemberScaleTwice) -Details "Expected outgoing call hierarchy entries for both instance-specific member call targets. Actual: $(Format-JsonCompact $outgoingMemberCalls)"

    $incomingCallsAfterContainer = Send-LspRequest -Process $process -Method "callHierarchy/incomingCalls" -Params ([ordered]@{
        item = $callHierarchyItem
    })
    $dynamicBoxIncoming = Find-CallHierarchyItem -Calls $incomingCallsAfterContainer -FromName "run_dynamic_box"
    $nestedContainerIncoming = Find-CallHierarchyItem -Calls $incomingCallsAfterContainer -FromName "run_nested_container"
    $dynamicBoxIncomingCount = if ($null -ne $dynamicBoxIncoming) { @($dynamicBoxIncoming.fromRanges).Count } else { 0 }
    $nestedContainerIncomingCount = if ($null -ne $nestedContainerIncoming) { @($nestedContainerIncoming.fromRanges).Count } else { 0 }
    Assert-True -Name "lsp_dynamic_container_incoming_call_hierarchy" -Condition ($dynamicBoxIncomingCount -ge 2) -Details "Expected incoming call hierarchy for dynamic container member alias and forwarded local alias. Actual: $(Format-JsonCompact $incomingCallsAfterContainer)"
    Assert-True -Name "lsp_nested_container_incoming_call_hierarchy" -Condition ($nestedContainerIncomingCount -ge 1) -Details "Expected incoming call hierarchy for nested container alias call. Actual: $(Format-JsonCompact $incomingCallsAfterContainer)"

    $runDynamicBoxCallHierarchyItems = Send-LspRequest -Process $process -Method "textDocument/prepareCallHierarchy" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $containerAliasPath) }
        position = $runDynamicBoxPos
    })
    $runDynamicBoxCallHierarchyItem = if (Has-LspResultItems $runDynamicBoxCallHierarchyItems) { @($runDynamicBoxCallHierarchyItems)[0] } else { $null }
    $outgoingDynamicCalls = if ($null -ne $runDynamicBoxCallHierarchyItem) {
        Send-LspRequest -Process $process -Method "callHierarchy/outgoingCalls" -Params ([ordered]@{
            item = $runDynamicBoxCallHierarchyItem
        })
    } else {
        $null
    }
    $hasOutgoingDynamicAddBase = $false
    foreach ($call in @($outgoingDynamicCalls)) {
        if ($null -ne $call -and $null -ne $call.to -and $call.to.name -eq "add_base") {
            $hasOutgoingDynamicAddBase = $true
            break
        }
    }
    Assert-True -Name "lsp_dynamic_container_outgoing_call_hierarchy" -Condition $hasOutgoingDynamicAddBase -Details "Expected outgoing call hierarchy entry for dynamic container member alias. Actual: $(Format-JsonCompact $outgoingDynamicCalls)"

    $runNestedContainerCallHierarchyItems = Send-LspRequest -Process $process -Method "textDocument/prepareCallHierarchy" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $containerAliasPath) }
        position = $runNestedContainerPos
    })
    $runNestedContainerCallHierarchyItem = if (Has-LspResultItems $runNestedContainerCallHierarchyItems) { @($runNestedContainerCallHierarchyItems)[0] } else { $null }
    $outgoingNestedContainerCalls = if ($null -ne $runNestedContainerCallHierarchyItem) {
        Send-LspRequest -Process $process -Method "callHierarchy/outgoingCalls" -Params ([ordered]@{
            item = $runNestedContainerCallHierarchyItem
        })
    } else {
        $null
    }
    $hasOutgoingNestedAddBase = $false
    foreach ($call in @($outgoingNestedContainerCalls)) {
        if ($null -ne $call -and $null -ne $call.to -and $call.to.name -eq "add_base") {
            $hasOutgoingNestedAddBase = $true
            break
        }
    }
    Assert-True -Name "lsp_nested_container_outgoing_call_hierarchy" -Condition $hasOutgoingNestedAddBase -Details "Expected outgoing call hierarchy entry for nested container alias call. Actual: $(Format-JsonCompact $outgoingNestedContainerCalls)"

    $urlDocumentLinks = Send-LspRequest -Process $process -Method "textDocument/documentLink" -Params ([ordered]@{
        textDocument = @{ uri = (New-FileUri $urlImportPath) }
    })
    $hasUrlImportLink = $false
    foreach ($link in @($urlDocumentLinks)) {
        if ($null -ne $link -and $link.target -eq "https://example.com/module.cfs") {
            $hasUrlImportLink = $true
            break
        }
    }
    Assert-True -Name "lsp_url_import_document_link" -Condition $hasUrlImportLink -Details "Expected document link for absolute URL import. Actual: $(Format-JsonCompact $urlDocumentLinks)"
}
finally {
    try {
        $null = Send-LspRequest -Process $process -Method "shutdown" -Params $null
    } catch {
    }

    try {
        Send-LspNotification -Process $process -Method "exit" -Params $null
    } catch {
    }

    if (-not $process.HasExited) {
        $process.Kill()
    }

    $stderr = $process.StandardError.ReadToEnd()
    if (-not [string]::IsNullOrWhiteSpace($stderr)) {
        Write-Host $stderr -ForegroundColor Yellow
    }
}

if ($script:AnyFailed) {
    throw "LSP regression smoke failed."
}

Write-Host "[DONE] LSP regression smoke passed." -ForegroundColor Green
