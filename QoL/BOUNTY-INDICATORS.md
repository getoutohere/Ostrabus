# Bounty availability indicators

The existing bounty list/detail hooks in `Patch.cs` display MIA when the target
CO is missing or its current ship is `OKLG_UNK`, the native offscreen holding pool.
The list displays `(MIA)`; details retain the advertised region with an MIA suffix.
An unconscious or dead target elsewhere is not marked missing solely for that state.

This is presentation only. It does not move anyone, cancel contracts, alter rewards,
change police behavior or prevent the bounty manager from placing targets later.
Availability is reevaluated whenever native code initializes the corresponding UI.
No new hooks, background state or saved data are added. Custom builds retain the
existing bounty UI feature selection.

The Jonah Walter case is a native police arrest: he was present on VNCA in `fine`,
officer Lateef Clay shot him unconscious and completed the Venus arrest shortly
afterward, and `steal` records him in `OKLG_UNK` with his contract still active.
`PLGAICrimeVenusLEOArrestEndAI` explicitly selects that destination; native
`BountyManager.AllowedToLeaveShip` currently prevents his replacement while
unconscious. The indicator reports his unavailability without undoing the arrest.
