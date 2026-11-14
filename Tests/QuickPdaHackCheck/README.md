# Assets-only PDA hacking checks

Run from the repository root:

```powershell
dotnet build Tests/QuickPdaHackCheck -c Release --nologo
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release -- Tests/QuickPdaHackCheck/bin/Release/netstandard2.1/QuickPdaHackCheck.dll
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/QuickPdaHackCheck/InstallCheck.ps1
```

The harness loads actual `QoL/PdaHacking/Assets` JSON through shipped
`DataHandler.JsonToData`, executes native overrides and calls the complete native
quick-action builder. No menu/gameplay hook or substitute menu result is installed.
No plugin reference or production C# is linked.

176 checks cover metadata, C#-free payload, unchanged PDA fields outside the action
list, inherited effects/duration, exclusive skill-dependent choices, incapacitation,
target conditions, controlled-character restriction and click-time validation.

Fixtures replace Unity equality/component lookup, selected-character lookup,
localization, parent validation and movement. Combat/job/plot/work state is empty;
pickup/drop definitions are placeholders and the recovery indicator is suppressed.
Native loading, cloning, overrides, participant/condition checks and menu enumeration
remain active. Full rendering, modeswitch content transfer and save reload are
not claimed as executed by this harness.

The installer fixture checks default/custom locations, hashes, load-order metadata,
idempotence, backups and selection/omission. `CustomBuilder/Test-Installer.ps1`
also checks independent Settings/hacking profiles, native checksums and rollback
of assets and enablement after a later plugin-copy failure.
