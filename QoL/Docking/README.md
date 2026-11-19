# Single-port docking shortcut

Skip the target-port selection menu in the player's comms docking conversation
when the game offers exactly one eligible port for the player's active docking
port. This is an optional QoL shortcut, enabled by default. Zero or multiple
choices retain the normal menu; the shortcut does not change the player's active
port or select a different docking facility.

The Mod Settings PDA app exposes **Single-port docking shortcut**, saved in the
normal BepInEx configuration:

```ini
[QoL.Docking]
AutoSelectSinglePort = true
```

PDA changes apply immediately to subsequent docking replies. With the option off,
the native port-selection menu remains. No restart is needed for PDA changes and
no migration or custom game-save fields are introduced.

## Native flow and scope

Reviewed against Ostranauts 1.1.0.3. The asset definitions
`SHIPMultiDockSelectAllow` and `SHIPMultiDockSelectForcedAllow` lead to
`them#SHIPDock` and `them#SHIPForceDockNoPilot`. In C#,
`Ostranauts.ShipGUIs.MFD.MFDComms.BuildInvIas` enumerates compatible target/own-port
pairs and filters them to the player's active own port. It constructs the exact
interaction and its `PortID` payload.

One postfix on `MFDComms.OnUIRefresh` examines the newly received port prompt
after that native list has been built. For exactly one matching, correctly bound
port interaction, it calls the existing private `ApplyInteraction(0)`. Native
interaction validation, message sending, waiting display, port payload and
conversation cleanup run as if the player clicked that option. Clearance replies,
range/fit checks, refusal, forced-docking consequences, clamp engagement and AI
behavior remain native.

The shortcut recognizes only the two authored port prompts. Ordinary replies,
public ATC listening pages, unrelated facilities, malformed/unbound options and
other menus are excluded. Reopening or rebuilding historical conversation options
does not send a request. Target substations/facilities are accepted only within
the selected target's ID namespace. A weak message-keyed marker prevents two
open MFDs from submitting the same delivered prompt twice; it retains no ships,
pages or conversations. A synchronous guard also prevents reentrant submission.
There is no update loop or fleet scan.

The desired change belongs to UI choice handling: eligible-port expansion and
the live CFG switch are not expressed by the authored conversation JSON. No
asset overrides or replacement clearance policy are needed. This follows the
native generated option rather than guessing a port from physical port count.

Custom builds select **single-port-docking** in the QoL category. The PDA setting
is omitted when the feature is absent; the feature can also run without the PDA
app, using its CFG setting. It has no feature dependencies.

## Validation

The bundled-Mono harness executes native `OnUIRefresh`, `BuildInvIas` and
`ApplyInteraction` with the production postfix. Fixtures supply compatible port
pairs, captain/interaction lookup, trigger outcomes and message delivery, and
suppress Unity UI rendering. They verify zero/one/multiple choices for normal and
forced docking, active-own-port filtering, native port payload, trigger rejection,
live disable/enable, public-channel and unrelated-message exclusions, target
facility replies, duplicate MFD notifications and historical-list construction.
The installed JSON prompt links are checked directly. Full Unity rendering and
the subsequent remote clearance/physical docking are not simulated by this test.

```powershell
dotnet build Tests/SinglePortDockingCheck -c Release
dotnet Tests/SaveJsonCompatibility/Host/bin/Debug/net8.0/MonoHost.dll Tests/SinglePortDockingCheck/bin/Release/netstandard2.1/SinglePortDockingCheck.dll
```

The PDA settings harness verifies default, saved lifetime and both toggle
directions. Custom-builder checks cover the isolated shortcut and its combination
with the PDA app, including filtered settings and native patch binding.
