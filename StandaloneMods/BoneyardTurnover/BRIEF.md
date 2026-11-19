# Boneyard Turnover

**Draft proposal for feedback.** An optional standalone simulation mod for
Ostranauts.

## Purpose

The newer regional boneyards are largely fixed populations. An unlucky initial
selection can leave a field without the new and unusual ship designs or equipment that
make a long trip worthwhile, and salvaging the field does not ordinarily provide
another selection later.

This mod would represent gradual removal and arrival of wrecks while the player
is elsewhere. Returning after a substantial absence would reveal a mixture of
familiar wrecks and new salvage opportunities. It would model the result of
turnover without creating a delivery fleet or continuously simulating that work.

This is an intentional, opinionated game design alteration.

## Proposed player experience

When the player leaves a supported boneyard's ATC region, the mod records the
field's eligible wrecks and the departure time. When the player returns, it
replaces a random subset with newly generated wrecks from that field's normal
ship-template pool.

The replacement fraction follows a **seven-day half-life**:

`replacement fraction = 1 - 2^(-days away / 7)`

All durations are **in-game time spent outside the ATC region**.

| Time away | Expected fraction replaced |
| --- | ---: |
| No absence | 0% |
| 1 day | 9.4% |
| 3.5 days | 29.3% |
| 7 days | 50% |
| 14 days | 75% |
| 21 days | 87.5% |
| 28 days | 93.75% |

For example, returning to 20 eligible wrecks after seven days would replace 10
randomly selected wrecks. At one day, the expected count is about 1.89: the mod
would replace one or two, with probabilities that preserve that average.
Fractional counts are randomly rounded rather than always rounded down.

There is no minimum waiting period, guaranteed rare item, guaranteed unseen
design or scheduled complete-field reset. Random turnover can nevertheless
replace every eligible wreck, particularly in a small field after a long absence.
A particular old wreck might survive several visits. For an original wreck,
short repeated absences give the same expected survival probability as one
equivalent uninterrupted absence, assuming it stays eligible. More frequent
visits do let the player inspect intermediate replacements.

Time spent remaining inside the region does not refresh its field. Reloading a
save does not count as a departure, advance the timer or grant another roll.

## Scope

The intended scope is the static, asset-defined fields around Mercury, Luna,
Phobos, Ganymede (JFTS), Titan (SVIR) and Europa, together with MSUZ, subject
to verification of each field's actual region, anchor and current environment.
Earth's derelict field is intentionally excluded to preserve its restricted-access role.
Authored blockade or story variants must be handled as variants of their actual
fields, not as additional populations to create alongside them.
