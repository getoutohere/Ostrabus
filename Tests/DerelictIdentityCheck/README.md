# Native derelict-name and registration guards

```powershell
dotnet build Tests/DerelictIdentityCheck -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release -- Tests/DerelictIdentityCheck/bin/Release/netstandard2.1/DerelictIdentityCheck.dll
```

This runs native 1.1.0.4 methods under bundled Mono. First it reproduces the
unpatched name overwrite and deferred-ID overwrite, then applies the two actual
production patch classes and checks their behavior. Assets for a small Ryokka
and a large Testudo must still request `$TEMPLATE` names.

Coverage includes name/ID separation, preserved wreck conversion effects,
asteroid and authored station conversion, all asteroid finally exits, unchanged
AI target descriptions, unchanged collision-free RNG draws, loaded/deferred
keys including null values, default and explicit prefixes, manufactured-station
and asteroid-field prefixes, repeated collisions, actual creation preserving
both registry entries, explicit-ID restoration and conversion, warning contents
and counts, and rejection of unexpected IL layouts.

Scene allocation returns an already named ship at the native SpawnShip boundary;
Unity object comparison, visibility and condition writes are fixtures. Scripted
Unity RNG draws make collisions deterministic; the native allocation and
StellarDerelict/CreateStellarObject registry writes execute. Warning capture is
at Unity Debug.LogWarning, which the native player console subscribes to; no
rendered console or full Unity loading scene is claimed. No other Ostrabus
behavior patches are loaded and no live/save changes are made.

For the independent native/no-op-Harmony finally-emission reproducer:

```powershell
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release -- Tests/DerelictIdentityCheck/bin/Release/netstandard2.1/DerelictIdentityCheck.dll --finally-control
```

This diagnostic control deliberately reproduces the broken exit with an identity
transpiler and neither production repair installed. If Harmony/native code later
changes, review whether the explicit finally continuation is still necessary.
The bundled host's missing Unity Debug.Break diagnostic does not prevent these
assertions from executing; process exit status and the final result are checked.

Current 1.1.0.4 receipt: 349 regression assertions and 24 optional control
assertions. Compiler output has no errors or warnings.
