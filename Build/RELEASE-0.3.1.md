# Ostrabus 0.3.1

Make the custom builder's C# readable without changing its build selections or
gameplay behavior. Keep opening braces on the initiating line and simple
conditionals compact; separate distinct operations, use descriptive local names,
and wrap nested LINQ/XML construction. Replace escaped, concatenated method
strings with readable multiline C# templates. Apply the same cleanup to the
native binding harness and add builder-scoped editor formatting settings.

Readability validation passed before the release metadata bump:

- 692 generated C# files across four representative profiles have identical
  code tokens; all four selection plans and project definitions are equivalent.
- All 353 builder assertions and 22 compiled/native-bound profiles pass, including
  repeatable finalization, normal patch ordering and dynamic hook cleanup.

Project, plugin, PDA and package versions advance to 0.3.1. The normal full
deployment path is unchanged. Pending torch implementation/oracle work remains
excluded, and generated files, packages and local profiles remain ignored.
