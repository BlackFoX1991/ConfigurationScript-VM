param(
    [switch]$SkipBuild,
    [string]$NameFilter = "*",
    [string]$DllDir = ""
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false
$script:AnyFailed = $false

$edgeDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $edgeDir
$repoParent = Split-Path -Parent $repoRoot
$defaultBuiltDllDir = Join-Path $repoParent "dist\Debug\net10.0"
$edgeAwaitablesProject = Join-Path $edgeDir "EdgeAwaitablesPlugin\CFGS.EdgeAwaitables.csproj"
$edgePartialProject = Join-Path $edgeDir "EdgePartialPlugin\CFGS.EdgePartial.csproj"

if ([string]::IsNullOrWhiteSpace($DllDir)) {
    $DllDir = $defaultBuiltDllDir
}

# Keep dotnet first-run/sentinel writes inside writable workspace.
$dotnetHome = Join-Path $edgeDir "._tmp_dotnet_home"
New-Item -ItemType Directory -Force -Path $dotnetHome | Out-Null
[Environment]::SetEnvironmentVariable("DOTNET_CLI_HOME", $dotnetHome, "Process")
[Environment]::SetEnvironmentVariable("DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "1", "Process")
[Environment]::SetEnvironmentVariable("DOTNET_NOLOGO", "1", "Process")

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

function Ensure-StdLib {
    param(
        [string]$TargetDir,
        [string]$SourceDir
    )

    $dllName = "CFGS.StandardLibrary.dll"
    $targetPath = Join-Path $TargetDir $dllName
    $sourcePath = Join-Path $SourceDir $dllName
    if (Test-Path -LiteralPath $sourcePath) {
        Copy-Item -LiteralPath $sourcePath -Destination $targetPath -Force
        Write-Host "[INFO] Copied $dllName from '$SourceDir'"

        $depsName = "CFGS.StandardLibrary.deps.json"
        $sourceDepsPath = Join-Path $SourceDir $depsName
        $targetDepsPath = Join-Path $TargetDir $depsName
        if (Test-Path -LiteralPath $sourceDepsPath) {
            Copy-Item -LiteralPath $sourceDepsPath -Destination $targetDepsPath -Force
            Write-Host "[INFO] Copied $depsName from '$SourceDir'"
        }

        return
    }

    throw "Required DLL '$dllName' not found. Checked '$targetPath' and '$sourcePath'."
}

function Ensure-OptionalPluginDlls {
    param(
        [string]$TargetDir,
        [string]$SourceDir
    )

    $optionalDlls = @(
        "CFGS.Web.Http.dll",
        "CFGS.Microsoft.SQL.dll"
    )

    $copiedAnyOptional = $false
    foreach ($dllName in $optionalDlls) {
        $sourcePath = Join-Path $SourceDir $dllName
        if (Test-Path -LiteralPath $sourcePath) {
            $targetPath = Join-Path $TargetDir $dllName
            Copy-Item -LiteralPath $sourcePath -Destination $targetPath -Force
            Write-Host "[INFO] Copied $dllName from '$SourceDir'"
            $copiedAnyOptional = $true
        } else {
            Write-Host "[WARN] Optional DLL '$dllName' not found in '$SourceDir'." -ForegroundColor Yellow
        }
    }

    if (-not $copiedAnyOptional) {
        return
    }

    $sourceFull = [IO.Path]::GetFullPath($SourceDir)
    $targetFull = [IO.Path]::GetFullPath($TargetDir)
    if ([string]::Equals($sourceFull, $targetFull, [StringComparison]::OrdinalIgnoreCase)) {
        return
    }

    # Keep plugin runtime dependencies resolvable for isolated edgecase execution.
    Get-ChildItem -LiteralPath $SourceDir -File | Where-Object {
        $_.Name -like "*.dll" -or $_.Name -like "*.deps.json" -or $_.Name -like "*.runtimeconfig.json" -or $_.Name -like "*.pdb"
    } | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $TargetDir $_.Name) -Force
    }

    $runtimeDir = Join-Path $SourceDir "runtimes"
    if (Test-Path -LiteralPath $runtimeDir) {
        Copy-Item -LiteralPath $runtimeDir -Destination (Join-Path $TargetDir "runtimes") -Recurse -Force
    }
}

function Build-ProjectIfExists {
    param(
        [string]$ProjectPath,
        [string]$Label
    )

    if (-not (Test-Path -LiteralPath $ProjectPath)) {
        Write-Host "[WARN] Skipping build for $Label (not found: '$ProjectPath')." -ForegroundColor Yellow
        return
    }

    Write-Host "[INFO] Building $Label..."
    & dotnet build $ProjectPath --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed for $Label."
    }
}

