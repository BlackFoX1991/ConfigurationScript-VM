# CFGS Language Review Fix Status

Current objective: close the full language review findings and verify the repository after each major fix block.

## Findings

| ID | Area | Status | Acceptance criteria |
| --- | --- | --- | --- |
| R1 | Embedding API concurrency | Done | Public `CfgsScript` runtime operations are serialized per script instance and covered by `HostingSerializesConcurrentInvocations`. |
| R2 | Async child VM policy inheritance | Done | Hot-start async child VMs inherit security/configuration flags from the parent VM and `AsyncHotStartInheritsVmPolicy` proves disabled native interop stays disabled. |
| R3 | VM-scoped working directory | Done | `VM.WorkingDirectory`/`ResolveWorkspacePath` isolate workspace handling from the process current directory; covered by `WorkspaceIsVmScoped` and edgecases. |
| R4 | Package lockfile integrity | Done | Plugin DLLs restored through `.cfgs/plugins` are checked against `cfgs.lock.json` before loading; covered by `PluginLoadRejectsMismatchedPackageLockHash`. |
| R5 | Structured diagnostic codes | Done | Parser/lexer/compiler/runtime diagnostics can carry explicit stable codes; message matching is fallback behavior. Covered by `HostingPreservesStructuredDiagnosticCode`. |
| R6 | Standard-library namespace policy | Done | Removed old global/compat aliases for encoding, regex, json, math, and fs/file surfaces; runtime, LSP, edgecases, and docs now use namespace calls. |
| R7 | Formatter/LSP drift hardening | Done | Added LSP completion drift coverage for removed standard-library aliases and updated completion metadata. |
| R8 | Documentation sample/import fixes | Done | Documentation no longer points to missing sample folders and import resolution docs match the spec. |

## Verification

Final verification after fixes:

- `dotnet build -v minimal CFGS_VM.sln /p:EnableSourceControlManagerQueries=false`: passed, 0 warnings, 0 errors
- `dotnet run --project CFGS.Tooling.Tests\CFGS.Tooling.Tests.csproj --no-build`: 16/16 passed
- `dotnet run --project CFGS.Lsp.Tests\CFGS.Lsp.Tests.csproj --no-build`: 8/8 passed
- `powershell -ExecutionPolicy Bypass -File CFGS_NE\Edgecases\run_all_edgecases.ps1`: 210/210 passed

## Notes

- Worktree had existing modifications before this status file was created. Treat them as current user/project state.
- All review findings tracked in this file are completed and verified.
