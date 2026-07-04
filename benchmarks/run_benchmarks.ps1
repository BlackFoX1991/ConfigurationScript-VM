<#
.SYNOPSIS
    Runs the CFGS benchmark suite and reports how fast the VM executes each workload.

.DESCRIPTION
    Each case in .\cases\*.cfs is a self-contained CFGS program sized to run for
    roughly one to three seconds. The runner measures wall-clock time per run,
    subtracts a baseline (an almost-empty script) to estimate pure VM time, and
    reports min / median times plus a throughput figure derived from the work
    units declared in each case header (# units: / # unit-label:).

    Numbers are only meaningful in a Release build. Debug is several times slower.

.PARAMETER Configuration
    Build configuration to benchmark. Default: Release.

.PARAMETER Runs
    Measured runs per case (the minimum is reported as the headline number).

.PARAMETER Warmup
    Warmup runs per case, discarded (lets the JIT and file cache settle).

.PARAMETER Filter
    Only run cases whose file name contains this substring.

.PARAMETER SkipBuild
    Do not build; use the existing binaries under dist\<Configuration>\net10.0.

.PARAMETER Json
    Emit the results as JSON on stdout instead of a table.

.EXAMPLE
    .\benchmarks\run_benchmarks.ps1
    .\benchmarks\run_benchmarks.ps1 -Runs 7 -Filter loop
    .\benchmarks\run_benchmarks.ps1 -Configuration Debug -SkipBuild
#>
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [int]$Runs = 5,
    [int]$Warmup = 1,
    [string]$Filter = '',
    [switch]$SkipBuild,
    [switch]$Json
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir '..')
$casesDir = Join-Path $scriptDir 'cases'
$project = Join-Path $repoRoot 'CFGS_NE\CFGS_VM.csproj'
$vmDll = Join-Path $repoRoot ("dist\{0}\net10.0\CFGS_VM.dll" -f $Configuration)

function Get-Median {
    param([double[]]$Values)
    $sorted = @($Values | Sort-Object)
    $n = $sorted.Count
    if ($n -eq 0) { return 0 }
    if ($n % 2 -eq 1) { return $sorted[[int](($n - 1) / 2)] }
    return ($sorted[$n / 2 - 1] + $sorted[$n / 2]) / 2
}

function Get-CaseMeta {
    param([string]$Path)
    $name = [System.IO.Path]::GetFileNameWithoutExtension($Path)
    $units = 0
    $label = 'ops'
    foreach ($line in (Get-Content -LiteralPath $Path -TotalCount 12)) {
        if ($line -match '^\s*#\s*name:\s*(.+?)\s*$') { $name = $Matches[1] }
        elseif ($line -match '^\s*#\s*units:\s*(\d+)\s*$') { $units = [double]$Matches[1] }
        elseif ($line -match '^\s*#\s*unit-label:\s*(.+?)\s*$') { $label = $Matches[1] }
    }
    return [pscustomobject]@{ Name = $name; Units = $units; Label = $label }
}

function Invoke-Timed {
    param([string]$File)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $output = & dotnet $vmDll $File 2>&1
    $sw.Stop()
    $text = ($output | Out-String)
    $ok = ($LASTEXITCODE -eq 0) -and ($text -notmatch 'CFGS\d{4}|\[.*Error.*\]|Runtime error|Uncaught')
    return [pscustomobject]@{ Ms = $sw.Elapsed.TotalMilliseconds; Ok = $ok; Output = $text.Trim() }
}

function Measure-Case {
    param([string]$File)
    for ($w = 0; $w -lt $Warmup; $w++) { [void](Invoke-Timed -File $File) }
    $samples = New-Object System.Collections.Generic.List[double]
    $ok = $true
    $lastOutput = ''
    for ($r = 0; $r -lt $Runs; $r++) {
        $result = Invoke-Timed -File $File
        if (-not $result.Ok) { $ok = $false; $lastOutput = $result.Output }
        $samples.Add($result.Ms)
        $lastOutput = $result.Output
    }
    return [pscustomobject]@{ Samples = $samples; Ok = $ok; Output = $lastOutput }
}

function Format-Throughput {
    param([double]$UnitsPerSecond, [string]$Label)
    if ($UnitsPerSecond -le 0) { return '-' }
    if ($UnitsPerSecond -ge 1000000) { return ('{0:N2} M {1}/s' -f ($UnitsPerSecond / 1000000), $Label) }
    if ($UnitsPerSecond -ge 1000) { return ('{0:N1} K {1}/s' -f ($UnitsPerSecond / 1000), $Label) }
    return ('{0:N0} {1}/s' -f $UnitsPerSecond, $Label)
}

# --- Build -------------------------------------------------------------------
if (-not $SkipBuild) {
    Write-Host ("Building CFGS_VM ({0})..." -f $Configuration) -ForegroundColor Cyan
    dotnet build $project -c $Configuration -v q -nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}