function Invoke-Cfgs {
    param(
        [string[]]$CfgsArgs,
        [hashtable]$EnvVars = @{},
        [string]$StdinText = ""
    )

    $savedEnv = @{}
    foreach ($key in $EnvVars.Keys) {
        $savedEnv[$key] = [Environment]::GetEnvironmentVariable($key, "Process")
        $value = $EnvVars[$key]
        if ($null -eq $value) {
            [Environment]::SetEnvironmentVariable($key, $null, "Process")
        } else {
            [Environment]::SetEnvironmentVariable($key, [string]$value, "Process")
        }
    }

    try {
        Push-Location $repoRoot
        try {
            $savedEa = $ErrorActionPreference
            try {
                $ErrorActionPreference = "Continue"
                if ([string]::IsNullOrEmpty($StdinText)) {
                    $lines = & dotnet run --no-build --no-launch-profile --project CFGS_VM.csproj -p:OutputPath=bin\\Debug\\net10.0\\ -- @CfgsArgs 2>&1
                } else {
                    $lines = $StdinText | & dotnet run --no-build --no-launch-profile --project CFGS_VM.csproj -p:OutputPath=bin\\Debug\\net10.0\\ -- @CfgsArgs 2>&1
                }
                $exitCode = $LASTEXITCODE
            } finally {
                $ErrorActionPreference = $savedEa
            }
        } finally {
            Pop-Location
        }

        $text = ($lines | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine

        return [PSCustomObject]@{
            Output = $text
            ExitCode = $exitCode
        }
    } finally {
        foreach ($key in $EnvVars.Keys) {
            [Environment]::SetEnvironmentVariable($key, $savedEnv[$key], "Process")
        }
    }
}

function Edge-Path {
    param([string]$RelativePath)
    return (Join-Path $repoRoot $RelativePath)
}

function Run-ExpectContains {
    param(
        [string]$Name,
        [string[]]$ScriptArgs,
        [string]$Expected,
        [hashtable]$EnvVars = @{}
    )

    if ($Name -notlike $NameFilter) {
        return
    }

    $res = Invoke-Cfgs -CfgsArgs $ScriptArgs -EnvVars $EnvVars
    $ok = $res.Output.Contains($Expected)
    if ($ok) {
        Write-Result -Name $Name -Ok $true
    } else {
        Write-Result -Name $Name -Ok $false -Details "Expected output containing '$Expected'."
        Write-Host $res.Output
    }
}

function Run-ExpectNotContains {
    param(
        [string]$Name,
        [string[]]$ScriptArgs,
        [string]$Unexpected,
        [hashtable]$EnvVars = @{}
    )

    if ($Name -notlike $NameFilter) {
        return
    }

    $res = Invoke-Cfgs -CfgsArgs $ScriptArgs -EnvVars $EnvVars
    $ok = -not $res.Output.Contains($Unexpected)
    if ($ok) {
        Write-Result -Name $Name -Ok $true
    } else {
        Write-Result -Name $Name -Ok $false -Details "Unexpected output containing '$Unexpected'."
        Write-Host $res.Output
    }
}

try {
    Write-Host "[INFO] Plugin DLL source: '$DllDir'"

    if (-not $SkipBuild) {
        Write-Host "[INFO] Building CFGS_VM.csproj..."
        Push-Location $repoRoot
        try {
            & dotnet build CFGS_VM.csproj --nologo -p:OutputPath=bin\\Debug\\net10.0\\
            if ($LASTEXITCODE -ne 0) {
                throw "Build failed."
            }
        } finally {
            Pop-Location
        }

        Build-ProjectIfExists -ProjectPath (Join-Path $repoParent "CFGS.StandardLibrary\CFGS.StandardLibrary.csproj") -Label "CFGS.StandardLibrary"
        Build-ProjectIfExists -ProjectPath (Join-Path $repoParent "CFGS.Web.Http\CFGS.Web.Http.csproj") -Label "CFGS.Web.Http"
        Build-ProjectIfExists -ProjectPath (Join-Path $repoParent "CFGS.Microsoft.SQL\CFGS.Microsoft.SQL.csproj") -Label "CFGS.Microsoft.SQL"
        Build-ProjectIfExists -ProjectPath $edgeAwaitablesProject -Label "CFGS.EdgeAwaitables"
        Build-ProjectIfExists -ProjectPath $edgePartialProject -Label "CFGS.EdgePartial"
    }

    Ensure-StdLib -TargetDir $edgeDir -SourceDir $DllDir
    Ensure-OptionalPluginDlls -TargetDir $edgeDir -SourceDir $DllDir

    if ("229_await_non_generic_task_valuetask" -like $NameFilter -or
        "231_plugin_nonblocking_loader" -like $NameFilter -or
        "233_await_generic_task_valuetask" -like $NameFilter) {
        $edgeAwaitDll = Join-Path $edgeDir "CFGS.EdgeAwaitables.dll"
        if (-not (Test-Path -LiteralPath $edgeAwaitDll)) {
            Build-ProjectIfExists -ProjectPath $edgeAwaitablesProject -Label "CFGS.EdgeAwaitables"
        }
    }

    if ("506_plugin_register_failfast_fail" -like $NameFilter) {
        $edgePartialDll = Join-Path $edgeDir "CFGS.EdgePartial.dll"
        if (-not (Test-Path -LiteralPath $edgePartialDll)) {
            Build-ProjectIfExists -ProjectPath $edgePartialProject -Label "CFGS.EdgePartial"
        }
    }

    # Positive suites
    Run-ExpectContains -Name "101_core_expressions" -ScriptArgs @((Edge-Path "_edgecases\101_core_expressions.cfs")) -Expected "EDGE_OK:101_core_expressions"
    Run-ExpectContains -Name "102_control_flow_and_match" -ScriptArgs @((Edge-Path "_edgecases\102_control_flow_and_match.cfs")) -Expected "EDGE_OK:102_control_flow_and_match"
    Run-ExpectContains -Name "103_collections_and_delete" -ScriptArgs @((Edge-Path "_edgecases\103_collections_and_delete.cfs")) -Expected "EDGE_OK:103_collections_and_delete"
    Run-ExpectContains -Name "104_literals_and_primitives" -ScriptArgs @((Edge-Path "_edgecases\104_literals_and_primitives.cfs")) -Expected "EDGE_OK:104_literals_and_primitives"
    Run-ExpectContains -Name "201_functions_and_closures" -ScriptArgs @((Edge-Path "_edgecases\201_functions_and_closures.cfs")) -Expected "EDGE_OK:201_functions_and_closures"
    Run-ExpectContains -Name "202_classes_enums_inheritance" -ScriptArgs @((Edge-Path "_edgecases\202_classes_enums_inheritance.cfs")) -Expected "EDGE_OK:202_classes_enums_inheritance"
    Run-ExpectContains -Name "203_nested_classes_and_object_init" -ScriptArgs @((Edge-Path "_edgecases\203_nested_classes_and_object_init.cfs")) -Expected "EDGE_OK:203_nested_classes_and_object_init"
    Run-ExpectContains -Name "204_out_and_await" -ScriptArgs @((Edge-Path "_edgecases\204_out_and_await.cfs")) -Expected "EDGE_OK:204_out_and_await"
    Run-ExpectContains -Name "205_default_and_named_args" -ScriptArgs @((Edge-Path "_edgecases\205_default_and_named_args.cfs")) -Expected "EDGE_OK:205_default_and_named_args"
    Run-ExpectContains -Name "206_match_guards" -ScriptArgs @((Edge-Path "_edgecases\206_match_guards.cfs")) -Expected "EDGE_OK:206_match_guards"
    Run-ExpectContains -Name "207_module_exports_imports" -ScriptArgs @((Edge-Path "_edgecases\207_module_exports_imports.cfs")) -Expected "EDGE_OK:207_module_exports_imports"
    Run-ExpectContains -Name "208_match_patterns_and_bindings" -ScriptArgs @((Edge-Path "_edgecases\208_match_patterns_and_bindings.cfs")) -Expected "EDGE_OK:208_match_patterns_and_bindings"
    Run-ExpectContains -Name "209_module_multiple_import_idempotent" -ScriptArgs @((Edge-Path "_edgecases\209_module_multiple_import_idempotent.cfs")) -Expected "EDGE_OK:209_module_multiple_import_idempotent"
    Run-ExpectContains -Name "210_module_transitive_reimport" -ScriptArgs @((Edge-Path "_edgecases\210_module_transitive_reimport.cfs")) -Expected "EDGE_OK:210_module_transitive_reimport"
    Run-ExpectContains -Name "211_rest_and_spread_calls" -ScriptArgs @((Edge-Path "_edgecases\211_rest_and_spread_calls.cfs")) -Expected "EDGE_OK:211_rest_and_spread_calls"
    Run-ExpectContains -Name "212_destructuring_general" -ScriptArgs @((Edge-Path "_edgecases\212_destructuring_general.cfs")) -Expected "EDGE_OK:212_destructuring_general"
    Run-ExpectContains -Name "213_foreach_destructuring" -ScriptArgs @((Edge-Path "_edgecases\213_foreach_destructuring.cfs")) -Expected "EDGE_OK:213_foreach_destructuring"
    Run-ExpectContains -Name "214_foreach_index_value_pairs" -ScriptArgs @((Edge-Path "_edgecases\214_foreach_index_value_pairs.cfs")) -Expected "EDGE_OK:214_foreach_index_value_pairs"
    Run-ExpectContains -Name "215_namespace_declarations" -ScriptArgs @((Edge-Path "_edgecases\215_namespace_declarations.cfs")) -Expected "EDGE_OK:215_namespace_declarations"
    Run-ExpectContains -Name "216_name_resolution_order" -ScriptArgs @((Edge-Path "_edgecases\216_name_resolution_order.cfs")) -Expected "EDGE_OK:216_name_resolution_order"
    Run-ExpectContains -Name "217_receiver_valid_contexts" -ScriptArgs @((Edge-Path "_edgecases\217_receiver_valid_contexts.cfs")) -Expected "EDGE_OK:217_receiver_valid_contexts"
    Run-ExpectContains -Name "218_override_compatibility" -ScriptArgs @((Edge-Path "_edgecases\218_override_compatibility.cfs")) -Expected "EDGE_OK:218_override_compatibility"
    Run-ExpectContains -Name "219_constructor_flow" -ScriptArgs @((Edge-Path "_edgecases\219_constructor_flow.cfs")) -Expected "EDGE_OK:219_constructor_flow"
    Run-ExpectContains -Name "220_member_access_and_initializer_valid" -ScriptArgs @((Edge-Path "_edgecases\220_member_access_and_initializer_valid.cfs")) -Expected "EDGE_OK:220_member_access_and_initializer_valid"
    Run-ExpectContains -Name "221_namespace_oop_hardening" -ScriptArgs @((Edge-Path "_edgecases\221_namespace_oop_hardening.cfs")) -Expected "EDGE_OK:221_namespace_oop_hardening"
    Run-ExpectContains -Name "222_visibility_access_valid" -ScriptArgs @((Edge-Path "_edgecases\222_visibility_access_valid.cfs")) -Expected "EDGE_OK:222_visibility_access_valid"
    Run-ExpectContains -Name "223_constructor_alias_read_valid" -ScriptArgs @((Edge-Path "_edgecases\223_constructor_alias_read_valid.cfs")) -Expected "EDGE_OK:223_constructor_alias_read_valid"
    Run-ExpectContains -Name "224_override_visibility_compatibility_valid" -ScriptArgs @((Edge-Path "_edgecases\224_override_visibility_compatibility_valid.cfs")) -Expected "EDGE_OK:224_override_visibility_compatibility_valid"
    Run-ExpectContains -Name "225_constructor_access_visibility_valid" -ScriptArgs @((Edge-Path "_edgecases\225_constructor_access_visibility_valid.cfs")) -Expected "EDGE_OK:225_constructor_access_visibility_valid"
    Run-ExpectContains -Name "226_async_await_nonblocking_collections" -ScriptArgs @((Edge-Path "_edgecases\226_async_await_nonblocking_collections.cfs")) -Expected "EDGE_OK:226_async_await_nonblocking_collections"
    Run-ExpectContains -Name "227_yield_statement" -ScriptArgs @((Edge-Path "_edgecases\227_yield_statement.cfs")) -Expected "EDGE_OK:227_yield_statement"
    Run-ExpectContains -Name "228_async_function_task_contract" -ScriptArgs @((Edge-Path "_edgecases\228_async_function_task_contract.cfs")) -Expected "EDGE_OK:228_async_function_task_contract"
    Run-ExpectContains -Name "229_await_non_generic_task_valuetask" -ScriptArgs @((Edge-Path "_edgecases\229_await_non_generic_task_valuetask.cfs")) -Expected "EDGE_OK:229_await_non_generic_task_valuetask"
    Run-ExpectContains -Name "230_async_hot_start_call_order" -ScriptArgs @((Edge-Path "_edgecases\230_async_hot_start_call_order.cfs")) -Expected "EDGE_OK:230_async_hot_start_call_order"
    Run-ExpectContains -Name "231_plugin_nonblocking_loader" -ScriptArgs @((Edge-Path "_edgecases\231_plugin_nonblocking_loader.cfs")) -Expected "EDGE_OK:231_plugin_nonblocking_loader"
    Run-ExpectContains -Name "232_plugin_http_server_async" -ScriptArgs @((Edge-Path "_edgecases\232_plugin_http_server_async.cfs")) -Expected "EDGE_OK:232_plugin_http_server_async"
    Run-ExpectContains -Name "233_await_generic_task_valuetask" -ScriptArgs @((Edge-Path "_edgecases\233_await_generic_task_valuetask.cfs")) -Expected "EDGE_OK:233_await_generic_task_valuetask"
    Run-ExpectContains -Name "234_fromjson_vm_shape" -ScriptArgs @((Edge-Path "_edgecases\234_fromjson_vm_shape.cfs")) -Expected "EDGE_OK:234_fromjson_vm_shape"
    Run-ExpectContains -Name "235_sql_connect_failure_contract" -ScriptArgs @((Edge-Path "_edgecases\235_sql_connect_failure_contract.cfs")) -Expected "EDGE_OK:235_sql_connect_failure_contract"
    Run-ExpectContains -Name "236_destroy_builtin" -ScriptArgs @((Edge-Path "_edgecases\236_destroy_builtin.cfs")) -Expected "EDGE_OK:236_destroy_builtin"
    Run-ExpectContains -Name "237_using_statement" -ScriptArgs @((Edge-Path "_edgecases\237_using_statement.cfs")) -Expected "EDGE_OK:237_using_statement"
    Run-ExpectContains -Name "238_defer_statement" -ScriptArgs @((Edge-Path "_edgecases\238_defer_statement.cfs")) -Expected "EDGE_OK:238_defer_statement"
    Run-ExpectContains -Name "239_interfaces_and_implements" -ScriptArgs @((Edge-Path "_edgecases\239_interfaces_and_implements.cfs")) -Expected "EDGE_OK:239_interfaces_and_implements"
    Run-ExpectContains -Name "301_try_throw_finally" -ScriptArgs @((Edge-Path "_edgecases\301_try_throw_finally.cfs")) -Expected "EDGE_OK:301_try_throw_finally"
    Run-ExpectContains -Name "402_import_named_and_file" -ScriptArgs @((Edge-Path "_edgecases\402_import_named_and_file.cfs")) -Expected "EDGE_OK:402_import_named_and_file"
    if ("495_plugin_multifile_reload" -like $NameFilter) {
        $resMulti = Invoke-Cfgs -CfgsArgs @(
            (Edge-Path "_edgecases\495_plugin_reload_multifile_a.cfs"),
            (Edge-Path "_edgecases\496_plugin_reload_multifile_b.cfs")
        )
        $okMulti = $resMulti.Output.Contains("EDGE_OK:495_plugin_reload_A") -and
                   $resMulti.Output.Contains("EDGE_OK:495_plugin_reload_B") -and
                   (-not $resMulti.Output.Contains("undefined variable 'print'"))
        Write-Result -Name "495_plugin_multifile_reload" -Ok $okMulti -Details "Expected both scripts to execute with stdlib builtins available in each VM."
        if (-not $okMulti) { Write-Host $resMulti.Output }
    }

    # Negative parser/import checks
    Run-ExpectContains -Name "401_import_header_rules_fail" -ScriptArgs @((Edge-Path "_edgecases\401_import_header_rules_fail.cfs")) -Expected "Imports are only allowed in the header of the script"
    Run-ExpectContains -Name "403_import_cycle_fail" -ScriptArgs @((Edge-Path "_edgecases\403_import_cycle_fail.cfs")) -Expected "Import cycle detected:"
    Run-ExpectContains -Name "404_import_missing_class_fail" -ScriptArgs @((Edge-Path "_edgecases\404_import_missing_class_fail.cfs")) -Expected "Could not find export 'MissingClass'"
    Run-ExpectContains -Name "405_break_outside_loop_fail" -ScriptArgs @((Edge-Path "_edgecases\405_break_outside_loop_fail.cfs")) -Expected "break can only be used in loops"
    Run-ExpectContains -Name "406_continue_outside_loop_fail" -ScriptArgs @((Edge-Path "_edgecases\406_continue_outside_loop_fail.cfs")) -Expected "continue can only be used in loops"
    Run-ExpectContains -Name "407_return_in_out_fail" -ScriptArgs @((Edge-Path "_edgecases\407_return_in_out_fail.cfs")) -Expected "return can not be used in out-Block"
    Run-ExpectContains -Name "408_try_without_handlers_fail" -ScriptArgs @((Edge-Path "_edgecases\408_try_without_handlers_fail.cfs")) -Expected "try must have at least catch or finally"
    Run-ExpectContains -Name "409_self_inheritance_fail" -ScriptArgs @((Edge-Path "_edgecases\409_self_inheritance_fail.cfs")) -Expected "self inheritance not allowed"
    Run-ExpectContains -Name "410_enum_duplicate_value_fail" -ScriptArgs @((Edge-Path "_edgecases\410_enum_duplicate_value_fail.cfs")) -Expected "duplicate enum value"
    Run-ExpectContains -Name "411_class_member_duplicate_fail" -ScriptArgs @((Edge-Path "_edgecases\411_class_member_duplicate_fail.cfs")) -Expected "already declared in class"
    Run-ExpectContains -Name "412_duplicate_param_fail" -ScriptArgs @((Edge-Path "_edgecases\412_duplicate_param_fail.cfs")) -Expected "duplicate parameter name"
    Run-ExpectContains -Name "413_import_named_dll_fail" -ScriptArgs @((Edge-Path "_edgecases\413_import_named_dll_fail.cfs")) -Expected "named import from dll is not supported"
    Run-ExpectContains -Name "414_import_missing_dll_fail" -ScriptArgs @((Edge-Path "_edgecases\414_import_missing_dll_fail.cfs")) -Expected "plugin dll not found"
    Run-ExpectContains -Name "415_import_self_file_fail" -ScriptArgs @((Edge-Path "_edgecases\415_import_self_file_fail.cfs")) -Expected "self-import of"
    Run-ExpectContains -Name "416_import_missing_file_fail" -ScriptArgs @((Edge-Path "_edgecases\416_import_missing_file_fail.cfs")) -Expected "import path not found"
    Run-ExpectContains -Name "417_nested_class_static_fail" -ScriptArgs @((Edge-Path "_edgecases\417_nested_class_static_fail.cfs")) -Expected "nested classes cannot be static"
    Run-ExpectContains -Name "418_match_multiple_default_fail" -ScriptArgs @((Edge-Path "_edgecases\418_match_multiple_default_fail.cfs")) -Expected "multiple default case not allowed"
    Run-ExpectContains -Name "419_match_expr_duplicate_default_fail" -ScriptArgs @((Edge-Path "_edgecases\419_match_expr_duplicate_default_fail.cfs")) -Expected "duplicate '_' default arm in match expression"
    Run-ExpectContains -Name "420_const_assignment_fail" -ScriptArgs @((Edge-Path "_edgecases\420_const_assignment_fail.cfs")) -Expected "cannot assign to constant"
    Run-ExpectContains -Name "421_default_param_order_fail" -ScriptArgs @((Edge-Path "_edgecases\421_default_param_order_fail.cfs")) -Expected "non-default parameter cannot follow a default parameter"
    Run-ExpectContains -Name "422_named_arg_duplicate_fail" -ScriptArgs @((Edge-Path "_edgecases\422_named_arg_duplicate_fail.cfs")) -Expected "duplicate named argument 'b'"
    Run-ExpectContains -Name "423_named_arg_positional_after_named_fail" -ScriptArgs @((Edge-Path "_edgecases\423_named_arg_positional_after_named_fail.cfs")) -Expected "positional argument cannot follow named arguments"
    Run-ExpectContains -Name "424_named_arg_unknown_fail" -ScriptArgs @((Edge-Path "_edgecases\424_named_arg_unknown_fail.cfs")) -Expected "unknown named argument 'c'"
    Run-ExpectContains -Name "425_import_hidden_export_fail" -ScriptArgs @((Edge-Path "_edgecases\425_import_hidden_export_fail.cfs")) -Expected "Could not find export 'hidden'"
    Run-ExpectContains -Name "426_match_pattern_duplicate_binding_fail" -ScriptArgs @((Edge-Path "_edgecases\426_match_pattern_duplicate_binding_fail.cfs")) -Expected "duplicate match binding 'a'"
    Run-ExpectContains -Name "427_import_bare_export_module_fail" -ScriptArgs @((Edge-Path "_edgecases\427_import_bare_export_module_fail.cfs")) -Expected "bare import is not allowed for modules with explicit exports"
    Run-ExpectContains -Name "428_import_conflict_symbol_fail" -ScriptArgs @((Edge-Path "_edgecases\428_import_conflict_symbol_fail.cfs")) -Expected "duplicate function 'clash'"
    Run-ExpectContains -Name "429_rest_param_default_fail" -ScriptArgs @((Edge-Path "_edgecases\429_rest_param_default_fail.cfs")) -Expected "rest parameter cannot have a default value"
    Run-ExpectContains -Name "430_rest_param_not_last_fail" -ScriptArgs @((Edge-Path "_edgecases\430_rest_param_not_last_fail.cfs")) -Expected "rest parameter must be the last parameter"
    Run-ExpectContains -Name "431_spread_non_array_fail" -ScriptArgs @((Edge-Path "_edgecases\431_spread_non_array_fail.cfs")) -Expected "spread argument must be an array/list"
    Run-ExpectContains -Name "432_named_rest_param_fail" -ScriptArgs @((Edge-Path "_edgecases\432_named_rest_param_fail.cfs")) -Expected "rest parameter 'rest' cannot be passed as named argument"
    Run-ExpectContains -Name "433_spread_after_named_fail" -ScriptArgs @((Edge-Path "_edgecases\433_spread_after_named_fail.cfs")) -Expected "positional argument cannot follow named arguments"
    Run-ExpectContains -Name "434_const_destructure_requires_initializer_fail" -ScriptArgs @((Edge-Path "_edgecases\434_const_destructure_requires_initializer_fail.cfs")) -Expected "const destructuring declarations require an initializer"
    Run-ExpectContains -Name "435_destructure_invalid_target_fail" -ScriptArgs @((Edge-Path "_edgecases\435_destructure_invalid_target_fail.cfs")) -Expected "invalid destructuring target"
    Run-ExpectContains -Name "436_destructure_param_duplicate_binding_fail" -ScriptArgs @((Edge-Path "_edgecases\436_destructure_param_duplicate_binding_fail.cfs")) -Expected "duplicate destructuring binding 'a'"
    Run-ExpectContains -Name "437_foreach_destructure_duplicate_binding_fail" -ScriptArgs @((Edge-Path "_edgecases\437_foreach_destructure_duplicate_binding_fail.cfs")) -Expected "duplicate destructuring binding 'a'"
    Run-ExpectContains -Name "438_foreach_pair_duplicate_binding_fail" -ScriptArgs @((Edge-Path "_edgecases\438_foreach_pair_duplicate_binding_fail.cfs")) -Expected "duplicate destructuring binding 'i'"
    Run-ExpectContains -Name "439_namespace_member_duplicate_fail" -ScriptArgs @((Edge-Path "_edgecases\439_namespace_member_duplicate_fail.cfs")) -Expected "duplicate function 'x'"
    Run-ExpectContains -Name "440_namespace_import_inside_body_fail" -ScriptArgs @((Edge-Path "_edgecases\440_namespace_import_inside_body_fail.cfs")) -Expected "imports are not allowed inside namespace body"
    Run-ExpectContains -Name "441_namespace_reserved_name_fail" -ScriptArgs @((Edge-Path "_edgecases\441_namespace_reserved_name_fail.cfs")) -Expected "invalid symbol declaration name 'this'"
    Run-ExpectContains -Name "442_ambiguous_member_resolution_fail" -ScriptArgs @((Edge-Path "_edgecases\442_ambiguous_member_resolution_fail.cfs")) -Expected "invalid override for member 'token'"
    Run-ExpectContains -Name "443_namespace_root_conflict_fail" -ScriptArgs @((Edge-Path "_edgecases\443_namespace_root_conflict_fail.cfs")) -Expected "namespace root 'Core' conflicts with existing function 'Core'"
    Run-ExpectContains -Name "444_namespace_import_alias_conflict_fail" -ScriptArgs @((Edge-Path "_edgecases\444_namespace_import_alias_conflict_fail.cfs")) -Expected "namespace root 'Lib' conflicts with existing variable 'Lib'"
    Run-ExpectContains -Name "445_this_outside_method_fail" -ScriptArgs @((Edge-Path "_edgecases\445_this_outside_method_fail.cfs")) -Expected "invalid receiver usage 'this': only available in instance methods"
    Run-ExpectContains -Name "446_type_outside_method_fail" -ScriptArgs @((Edge-Path "_edgecases\446_type_outside_method_fail.cfs")) -Expected "invalid receiver usage 'type': only available in class methods"
    Run-ExpectContains -Name "447_super_without_base_fail" -ScriptArgs @((Edge-Path "_edgecases\447_super_without_base_fail.cfs")) -Expected "invalid receiver usage 'super': class has no base class"
    Run-ExpectContains -Name "448_outer_non_nested_fail" -ScriptArgs @((Edge-Path "_edgecases\448_outer_non_nested_fail.cfs")) -Expected "invalid receiver usage 'outer': only available in nested instance methods"
    Run-ExpectContains -Name "449_outer_static_nested_fail" -ScriptArgs @((Edge-Path "_edgecases\449_outer_static_nested_fail.cfs")) -Expected "invalid receiver usage 'outer': only available in nested instance methods"
    Run-ExpectContains -Name "450_receiver_assignment_fail" -ScriptArgs @((Edge-Path "_edgecases\450_receiver_assignment_fail.cfs")) -Expected "invalid receiver assignment 'this': receiver identifiers are read-only"
    Run-ExpectContains -Name "451_receiver_in_local_function_fail" -ScriptArgs @((Edge-Path "_edgecases\451_receiver_in_local_function_fail.cfs")) -Expected "invalid receiver usage 'this': only available in instance methods"
    Run-ExpectContains -Name "452_override_signature_mismatch_fail" -ScriptArgs @((Edge-Path "_edgecases\452_override_signature_mismatch_fail.cfs")) -Expected "incompatible override for method 'f'"
    Run-ExpectContains -Name "453_override_kind_mismatch_field_vs_method_fail" -ScriptArgs @((Edge-Path "_edgecases\453_override_kind_mismatch_field_vs_method_fail.cfs")) -Expected "invalid override for member 'f'"
    Run-ExpectContains -Name "454_override_kind_mismatch_static_instance_fail" -ScriptArgs @((Edge-Path "_edgecases\454_override_kind_mismatch_static_instance_fail.cfs")) -Expected "invalid override for member 'token'"
    Run-ExpectContains -Name "455_override_kind_mismatch_static_method_vs_instance_fail" -ScriptArgs @((Edge-Path "_edgecases\455_override_kind_mismatch_static_method_vs_instance_fail.cfs")) -Expected "invalid override for member 'ping'"
    Run-ExpectContains -Name "456_base_ctor_unknown_named_arg_fail" -ScriptArgs @((Edge-Path "_edgecases\456_base_ctor_unknown_named_arg_fail.cfs")) -Expected "invalid base constructor call in class 'Child': unknown named argument 'c'"
    Run-ExpectContains -Name "457_base_ctor_insufficient_args_fail" -ScriptArgs @((Edge-Path "_edgecases\457_base_ctor_insufficient_args_fail.cfs")) -Expected "invalid base constructor call in class 'Child': insufficient args for call"
    Run-ExpectContains -Name "458_base_ctor_positional_after_named_fail" -ScriptArgs @((Edge-Path "_edgecases\458_base_ctor_positional_after_named_fail.cfs")) -Expected "invalid base constructor call in class 'Child': positional argument cannot follow named arguments"
    Run-ExpectContains -Name "459_base_ctor_named_rest_fail" -ScriptArgs @((Edge-Path "_edgecases\459_base_ctor_named_rest_fail.cfs")) -Expected "invalid base constructor call in class 'Child': rest parameter 'rest' cannot be passed as named argument"
    Run-ExpectContains -Name "460_nested_constructor_missing_outer_fail" -ScriptArgs @((Edge-Path "_edgecases\460_nested_constructor_missing_outer_fail.cfs")) -Expected "nested constructor requires an outer instance argument '__outer'"
    Run-ExpectContains -Name "461_initializer_unknown_member_fail" -ScriptArgs @((Edge-Path "_edgecases\461_initializer_unknown_member_fail.cfs")) -Expected "unknown initializer member 'missing' for class 'Box'"
    Run-ExpectContains -Name "462_initializer_reserved_member_fail" -ScriptArgs @((Edge-Path "_edgecases\462_initializer_reserved_member_fail.cfs")) -Expected "invalid initializer member '__type': reserved member name"
    Run-ExpectContains -Name "463_this_static_member_access_fail" -ScriptArgs @((Edge-Path "_edgecases\463_this_static_member_access_fail.cfs")) -Expected "invalid instance member access 'token' in class 'C': member is static"
    Run-ExpectContains -Name "464_type_instance_member_access_fail" -ScriptArgs @((Edge-Path "_edgecases\464_type_instance_member_access_fail.cfs")) -Expected "invalid static member access 'token' in class 'C': member is instance"
    Run-ExpectContains -Name "465_class_static_access_instance_member_fail" -ScriptArgs @((Edge-Path "_edgecases\465_class_static_access_instance_member_fail.cfs")) -Expected "invalid static member access 'token' in class 'C': member is instance"
    Run-ExpectContains -Name "466_instance_assign_reserved_member_fail" -ScriptArgs @((Edge-Path "_edgecases\466_instance_assign_reserved_member_fail.cfs")) -Expected "invalid member assignment '__type': reserved member name"
    Run-ExpectContains -Name "467_static_assign_reserved_member_fail" -ScriptArgs @((Edge-Path "_edgecases\467_static_assign_reserved_member_fail.cfs")) -Expected "invalid member assignment '__base': reserved member name"
    Run-ExpectContains -Name "468_namespace_override_validation_fail" -ScriptArgs @((Edge-Path "_edgecases\468_namespace_override_validation_fail.cfs")) -Expected "invalid override for member 'ping'"
    Run-ExpectContains -Name "469_namespace_static_access_instance_member_fail" -ScriptArgs @((Edge-Path "_edgecases\469_namespace_static_access_instance_member_fail.cfs")) -Expected "invalid static member access 'token' in class 'C': member is instance"
    Run-ExpectContains -Name "470_reserved_member_alias_instance_fail" -ScriptArgs @((Edge-Path "_edgecases\470_reserved_member_alias_instance_fail.cfs")) -Expected "cannot assign to reserved runtime member '__type'"
    Run-ExpectContains -Name "471_reserved_member_alias_static_fail" -ScriptArgs @((Edge-Path "_edgecases\471_reserved_member_alias_static_fail.cfs")) -Expected "cannot assign to reserved runtime member '__base'"
    Run-ExpectContains -Name "472_reserved_ctor_param_fail" -ScriptArgs @((Edge-Path "_edgecases\472_reserved_ctor_param_fail.cfs")) -Expected "invalid parameter name '__type'"
    Run-ExpectContains -Name "473_private_member_access_fail" -ScriptArgs @((Edge-Path "_edgecases\473_private_member_access_fail.cfs")) -Expected "inaccessible instance member 'token' in class 'Vault': 'private' access"
    Run-ExpectContains -Name "474_private_static_access_fail" -ScriptArgs @((Edge-Path "_edgecases\474_private_static_access_fail.cfs")) -Expected "inaccessible member 'token' in class 'Vault': 'private' access"
    Run-ExpectContains -Name "475_protected_access_outside_fail" -ScriptArgs @((Edge-Path "_edgecases\475_protected_access_outside_fail.cfs")) -Expected "inaccessible instance member 'p' in class 'Base': 'protected' access"
    Run-ExpectContains -Name "476_protected_static_access_outside_fail" -ScriptArgs @((Edge-Path "_edgecases\476_protected_static_access_outside_fail.cfs")) -Expected "inaccessible static member 'ps' in class 'Base': 'protected' access"
    Run-ExpectContains -Name "477_runtime_alias_private_set_fail" -ScriptArgs @((Edge-Path "_edgecases\477_runtime_alias_private_set_fail.cfs")) -Expected "inaccessible instance member 'x' in class 'C': 'private' access"
    Run-ExpectContains -Name "478_runtime_alias_private_static_set_fail" -ScriptArgs @((Edge-Path "_edgecases\478_runtime_alias_private_static_set_fail.cfs")) -Expected "inaccessible static member 's' in class 'C': 'private' access"
    Run-ExpectContains -Name "479_duplicate_access_modifier_fail" -ScriptArgs @((Edge-Path "_edgecases\479_duplicate_access_modifier_fail.cfs")) -Expected "duplicate access modifier in class member declaration"
    Run-ExpectContains -Name "480_duplicate_static_modifier_fail" -ScriptArgs @((Edge-Path "_edgecases\480_duplicate_static_modifier_fail.cfs")) -Expected "duplicate 'static' modifier in class member declaration"
    Run-ExpectContains -Name "481_reserved_ctor_alias_instance_fail" -ScriptArgs @((Edge-Path "_edgecases\481_reserved_ctor_alias_instance_fail.cfs")) -Expected "cannot assign to reserved runtime member 'new'"
    Run-ExpectContains -Name "482_reserved_ctor_alias_static_fail" -ScriptArgs @((Edge-Path "_edgecases\482_reserved_ctor_alias_static_fail.cfs")) -Expected "cannot assign to reserved runtime member 'new'"
    Run-ExpectContains -Name "483_override_visibility_narrower_instance_fail" -ScriptArgs @((Edge-Path "_edgecases\483_override_visibility_narrower_instance_fail.cfs")) -Expected "incompatible visibility override for member 'f'"
    Run-ExpectContains -Name "484_override_visibility_narrower_static_fail" -ScriptArgs @((Edge-Path "_edgecases\484_override_visibility_narrower_static_fail.cfs")) -Expected "incompatible visibility override for member 'token'"
    Run-ExpectContains -Name "485_constructor_private_external_fail" -ScriptArgs @((Edge-Path "_edgecases\485_constructor_private_external_fail.cfs")) -Expected "inaccessible member 'new' in class 'Vault': 'private' access"
    Run-ExpectContains -Name "486_constructor_protected_external_fail" -ScriptArgs @((Edge-Path "_edgecases\486_constructor_protected_external_fail.cfs")) -Expected "inaccessible member 'new' in class 'Base': 'protected' access"
    Run-ExpectContains -Name "487_delete_missing_index_var_fail" -ScriptArgs @((Edge-Path "_edgecases\487_delete_missing_index_var_fail.cfs")) -Expected "undefined variable 'missing'"
    Run-ExpectContains -Name "488_delete_missing_var_fail" -ScriptArgs @((Edge-Path "_edgecases\488_delete_missing_var_fail.cfs")) -Expected "undefined variable 'missing'"
    Run-ExpectContains -Name "489_index_non_integer_fail" -ScriptArgs @((Edge-Path "_edgecases\489_index_non_integer_fail.cfs")) -Expected "index must be an integer"
    Run-ExpectContains -Name "490_slice_non_integer_bound_fail" -ScriptArgs @((Edge-Path "_edgecases\490_slice_non_integer_bound_fail.cfs")) -Expected "index must be an integer"
    Run-ExpectContains -Name "491_dict_literal_intrinsic_key_fail" -ScriptArgs @((Edge-Path "_edgecases\491_dict_literal_intrinsic_key_fail.cfs")) -Expected "reserved for dictionary intrinsics"
    Run-ExpectContains -Name "492_dict_push_intrinsic_key_fail" -ScriptArgs @((Edge-Path "_edgecases\492_dict_push_intrinsic_key_fail.cfs")) -Expected "reserved for dictionary intrinsics"
    Run-ExpectContains -Name "493_slice_set_string_immutable_fail" -ScriptArgs @((Edge-Path "_edgecases\493_slice_set_string_immutable_fail.cfs")) -Expected "SLICE_SET on string. Strings are immutable."
    Run-ExpectContains -Name "494_import_invalid_dll_format_fail" -ScriptArgs @((Edge-Path "_edgecases\494_import_invalid_dll_format_fail.cfs")) -Expected "failed to load plugin dll"
    Run-ExpectContains -Name "506_plugin_register_failfast_fail" -ScriptArgs @((Edge-Path "_edgecases\506_plugin_register_failfast_fail.cfs")) -Expected "failed to load plugin dll"
    Run-ExpectContains -Name "507_defer_outside_block_fail" -ScriptArgs @((Edge-Path "_edgecases\507_defer_outside_block_fail.cfs")) -Expected "defer is only allowed inside '{...}' blocks"
    Run-ExpectContains -Name "508_interface_visibility_fail" -ScriptArgs @((Edge-Path "_edgecases\508_interface_visibility_fail.cfs")) -Expected "non-public visibility"
    Run-ExpectContains -Name "509_interface_extends_class_fail" -ScriptArgs @((Edge-Path "_edgecases\509_interface_extends_class_fail.cfs")) -Expected "is a class"
    Run-ExpectContains -Name "497_yield_outside_function_fail" -ScriptArgs @((Edge-Path "_edgecases\497_yield_outside_function_fail.cfs")) -Expected "invalid top-level statement Yield"
    Run-ExpectContains -Name "498_async_without_func_fail" -ScriptArgs @((Edge-Path "_edgecases\498_async_without_func_fail.cfs")) -Expected "expected 'func' after 'async'"
    Run-ExpectContains -Name "499_await_without_async_fail" -ScriptArgs @((Edge-Path "_edgecases\499_await_without_async_fail.cfs")) -Expected "await can only be used in async function statements"
    Run-ExpectContains -Name "500_yield_without_async_fail" -ScriptArgs @((Edge-Path "_edgecases\500_yield_without_async_fail.cfs")) -Expected "yield can only be used in async function statements"
    Run-ExpectContains -Name "503_await_position_fail" -ScriptArgs @((Edge-Path "_edgecases\503_await_position_fail.cfs")) -Expected "503_await_position_fail.cfs:4:1"
    Run-ExpectContains -Name "504_async_func_expr_position_fail" -ScriptArgs @((Edge-Path "_edgecases\504_async_func_expr_position_fail.cfs")) -Expected "504_async_func_expr_position_fail.cfs:2:9"

    # CLI behavior checks
    if ("701_cli_set_buffer" -like $NameFilter) {
        $resCliBuffer = Invoke-Cfgs -CfgsArgs @(
            "-s", "buffer", "321",
            (Edge-Path "_edgecases\601_compile_binary_smoke.cfs")
        )
        $okCliBuffer = $resCliBuffer.Output.Contains("[SETTINGS] Debug-Buffer set to 321")
        Write-Result -Name "701_cli_set_buffer" -Ok $okCliBuffer -Details "Expected buffer setting confirmation."
        if (-not $okCliBuffer) { Write-Host $resCliBuffer.Output }
    }

    if ("702_cli_set_ansi" -like $NameFilter) {
        $resCliAnsi = Invoke-Cfgs -CfgsArgs @(
            "-s", "ansi", "0",
            (Edge-Path "_edgecases\601_compile_binary_smoke.cfs")
        )
        $okCliAnsi = $resCliAnsi.Output.Contains("Ansi-Mode disabled")
        Write-Result -Name "702_cli_set_ansi" -Ok $okCliAnsi -Details "Expected ansi mode setting confirmation."
        if (-not $okCliAnsi) { Write-Host $resCliAnsi.Output }
    }

    if ("703_cli_params_ignore_mode" -like $NameFilter) {
        $resCliParams = Invoke-Cfgs -CfgsArgs @(
            (Edge-Path "_edgecases\601_compile_binary_smoke.cfs"),
            "-p", "foo", "bar"
        )
        $okCliParams = (-not $resCliParams.Output.Contains("Could not load the script-file : 'foo'")) -and
                       (-not $resCliParams.Output.Contains("Could not load the script-file : 'bar'")) -and
                       (-not $resCliParams.Output.Contains("Invalid command"))
        Write-Result -Name "703_cli_params_ignore_mode" -Ok $okCliParams -Details "Expected -p args to be ignored as script files."
        if (-not $okCliParams) { Write-Host $resCliParams.Output }
    }

    # REPL redirected-input checks
    if ("704_repl_redirected_exit" -like $NameFilter) {
        $resReplExit = Invoke-Cfgs -CfgsArgs @() -StdinText "exit`n"
        $okReplExit = $resReplExit.Output.Contains("[ Configuration-Language ]") -and
                      (-not $resReplExit.Output.Contains("InvalidOperationException : Cannot read keys")) -and
                      (-not $resReplExit.Output.Contains("[Error-Id:"))
        Write-Result -Name "704_repl_redirected_exit" -Ok $okReplExit -Details "Expected clean REPL exit on redirected stdin."
        if (-not $okReplExit) { Write-Host $resReplExit.Output }
    }

    if ("705_repl_redirected_commands" -like $NameFilter) {
        $stdin = "debug`nansi`nbuffer:77`nhelp`nexit`n"
        $resReplCmd = Invoke-Cfgs -CfgsArgs @() -StdinText $stdin
        $okReplCmd = $resReplCmd.Output.Contains("Debug mode is now") -and
                     $resReplCmd.Output.Contains("Ansi-Mode is") -and
                     $resReplCmd.Output.Contains("[SETTINGS] Debug-Buffer set to 77") -and
                     $resReplCmd.Output.Contains("[===================HELP============================]") -and
                     (-not $resReplCmd.Output.Contains("InvalidOperationException : Cannot read keys")) -and
                     (-not $resReplCmd.Output.Contains("[Error-Id:"))
        Write-Result -Name "705_repl_redirected_commands" -Ok $okReplCmd -Details "Expected redirected REPL commands to work without ReadKey exceptions."
        if (-not $okReplCmd) { Write-Host $resReplCmd.Output }
    }

    # Error tracking checks
    $trackingLogPath = Join-Path $edgeDir "_tmp_error_tracking.jsonl"
    if (Test-Path -LiteralPath $trackingLogPath) {
        Remove-Item -LiteralPath $trackingLogPath -Force
    }

    if ("501_error_tracking_enabled" -like $NameFilter) {
        $resTrackOn = Invoke-Cfgs -CfgsArgs @(Edge-Path "_edgecases\501_error_tracking_probe.cfs") -EnvVars @{
            CFGS_ERROR_TRACKING_PATH = $trackingLogPath
            CFGS_DISABLE_ERROR_TRACKING = $null
        }

        $hasErrorId = $resTrackOn.Output.Contains("[Error-Id:")
        $logExists = Test-Path -LiteralPath $trackingLogPath
        $hasEntry = $false
        $sourceOk = $false

        if ($logExists) {
            $logLines = @(Get-Content -LiteralPath $trackingLogPath)
            if ($logLines.Count -gt 0) {
                $hasEntry = $true
                try {
                    $last = $logLines[-1] | ConvertFrom-Json
                    $sourceName = [string]$last.SourceName
                    $sourceOk = $sourceName -like "*501_error_tracking_probe.cfs*"
                } catch {
                    $sourceOk = $false
                }
            }
        }

        $okTrackOn = $hasErrorId -and $logExists -and $hasEntry -and $sourceOk
        Write-Result -Name "501_error_tracking_enabled" -Ok $okTrackOn -Details "Expected Error-Id output and log entry with matching SourceName."
        if (-not $okTrackOn) { Write-Host $resTrackOn.Output }
    }

    if ("502_error_tracking_disabled" -like $NameFilter) {
        $beforeCount = 0
        if (Test-Path -LiteralPath $trackingLogPath) {
            $beforeCount = @(Get-Content -LiteralPath $trackingLogPath).Count
        }

        $resTrackOff = Invoke-Cfgs -CfgsArgs @(Edge-Path "_edgecases\501_error_tracking_probe.cfs") -EnvVars @{
            CFGS_ERROR_TRACKING_PATH = $trackingLogPath
            CFGS_DISABLE_ERROR_TRACKING = "1"
        }

        $afterCount = 0
        if (Test-Path -LiteralPath $trackingLogPath) {
            $afterCount = @(Get-Content -LiteralPath $trackingLogPath).Count
        }

        $hasNoErrorId = -not $resTrackOff.Output.Contains("[Error-Id:")
        $countStable = $afterCount -eq $beforeCount
        $okTrackOff = $hasNoErrorId -and $countStable

        Write-Result -Name "502_error_tracking_disabled" -Ok $okTrackOff -Details "Expected no Error-Id and unchanged log entry count."
        if (-not $okTrackOff) { Write-Host $resTrackOff.Output }
    }

    # Removed binary/compile CLI checks
    if ("601_compile_flag_removed" -like $NameFilter) {
        $resNoCompile = Invoke-Cfgs -CfgsArgs @("-c", (Edge-Path "_edgecases\601_compile_binary_smoke.cfs"))
        $okNoCompile = $resNoCompile.Output.Contains("Invalid command -c.")
        Write-Result -Name "601_compile_flag_removed" -Ok $okNoCompile -Details "Expected '-c' to be rejected as invalid command."
        if (-not $okNoCompile) { Write-Host $resNoCompile.Output }
    }

    if ("602_binary_flag_removed" -like $NameFilter) {
        $resNoBinary = Invoke-Cfgs -CfgsArgs @("-b", (Edge-Path "_edgecases\601_compile_binary_smoke.cfs"))
        $okNoBinary = $resNoBinary.Output.Contains("Invalid command -b.")
        Write-Result -Name "602_binary_flag_removed" -Ok $okNoBinary -Details "Expected '-b' to be rejected as invalid command."
        if (-not $okNoBinary) { Write-Host $resNoBinary.Output }
    }

    if ("603_cfb_file_rejected" -like $NameFilter) {
        $resCfb = Invoke-Cfgs -CfgsArgs @((Edge-Path "_edgecases\603_removed_binary_placeholder.cfb"))
        $okCfb = $resCfb.Output.Contains("Binary .cfb support has been removed.")
        Write-Result -Name "603_cfb_file_rejected" -Ok $okCfb -Details "Expected '.cfb' files to be rejected."
        if (-not $okCfb) { Write-Host $resCfb.Output }
    }
}
catch {
    $script:AnyFailed = $true
    Write-Host "[FAIL] Runner exception: $($_.Exception.Message)" -ForegroundColor Red
}

if ($script:AnyFailed) {
    Write-Host "[DONE] Edge suite finished with failures." -ForegroundColor Red
    exit 1
}

Write-Host "[DONE] Edge suite passed." -ForegroundColor Green
exit 0
