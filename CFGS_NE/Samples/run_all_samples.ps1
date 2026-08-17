<#
.SYNOPSIS
    Runs every sample script and fails if any of them does not complete cleanly.

.DESCRIPTION
    The samples under CFGS_NE\Samples are the documentation's runnable examples, and
    each one self-checks: it throws through its own `ensure(...)` helper when a result is
    wrong and prints a marker line when it reaches the end. Until this runner existed
    nothing executed them, so they silently rotted against language changes.

    Compile-only validation is not enough here. Several stale constructs (bracket access
    on a namespace import alias, renamed builtins) only fail at runtime, so every sample
    is actually executed.

    A sample that is imported by another sample is treated as a module: it declares
    symbols instead of running a program, so only successful initialization is required
    and no marker is expected. Module files are discovered from the `import "...cfs"`
    statements of the other samples rather than hard-coded.

.PARAMETER Configuration
    Build configuration of the VM to test against. Default: Release.

.PARAMETER SkipBuild
    Do not build; use whatever is already under dist\<Configuration>\net10.0.

.PARAMETER Filter
    Only run samples whose file name contains this substring.

.PARAMETER VerboseOutput
    Print the full output of every sample, not just of the failing ones.

.PARAMETER IncludeSkippedNative
    Run samples even when a .dll they import cannot be found.

.EXAMPLE
    pwsh CFGS_NE/Samples/run_all_samples.ps1

.EXAMPLE
    pwsh CFGS_NE/Samples/run_all_samples.ps1 -SkipBuild -Filter oop
#>
param(
    [string]$Configuration = "Release",
    [switch]$SkipBuild,
    [string]$Filter = "",
    [switch]$VerboseOutput,
    [switch]$IncludeSkippedNative
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir "..\..")
$solution = Join-Path $repoRoot "CFGS_VM.sln"
$binDir = Join-Path $repoRoot "dist\$Configuration\net10.0"
$vmDll = Join-Path $binDir "CFGS_VM.dll"

function Write-Status($status, $name, $message = "") {
    $line = "{0,-6} {1}" -f $status, $name
    if ($message) {
        $line = "$line - $message"
    }
    Write-Host $line
}

# A sample that some other sample imports is a module: it declares symbols and is not a
# program in its own right, so it prints no marker. Derived from the sources instead of
# hard-coded so that adding a module to a sample folder does not silently fail the gate.
function Get-ModuleFileNames($sampleFiles) {
    $modules = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $sampleFiles) {
        $content = Get-Content -LiteralPath $file.FullName -Raw
        foreach ($match in [regex]::Matches($content, 'import\s+(?:\*\s+as\s+\w+\s+from\s+|\{[^}]*\}\s+from\s+)?"([^"]+\.cfs)"')) {
            [void]$modules.Add([System.IO.Path]::GetFileName($match.Groups[1].Value))
        }
    }
    return $modules
}

function Test-DllDependenciesAvailable($sampleFile) {
    $content = Get-Content -LiteralPath $sampleFile.FullName -Raw
    foreach ($match in [regex]::Matches($content, 'import\s+(?:native\s+)?"([^"]+\.dll)"')) {
        $dll = $match.Groups[1].Value
        if ([System.IO.Path]::IsPathRooted($dll)) {
            if (-not (Test-Path -LiteralPath $dll)) {
                return @{ Available = $false; Missing = $dll }
            }
            continue
        }

        $candidates = @(
            (Join-Path $sampleFile.DirectoryName $dll),
            (Join-Path $repoRoot $dll),
            (Join-Path $binDir $dll)
        )

        if (-not ($candidates | Where-Object { Test-Path -LiteralPath $_ })) {
            return @{ Available = $false; Missing = $dll }
        }
    }

    return @{ Available = $true; Missing = "" }
}

Push-Location $repoRoot
try {
    if (-not $SkipBuild) {
        dotnet build $solution -c $Configuration --nologo | Write-Host
        if ($LASTEXITCODE -ne 0) {
            throw "Build failed."
        }
    }

    if (-not (Test-Path -LiteralPath $vmDll)) {
        throw "VM executable not found: $vmDll"
    }

    $allSamples = Get-ChildItem -LiteralPath $scriptDir -File -Filter "*.cfs" -Recurse | Sort-Object FullName
    $moduleNames = Get-ModuleFileNames $allSamples

    $samples = $allSamples | Where-Object {
        [string]::IsNullOrWhiteSpace($Filter) -or $_.Name -like "*$Filter*"
    }

    $passed = 0
    $failed = 0
    $skipped = 0
    $failedSamples = New-Object System.Collections.Generic.List[string]
    $skippedSamples = New-Object System.Collections.Generic.List[string]

    foreach ($sample in $samples) {
        $relative = $sample.FullName.Substring($repoRoot.Path.Length + 1)
        $isModule = $moduleNames.Contains($sample.Name)

        $dllCheck = Test-DllDependenciesAvailable $sample
        if (-not $dllCheck.Available -and -not $IncludeSkippedNative) {
            $skipped++
            $skippedSamples.Add("$relative (missing $($dllCheck.Missing))")
            Write-Status "SKIP" $relative "missing dll dependency $($dllCheck.Missing)"
            continue
        }

        # Samples import their siblings by bare file name, so each one runs from its own
        # directory.
        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        Push-Location $sample.DirectoryName
        try {
            $output = & dotnet $vmDll $sample.Name 2>&1
            $exitCode = $LASTEXITCODE
        }
        finally {
            Pop-Location
        }
        $stopwatch.Stop()

        $text = ($output | Out-String)
        if ($VerboseOutput) {
            Write-Host $text
        }

        # Every sample must exit cleanly and must not have printed a diagnostic. A program
        # sample must additionally have reached its end-of-run marker, which is what proves
        # its `ensure(...)` checks all passed rather than the file merely parsing.
        $ok = $exitCode -eq 0 -and $text -notmatch 'CFGS\d{4}|\[.*Error.*\]|Uncaught system exception'
        if ($ok -and -not $isModule) {
            $ok = $text -match '_OK:|_SUMMARY'
        }

        $duration = "{0:n2}s" -f $stopwatch.Elapsed.TotalSeconds
        if ($ok) {
            $passed++
            Write-Status "PASS" $relative $(if ($isModule) { "$duration (module)" } else { $duration })
        }
        else {
            $failed++
            $failedSamples.Add($relative)
            Write-Status "FAIL" $relative $duration
            if (-not $VerboseOutput) {
                ($output | Select-Object -Last 20) | ForEach-Object { Write-Host "       $_" }
            }
        }
    }

    Write-Host ""
    Write-Host ("SAMPLES_SUMMARY:TOTAL={0}:PASSED={1}:FAILED={2}:SKIPPED={3}" -f $samples.Count, $passed, $failed, $skipped)

    if ($skippedSamples.Count -gt 0) {
        Write-Host "Skipped:"
        $skippedSamples | ForEach-Object { Write-Host "  $_" }
    }

    if ($failedSamples.Count -gt 0) {
        Write-Host "Failed:"
        $failedSamples | ForEach-Object { Write-Host "  $_" }
        exit 1
    }

    exit 0
}
finally {
    Pop-Location
}
