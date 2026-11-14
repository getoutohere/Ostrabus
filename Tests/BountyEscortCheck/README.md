# Native bounty escort permission checks

From the repository root:

```powershell
dotnet build Tests/BountyEscortCheck -c Release --nologo -v quiet
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/BountyEscortCheck/bin/Release/netstandard2.1/BountyEscortCheck.dll
```

Links the production patch and runs native `HasShoreLeave`,
`HasAirlockPermission` and `Tile.IsWalkable` from the installed game assembly.
The baseline reproduces the airlock veto after successful bounty custody; the
patch opens the permission gate while preserving roster flags and other tile
obstacles. It also checks the shipped arrest/control assets, 256 permission
combinations, removal of custody and missing-roster behavior: 531 checks total.

Unity/scene services, condition/ship/company lookup, pressure/gravity and the
emergency trigger have explicit fixtures. This does not run a complete social
arrest interaction, timer expiration or bounty hand-in. See the
[repair scope and evidence](../../MiscBugFixes/BOUNTY-ESCORT.md).

Run bundled-Mono harnesses sequentially: they share the host's staging directory.
