# Plot asset audit

Offline native-generation and condition-gate probe for Lonesome and Insurance
Fraud. Loads local asset tables recursively, executes native interaction overrides
before the dependent native beat overrides, and records structural reachability
before and after candidate reference corrections. These candidates exist only in
the diagnostic process. No installed files or saves are edited.

```powershell
dotnet build Tools/PlotAudit -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tools/PlotAudit/bin/Release/netstandard2.1/PlotAudit.dll obj/plot-audit.json
```

Requires the installed game and bundled Mono host. No running game or live hooks.
The phase graph does not prove that a player can satisfy a branch's roles and
conditions. See [the findings and limitations](../../MiscBugFixes/PLOT-REFERENCE-AUDIT.md).
