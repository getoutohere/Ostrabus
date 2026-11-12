# Saved inventory dependency audit

Read-only diagnostic for a named save ZIP and selected hulls. Streams layout
records and the main saved-CO array instead of loading the entire main save JSON.
Outputs unresolved dependency chains and their existing saved CO records; verifies
the ZIP's SHA-256 hash before and after. Never modifies saves or installed assets.

```powershell
dotnet run --project Tools/InventoryLoadAudit -c Release -- <save.zip> obj/inventory-audit.json OKLG_UNK H-3UTJ
```

The dependency model estimates loose-item damage thresholds from base definitions
and overlays. It does not emulate installed-item destruction chains, Unity
creation or native slot-placement failures. Validate gameplay conclusions against
native code and loader fixtures. See [the diagnosed case and patch](../../MiscBugFixes/SAVED-INVENTORY-PARENTS.md).
