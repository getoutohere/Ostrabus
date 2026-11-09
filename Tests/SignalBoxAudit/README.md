# Vanilla signal-box component audit

See [the findings](../../MiscBugFixes/SIGNAL-BOX-AUDIT.md) for reproductions,
source locations, consequences and fix boundaries. Run instructions are there.
The harness executes the installed vanilla methods with engine fixtures and
installs the production logging patches only in the offline Mono process. It
does not change the running game or edit saves. Expected assertions cover
correct baseline behavior, currently confirmed failures, unchanged audit
behavior with logging installed, and exclusion of diagnostic messages from the
GUI while preserving other logs/exceptions. When behavior fixes
are implemented, turn those reproductions into regression tests with corrected
expectations and the production patches installed.

The harness now also installs [offline candidate fixes](CANDIDATES.md) after the
native baseline: **2,484 total checks**, including a 300-event stateful network
and the endpoint authority/conversion preservation matrix.
These candidate hooks exist only in this test project and are not deployed. Their
remaining lifecycle and label-format limitations are documented before promotion.
