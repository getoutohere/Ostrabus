# Plot-reference audit

November 10, 2026. Investigation only. **No production plot patches or asset
overrides implemented.** If implemented later, these corrections belong in a
native JSON mod package, not additional plot-behavior Harmony hooks.

`Tools/PlotAudit` runs shipped `JsonInteractionOverride.Generate` and
`JsonPlotBeatOverride.Generate` under bundled Mono, with relevant template
dependencies and native ordering. It records both original phase graphs and
in-memory candidate reference corrections. Installed assets and saves are not
modified. The native `CondTrigger.Triggered` method checks the gates discussed
below; Unity object equality and parent validation are fixture boundaries.

## Lonesome / Lonesome Polecat: complete authored exits

Three phase references incorrectly use `PLOT_Lonesome_Stage02ToEnd`,
`PLOT_Lonesome_Stage03ToEnd`, and `PLOT_Lonesome_Stage04ToEnd`. Their defined beat
names start with `Beat_`, not `PLOT_`.

All three intended beats exist. Their native phase increments (3, 2 and 1)
reach phase 5, whose existing `Beat_Lonesome_End` advances beyond the plot and
applies `CONDPLOT_Lonesome_End`. The latter clears the started flag, accumulated
Lonesome stat and all four stage flags. Trigger interactions and player actions
resolve, including ordinary progression and backwards transitions.

Native gate tests confirm that each recovery case (`DcIntimacy01`,
`DcIntimacy02`, or `IsChaste`) satisfies `CTPLOT_Lonesome_End`.

Conclusion: a small asset-only reference correction is justified. The plot is
not universally impossible in vanilla: its stage-1 exit already works. The bad
references remove recovery exits from later stages and can leave it active after
the relevant need has recovered. No new ending, rewards or story policy needed.

## Wrongfully Accused / Insurance Fraud: defer a simple patch

Native generation produces `WrongfullyAccusedCrimMIDStartReverse` and
`WrongfullyAccusedCrimMIDTurnInReverse`, but the six-phase plot references names
without `Reverse`. This blocks structural progression at phase 3. Correcting both
references produces a graph with an end and all actions resolve, but that is not
proof of a playable, coherent completion path.

The later sequence retains incompatible template bindings:

- The supposed corpse-investigation action `PLOTWrongfullyAccusedCrimMIDTurnIn`
  still uses `strTargetPoint=talk` and `TIsValidSocialTarget`. The native gate
  explicitly rejects `IsDead`; a human-corpse fixture confirms rejection.
- The phase-4 trigger receives `[protag],[contact],[target]` and saves its third
  object as `[deadbody]`. This aliases the existing `[target]`, originally chosen
  using `RandomAdultGigTargetLiving`, rather than finding a separate corpse.
- That trigger additionally requires its third object to be on
  `PlayerCurrentShip`, despite the narrative directing the player to the purported
  corpse's ship. The reviewed chain contains no authored step that resolves this
  mismatch or supplies the separate corpse.

Conclusion: do not ship just the naming fixes or invent a parallel C# plot
controller. They would expose further broken/incomplete authored mechanics.
Any further work needs a deliberate asset-level design review of roles, corpse
selection and interaction eligibility. Structural phase reachability alone is
insufficient; this audit does not claim the Insurance Fraud plot is playable
after two string changes.

Local ignored evidence: `obj/ConsoleIssues/plot-audit.json`.
