# Native station migration integration checks

References the built standalone plugin and executes under the game's bundled
Mono. Binds its actual loader transpiler and inspects the transformed native IL:
exactly the system-init and kiosk-respawn call sites change, with unrelated
respawn paths retained. Exercises all eleven physical section decisions through the real
adapter, native marker/idempotence/removal gates, socket rotation against native
`TileUtils.RotateTilesCW`, saved-definition resolution and native `SpawnItems`
admission. Non-target ship identity and docking/kinematic/crew/room/zone references
are checked for preservation. Authored target/template bindings must agree with
native world spawn assets. The two parent interiors must remain unchanged, the
two substations must receive their kiosks, and a previous global completion
marker must not suppress the correction or recreate a deliberately removed
completed kiosk.

```powershell
dotnet build Tests/StationMigrationNativeCheck -c Release
dotnet Tests/SaveJsonCompatibility/Host/bin/Release/net8.0/MonoHost.dll Tests/StationMigrationNativeCheck/bin/Release/netstandard2.1/StationMigrationNativeCheck.dll <old-data> <new-data>
```

System-init and object-factory fixture prefixes stop before Unity scene/object
allocation. This proves loader admission and the reviewed native gates, not
complete scene reconstruction, actual game-save replay or a working trade UI.
The companion is also checked with `Tests/PatchBindingCheck --companion` against
the full Ostrabus plugin. Nothing connects to or alters a running game.
