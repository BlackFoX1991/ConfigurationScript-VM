<#
.SYNOPSIS
    Runs every CFGS test suite and fails if any of them reports a failure.

.DESCRIPTION
    The repository has four suites and no single entry point that covers them:

      CFGS.Tooling.Tests   hosting, VM, .cfb, native interop, tooling
      CFGS.Lsp.Tests       language server analysis, hover, diagnostics
      Edgecases            340 .cfs programs run against the built VM
      Samples              the 19 documented example scripts, executed end to end

    Both test projects are OutputType=Exe with hand-written runners rather than a
    test SDK, so "dotnet test CFGS_VM.sln" discovers nothing, prints nothing, and
    exits 0. Anything wired to that command would report success without ever
    executing a test. This script runs the three suites explicitly, checks the
    machine-readable summary line each one prints, and exits non-zero if any suite
    fails or fails to produce a summary at all.

.PARAMETER Configuration
    Build configuration for the two test runners and the sample suite. Default: Release.
    The edge case suite always runs against the Debug build, because that is what
    its own runner builds and expects. That means an unqualified run builds Release here
    and then lets the edge case runner build Debug as well; the two are deliberately not
    unified, because changing which binary the edge cases exercise is a separate decision.

.PARAMETER SkipBuild
    Do not build; use whatever is already under dist\<Configuration>\net10.0.

.PARAMETER SkipEdgecases
    Skip the edge case suite. It is by far the slowest of the four.

.PARAMETER SkipSamples
    Skip the sample suite.

.EXAMPLE
    pwsh scripts/run-all-tests.ps1

.EXAMPLE
    pwsh scripts/run-all-tests.ps1 -SkipBuild -SkipEdgecases
#>
param(
    [string]$Configuration = "Release",
    [switch]$SkipBuild,
    [switch]$SkipEdgecases,
    [switch]$SkipSamples
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir "..")
$solution = Join-Path $repoRoot "CFGS_VM.sln"
$binDir = Join-Path $repoRoot "dist\$Configuration\net10.0"

$results = New-Object System.Collections.Generic.List[psobject]

function Add-Result {
    param([string]$Suite, [bool]$Ok, [string]$Detail)

    $results.Add([pscustomobject]@{
        Suite  = $Suite
        Status = if ($Ok) { "PASS" } else { "FAIL" }
        Detail = $Detail
    })
}

# Runs one of the console test runners and reads back the SUMMARY line it prints.
# A missing summary is treated as a failure: it means the runner crashed or was
# replaced by something that does not report, which must not pass silently.
function Invoke-TestRunner {
    param([string]$Suite, [string]$DllName, [string]$SummaryPrefix)

    $dll = Join-Path $binDir $DllName
    if (-not (Test-Path -LiteralPath $dll)) {
        Add-Result $Suite $false "not built: $dll"
        return
    }

    Push-Location $binDir
    try {
        $output = & dotnet $dll 2>&1
        $exit = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    $summary = $output |
        ForEach-Object { $_.ToString() } |
        Where-Object { $_.StartsWith($SummaryPrefix, [StringComparison]::Ordinal) } |
        Select-Object -Last 1

    if (-not $summary) {
        Write-Host ($output | Out-String)
        Add-Result $Suite $false "no $SummaryPrefix line (exit $exit)"
        return
    }

    if ($exit -ne 0) {
        $output | Where-Object { $_.ToString().StartsWith("FAIL", [StringComparison]::Ordinal) } | ForEach-Object { Write-Host "    $_" }
    }

    Add-Result $Suite ($exit -eq 0) $summary
}

# Same contract as Invoke-TestRunner, but for the two suites that are PowerShell scripts
# driving the built VM over a folder of .cfs files rather than compiled runners.
function Invoke-ScriptSuite {
    param([string]$Suite, [string]$ScriptPath, [string]$SummaryPrefix, [string[]]$Arguments = @())

    if (-not (Test-Path -LiteralPath $ScriptPath)) {
        Add-Result $Suite $false "runner not found: $ScriptPath"
        return
    }

    $output = & pwsh -NoProfile -File $ScriptPath @Arguments
    $exit = $LASTEXITCODE

    $summary = $output |
        ForEach-Object { $_.ToString() } |
        Where-Object { $_.StartsWith($SummaryPrefix, [StringComparison]::Ordinal) } |
        Select-Object -Last 1

    if (-not $summary) {
        Write-Host ($output | Out-String)
        Add-Result $Suite $false "no $SummaryPrefix line (exit $exit)"
        return
    }

    if ($exit -ne 0) {
        $output | Where-Object { $_.ToString().StartsWith("FAIL", [StringComparison]::Ordinal) } | ForEach-Object { Write-Host "    $_" }
    }

    Add-Result $Suite ($exit -eq 0) $summary
}

Push-Location $repoRoot
try {
    if (-not $SkipBuild) {
        Write-Host "Building $Configuration ..."
        dotnet build $solution -c $Configuration --nologo | Write-Host
        if ($LASTEXITCODE -ne 0) {
            throw "Build failed."
        }
    }

    Write-Host "Running CFGS.Tooling.Tests ..."
    Invoke-TestRunner "CFGS.Tooling.Tests" "CFGS.Tooling.Tests.dll" "TOOLING_TEST_SUMMARY:"

    Write-Host "Running CFGS.Lsp.Tests ..."
    Invoke-TestRunner "CFGS.Lsp.Tests" "CFGS.Lsp.Tests.dll" "LSP_TEST_SUMMARY:"

    if ($SkipEdgecases) {
        Add-Result "Edgecases" $true "skipped (-SkipEdgecases)"
    }
    else {
        Write-Host "Running edge case suite ..."
        Invoke-ScriptSuite "Edgecases" (Join-Path $repoRoot "CFGS_NE\Edgecases\run_all_edgecases.ps1") "EDGECASE_SUMMARY:"
    }

    if ($SkipSamples) {
        Add-Result "Samples" $true "skipped (-SkipSamples)"
    }
    else {
        Write-Host "Running sample suite ..."
        # The solution was already built above, so the runner reuses that output instead
        # of building a second time.
        Invoke-ScriptSuite "Samples" (Join-Path $repoRoot "CFGS_NE\Samples\run_all_samples.ps1") "SAMPLES_SUMMARY:" @("-Configuration", $Configuration, "-SkipBuild")
    }

    Write-Host ""
    Write-Host "=== CFGS test summary ==="
    $results | Format-Table -AutoSize | Out-String | Write-Host

    $failed = @($results | Where-Object { $_.Status -eq "FAIL" })
    if ($failed.Count -gt 0) {
        Write-Host "ALL_TESTS_SUMMARY:RESULT=FAIL:FAILED_SUITES=$($failed.Count)"
        exit 1
    }

    Write-Host "ALL_TESTS_SUMMARY:RESULT=PASS:FAILED_SUITES=0"
    exit 0
}
finally {
    Pop-Location
}