if (-not (Test-Path -LiteralPath $vmDll)) {
    throw "VM assembly not found: $vmDll (build first, or drop -SkipBuild)."
}

# --- Environment -------------------------------------------------------------
$cpu = ''
$cores = ''
try {
    $proc = Get-CimInstance Win32_Processor -ErrorAction Stop | Select-Object -First 1
    $cpu = $proc.Name.Trim()
    $cores = "$($proc.NumberOfCores)C/$($proc.NumberOfLogicalProcessors)T"
} catch { }
$dotnetVersion = (& dotnet --version)

# --- Baseline ----------------------------------------------------------------
$baselineFile = Join-Path $casesDir '_baseline.cfs'
$baselineMin = 0.0
if (Test-Path -LiteralPath $baselineFile) {
    Write-Host 'Measuring baseline (process + startup + compile)...' -ForegroundColor Cyan
    $baseResult = Measure-Case -File $baselineFile
    $baselineMin = ($baseResult.Samples | Measure-Object -Minimum).Minimum
}

# --- Cases -------------------------------------------------------------------
$caseFiles = Get-ChildItem -LiteralPath $casesDir -File -Filter '*.cfs' |
    Where-Object { $_.Name -ne '_baseline.cfs' } |
    Where-Object { [string]::IsNullOrEmpty($Filter) -or $_.Name -like "*$Filter*" } |
    Sort-Object Name

$results = New-Object System.Collections.Generic.List[object]

foreach ($file in $caseFiles) {
    $meta = Get-CaseMeta -Path $file.FullName
    Write-Host ("Running {0} ..." -f $meta.Name) -ForegroundColor DarkGray
    $measured = Measure-Case -File $file.FullName

    $min = ($measured.Samples | Measure-Object -Minimum).Minimum
    $median = Get-Median -Values $measured.Samples
    $mean = ($measured.Samples | Measure-Object -Average).Average
    $net = [Math]::Max(0.0, $min - $baselineMin)
    $throughput = 0.0
    if ($net -gt 0 -and $meta.Units -gt 0) { $throughput = $meta.Units / ($net / 1000.0) }

    $results.Add([pscustomobject]@{
        Case         = $meta.Name
        Units        = $meta.Units
        Label        = $meta.Label
        MinMs        = [Math]::Round($min, 1)
        MedianMs     = [Math]::Round($median, 1)
        MeanMs       = [Math]::Round($mean, 1)
        NetMs        = [Math]::Round($net, 1)
        Throughput   = Format-Throughput -UnitsPerSecond $throughput -Label $meta.Label
        Ok           = $measured.Ok
        Output       = $measured.Output
    })
}

# --- Report ------------------------------------------------------------------
if ($Json) {
    [pscustomobject]@{
        configuration = $Configuration
        runs          = $Runs
        warmup        = $Warmup
        cpu           = $cpu
        cores         = $cores
        dotnet        = $dotnetVersion
        baselineMs    = [Math]::Round($baselineMin, 1)
        results       = $results
    } | ConvertTo-Json -Depth 5
    return
}

Write-Host ''
Write-Host '=== CFGS benchmark results ===' -ForegroundColor Green
Write-Host ("Config: {0}   Runs: {1} (+{2} warmup)   Baseline: {3:N0} ms" -f $Configuration, $Runs, $Warmup, $baselineMin)
if ($cpu) { Write-Host ("CPU: {0} ({1})   .NET: {2}" -f $cpu, $cores, $dotnetVersion) }
Write-Host ''

$table = $results | Format-Table `
    @{ Label = 'Case'; Expression = { $_.Case }; Width = 34 },
    @{ Label = 'Min ms'; Expression = { '{0:N1}' -f $_.MinMs }; Align = 'Right' },
    @{ Label = 'Median'; Expression = { '{0:N1}' -f $_.MedianMs }; Align = 'Right' },
    @{ Label = 'Net ms'; Expression = { '{0:N1}' -f $_.NetMs }; Align = 'Right'; Width = 9 },
    @{ Label = 'Throughput (net)'; Expression = { $_.Throughput } },
    @{ Label = 'OK'; Expression = { if ($_.Ok) { 'yes' } else { 'FAIL' } } } `
    -AutoSize | Out-String
Write-Host $table

$failed = @($results | Where-Object { -not $_.Ok })
if ($failed.Count -gt 0) {
    Write-Host ("{0} case(s) FAILED to run cleanly:" -f $failed.Count) -ForegroundColor Red
    foreach ($f in $failed) { Write-Host ("  {0}: {1}" -f $f.Case, $f.Output) -ForegroundColor Red }
    exit 1
}

Write-Host 'Net ms = min minus baseline (an estimate of pure VM time; startup excluded).' -ForegroundColor DarkGray
exit 0
