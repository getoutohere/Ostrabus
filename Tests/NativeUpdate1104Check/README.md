# Native 1.1.0.3 / 1.1.0.4 controls

Build with `dotnet build Tests/NativeUpdate1104Check -c Release`, then run through
`Tests/SaveJsonCompatibility/Host` with `current` for 1.1.0.4 or `legacy` for
1.1.0.3. The harness verifies the loaded native assembly's exact SHA-256 before
executing it. For the legacy control, put the harness and its dependencies beside
the preserved 1.1.0.3 `Assembly-CSharp.dll` in an ignored fixture directory; the
Mono host can still use the installed, unchanged runtime.

Native `CondOwner.AddReplyThread` and `CleanupReplies` execute without any
Ostrabus patch. Sender clocks behind, aligned with and ahead of the recipient
demonstrate the old immediate-expiry defect and the new recipient-clock stamp.
Duplicate replacement and the native 30-second expiry boundary are checked.
The actual private `GasContainer.AtmoSafe` getter verifies oxygen 20 -> 22,
with nitrogen 80 and temperature 297 retained.

Actor identities, selection and Unity equality are fixtures. Disposed isolated
clocks provide fixed epochs; no scheduler, full conversation UI or Unity scene
is exercised. Results: 16 legacy checks and 18 current checks.
