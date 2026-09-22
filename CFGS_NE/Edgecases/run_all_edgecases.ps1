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
$vmDll = Join-Path $repoRoot "dist\Debug\net10.0\CFGS_VM.dll"
$importsDir = Join-Path $scriptDir "_imports"
$createdImportsDir = $false
$copiedImportFiles = New-Object System.Collections.Generic.List[string]
$fixturesDir = Join-Path $scriptDir "_fixtures"
$createdFixturesDir = $false
$partialPluginProject = Join-Path $scriptDir "Fixtures\PartialRegistrationPlugin\CFGS.EdgePartial.csproj"
$errorMarkers = 'EDGE_FAIL|EDGE_FAIL_CASE|CFGS\d{4}|\[.*Error.*\]|Runtime error|Could not load|Uncaught system exception'

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

# Test fixtures that must not live in the repository as binaries: a file that is not a managed
# assembly (494) and a plugin whose registration throws halfway (506). Built against the VM under test
# on every run so the plugin never goes stale, and removed again afterwards.
function Initialize-Fixtures {
    if (-not (Test-Path -LiteralPath $fixturesDir)) {
        New-Item -ItemType Directory -Path $fixturesDir | Out-Null
        $script:createdFixturesDir = $true
    }

    Set-Content -LiteralPath (Join-Path $fixturesDir "_invalid_plugin.dll") -Value "not a real managed assembly" -NoNewline

    $buildOutput = & dotnet build $partialPluginProject -c Release --nologo -v q -o $fixturesDir "-p:CfgsVmAssembly=$vmDll" 2>&1
    if ($LASTEXITCODE -ne 0) {
        # No throw: the fixture's test then fails with a precise expectation mismatch.
        Write-Host "WARNING: building the CFGS.EdgePartial fixture failed:"
        $buildOutput | Select-Object -Last 10 | ForEach-Object { Write-Host "       $_" }
    }
}

function Clear-Fixtures {
    if ($script:createdFixturesDir -and (Test-Path -LiteralPath $fixturesDir)) {
        Remove-Item -LiteralPath $fixturesDir -Recurse -Force
    }
}

# An expected-failure test states the diagnostic it must produce in a header comment:
#   # expect-error: CFGS1000 break can only be used in loops
# The code is required, the message fragment is recommended. The first CFGS diagnostic in the output
# must carry exactly that code and contain the fragment, so a test that starts failing for a different
# reason (a missing fixture, a renamed builtin, a changed rule) fails instead of passing silently.
function Get-ErrorExpectation($testFile) {
    $lines = @(Select-String -LiteralPath $testFile.FullName -Pattern '^\s*#\s*expect-error:\s*(.*?)\s*$')
    if ($lines.Count -eq 0) {
        return $null
    }

    if ($lines.Count -gt 1) {
        return @{ Invalid = "more than one '# expect-error:' header" }
    }

    $spec = $lines[0].Matches[0].Groups[1].Value
    if ($spec -notmatch '^(CFGS\d{4})(?:\s+(.*))?$') {
        return @{ Invalid = "malformed '# expect-error:' header, expected '# expect-error: CFGSxxxx <message fragment>'" }
    }

    return @{ Code = $Matches[1]; Fragment = "$($Matches[2])".Trim() }
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
    if ($testFile.Name -match '_fail(\.|_)' -or $content -match '(?m)^\s*#\s*expect-error:') {
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
    }

    if (-not (Test-Path -LiteralPath $vmDll)) {
        throw "VM executable not found: $vmDll"
    }

    Initialize-ImportsDirectory
    Initialize-Fixtures

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

        $stem = [System.IO.Path]::GetFileNameWithoutExtension($test.Name)
        $reason = $null
        switch ($kind) {
            "ExpectedFailure" {
                $expectation = Get-ErrorExpectation $test
                $firstDiagnostic = @($output | ForEach-Object { "$_" } | Where-Object { $_ -match '^CFGS\d{4}:' }) | Select-Object -First 1

                if ($null -eq $expectation) {
                    $reason = "missing '# expect-error: CFGSxxxx <message fragment>' header"
                } elseif ($expectation.Invalid) {
                    $reason = $expectation.Invalid
                } elseif ($exitCode -eq 0) {
                    $reason = "expected $($expectation.Code), but the script exited with code 0"
                } elseif (-not $firstDiagnostic) {
                    $reason = "expected $($expectation.Code), but the output has no CFGS diagnostic"
                } elseif (-not $firstDiagnostic.StartsWith("$($expectation.Code):", [System.StringComparison]::Ordinal)) {
                    $reason = "expected $($expectation.Code), got: $firstDiagnostic"
                } elseif ($expectation.Fragment -and -not $firstDiagnostic.Contains($expectation.Fragment)) {
                    $reason = "expected a message containing '$($expectation.Fragment)', got: $firstDiagnostic"
                }
            }
            "EdgeAssert" {
                # The summary must come from this test (not a copied edge_begin name), report at least
                # one case, and report no failure.
                $escapedStem = [regex]::Escape($stem)
                $summary = [regex]::Match($text, "(?m)^EDGE_SUMMARY:${escapedStem}:TOTAL=(\d+):FAILED=(\d+)\r?$")

                if ($exitCode -ne 0) {
                    $reason = "exited with code $exitCode"
                } elseif ($text -match $errorMarkers) {
                    $reason = "output contains a failure or error marker"
                } elseif (-not $summary.Success) {
                    $reason = "no EDGE_SUMMARY line for '$stem' (does edge_begin use the file name?)"
                } elseif ([int]$summary.Groups[1].Value -eq 0) {
                    $reason = "the test ran no cases"
                } elseif ($text -notmatch "(?m)^EDGE_OK:${escapedStem}\r?$") {
                    $reason = "no EDGE_OK line for '$stem'"
                }
            }
            default {
                if ($exitCode -ne 0) {
                    $reason = "exited with code $exitCode"
                } elseif ($text -match $errorMarkers) {
                    $reason = "output contains a failure or error marker"
                }
            }
        }
        $ok = $null -eq $reason

        $duration = "{0:n2}s" -f $stopwatch.Elapsed.TotalSeconds
        if ($ok) {
            $passed++
            Write-Status "PASS" $test.Name $duration
        } else {
            $failed++
            $failedTests.Add($test.Name)
            Write-Status "FAIL" $test.Name "$duration - $reason"
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
    Clear-Fixtures
    Pop-Location
}
