param(
    [switch]$SkipBuild,
    [string]$Filter = "",
    [switch]$VerboseOutput,
    [switch]$IncludeSkippedNative
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir "..\..")
$solution = Join-Path $repoRoot "CFGS_VM.sln"
$edgeAwaitablesProject = Join-Path $repoRoot "CFGS.EdgeAwaitables\CFGS.EdgeAwaitables.csproj"
$vmDll = Join-Path $repoRoot "dist\Debug\net10.0\CFGS_VM.dll"
$importsDir = Join-Path $scriptDir "_imports"
$createdImportsDir = $false
$copiedImportFiles = New-Object System.Collections.Generic.List[string]

function Write-Status($status, $name, $message = "") {
    $line = "{0,-6} {1}" -f $status, $name
    if ($message) {
        $line = "$line - $message"
    }
    Write-Host $line
}

function Initialize-ImportsDirectory {
    if (-not (Test-Path -LiteralPath $importsDir)) {
        New-Item -ItemType Directory -Path $importsDir | Out-Null
        $script:createdImportsDir = $true
    }

    Get-ChildItem -LiteralPath $scriptDir -File -Filter "*.cfs" |
        Where-Object {
            $_.Name -notmatch '^\d+_' -and
            $_.Name -ne 'edge_assert.cfs' -and
            $_.Name -notmatch '^_tmp_'
        } |
        ForEach-Object {
            $target = Join-Path $importsDir $_.Name
            if (-not (Test-Path -LiteralPath $target)) {
                Copy-Item -LiteralPath $_.FullName -Destination $target
                $script:copiedImportFiles.Add($target)
            }
        }
}

function Clear-GeneratedImportsDirectory {
    foreach ($file in $script:copiedImportFiles) {
        if (Test-Path -LiteralPath $file) {
            Remove-Item -LiteralPath $file -Force
        }
    }

    if ($script:createdImportsDir -and (Test-Path -LiteralPath $importsDir)) {
        Remove-Item -LiteralPath $importsDir -Force
    }
}

function Test-DllDependenciesAvailable($testFile) {
    $content = Get-Content -LiteralPath $testFile.FullName -Raw
    $matches = [regex]::Matches($content, 'import\s+(?:native\s+)?"([^"]+\.dll)"')
    foreach ($match in $matches) {
        $dll = $match.Groups[1].Value
        if ([System.IO.Path]::IsPathRooted($dll)) {
            if (-not (Test-Path -LiteralPath $dll)) {
                return @{ Available = $false; Missing = $dll }
            }
            continue
        }

        $candidates = @(
            (Join-Path $scriptDir $dll),
            (Join-Path $repoRoot $dll),
            (Join-Path (Split-Path -Parent $vmDll) $dll)
        )

        $found = $false
        foreach ($candidate in $candidates) {
            if (Test-Path -LiteralPath $candidate) {
                $found = $true
                break
            }
        }

        if (-not $found) {
            return @{ Available = $false; Missing = $dll }
        }
    }

    return @{ Available = $true; Missing = "" }
}

function Get-TestKind($testFile) {
    $content = Get-Content -LiteralPath $testFile.FullName -Raw
    if ($testFile.Name -match '_fail(\.|_)|^501_|^707_|^709_') {
        return "ExpectedFailure"
    }
    if ($content -match 'edge_begin\s*\(') {
        return "EdgeAssert"
    }
    return "Smoke"
}

Push-Location $repoRoot
try {
    if (-not $SkipBuild) {
        dotnet build $solution --no-restore | Write-Host
        if ($LASTEXITCODE -ne 0) {
            throw "Build failed."
        }

        if (Test-Path -LiteralPath $edgeAwaitablesProject) {
            dotnet build $edgeAwaitablesProject | Write-Host
            if ($LASTEXITCODE -ne 0) {
                throw "EdgeAwaitables build failed."
            }
        }
    }

    if (-not (Test-Path -LiteralPath $vmDll)) {
        throw "VM executable not found: $vmDll"
    }

    Initialize-ImportsDirectory

    $tests = Get-ChildItem -LiteralPath $scriptDir -File -Filter "*.cfs" |
        Where-Object {
            $_.Name -match '^\d+_' -and
            $_.Name -notmatch '_dep\.cfs$' -and
            ([string]::IsNullOrWhiteSpace($Filter) -or $_.Name -like "*$Filter*")
        } |
        Sort-Object Name

    $passed = 0
    $failed = 0
    $skipped = 0
    $failedTests = New-Object System.Collections.Generic.List[string]
    $skippedTests = New-Object System.Collections.Generic.List[string]

    foreach ($test in $tests) {
        $kind = Get-TestKind $test
        $dllCheck = Test-DllDependenciesAvailable $test
        if ($kind -ne "ExpectedFailure" -and -not $dllCheck.Available -and -not $IncludeSkippedNative) {
            $skipped++
            $skippedTests.Add("$($test.Name) (missing $($dllCheck.Missing))")
            Write-Status "SKIP" $test.Name "missing dll dependency $($dllCheck.Missing)"
            continue
        }

        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        $output = & dotnet $vmDll $test.FullName 2>&1
        $exitCode = $LASTEXITCODE
        $stopwatch.Stop()

        $text = ($output | Out-String)
        if ($VerboseOutput) {
            Write-Host $text
        }

        $ok = $false
        switch ($kind) {
            "ExpectedFailure" {
                $ok = $exitCode -ne 0 -or
                    $text -match 'CFGS\d{4}|\[.*Error.*\]|Parser error|Compiler error|Runtime error|Could not load|Uncaught system exception'
            }
            "EdgeAssert" {
                $ok = $exitCode -eq 0 -and
                    $text -match 'EDGE_OK:' -and
                    $text -notmatch 'EDGE_FAIL|EDGE_FAIL_CASE|CFGS\d{4}|\[.*Error.*\]|Runtime error|Could not load|Uncaught system exception'
            }
            default {
                $ok = ($text -match 'EDGE_OK:' -and $text -notmatch 'EDGE_FAIL') -or
                    ($exitCode -eq 0 -and
                        $text -notmatch 'EDGE_FAIL|EDGE_FAIL_CASE|CFGS\d{4}|\[.*Error.*\]|Runtime error|Could not load|Uncaught system exception')
            }
        }

        $duration = "{0:n2}s" -f $stopwatch.Elapsed.TotalSeconds
        if ($ok) {
            $passed++
            Write-Status "PASS" $test.Name $duration
        } else {
            $failed++
            $failedTests.Add($test.Name)
            Write-Status "FAIL" $test.Name $duration
            if (-not $VerboseOutput) {
                ($output | Select-Object -Last 20) | ForEach-Object { Write-Host "       $_" }
            }
        }
    }

    Write-Host ""
    Write-Host ("EDGECASE_SUMMARY:TOTAL={0}:PASSED={1}:FAILED={2}:SKIPPED={3}" -f $tests.Count, $passed, $failed, $skipped)

    if ($skippedTests.Count -gt 0) {
        Write-Host "Skipped:"
        $skippedTests | ForEach-Object { Write-Host "  $_" }
    }

    if ($failedTests.Count -gt 0) {
        Write-Host "Failed:"
        $failedTests | ForEach-Object { Write-Host "  $_" }
        exit 1
    }

    exit 0
}
finally {
    Clear-GeneratedImportsDirectory
    Pop-Location
}
