# Native navigation ownership inventory

Read-only development scanner using Mono.Cecil on the installed game assembly.
Run from the repository root:

~~~powershell
dotnet run --project Tools/NavigationOwnershipAudit -c Release
dotnet run --project Tools/NavigationOwnershipAudit -c Release -- --check-review
dotnet run --project Tools/NavigationOwnershipAudit -c Release -- method Ostranauts.ShipGUIs.NavStation.NavModCoursePlot EngageTrip
~~~

Output goes to ignored obj/NavigationOwnershipAudit, or the first argument's
directory. It records the assembly SHA-256, selected lifecycle/control call sites,
direct control-field stores and field-address accesses. The sorted reset caller
list contains 28 methods in the inspected 1.0.1.10 assembly. The withdrawal
inventory contains 31 direct calls in 27 methods. The separate tracked
withdrawal-review.json assigns each caller a disposition and evidence level.

The --check-review command validates caller coverage, per-method call counts and
the exact reviewed assembly SHA-256, and fails when revalidation is required.
It does not automatically accept changed game code. Even an unrelated assembly
change invalidates the review; the existing behavior-graph tools provide finer
dependency validation for their respective graph slices. The method command
prints native IL without loading Unity or changing the installed assembly.

This is an IL inventory, not a reachability proof, defect counter, hook counter
or complete alias analysis. Field addresses can be reads. Use the
[collateral audit](../../Navigation/CONTROL-OWNERSHIP-AUDIT.md) and native
[reproduction suite](../../Tests/UnregisteredCourseCheck/README.md) for dispositions
and evidence. No game process, assembly, asset or save is modified.
