# Player console retention and scrolling

Native `ConsoleToGUI` uses a single `myLog` string. `Log` and `LogInfo` search
only the first `kChars + 40` characters for a newline while trimming. A longer
leading line makes `Log` abandon trimming and makes `LogInfo` repeat a substring
that removes nothing. Command echoes in `DrawConsole` bypass trimming entirely.
The native `GUI.TextArea` also has a maximum length and a fixed 4,000-pixel
rectangle, so excess/wrapped output may be hidden instead of accessible by scroll.

The local `console_params.json` requests a 700-character ordinary message limit
and 16,382-character history. These are runtime console settings, not game behavior
assets. JSON cannot correct the faulty trimming loops or display geometry.

`ConsoleBufferPatch` replaces only history-field writes in native `Log`,
`LogInfo` and `DrawConsole`. The buffer is bounded before native trimming runs,
so those loops have no work. It removes oldest output, preferring complete native
entries (including stack traces). If one entry exceeds the entire budget, it
keeps trailing lines or the tail of an oversized single line. It avoids splitting
a UTF-16 surrogate pair. Existing oversized buffers are bounded before display.
Configured history limits are respected within 1-16,382 characters; invalid or
larger values are clamped to that range, preserving the native default ceiling.

Only the content rectangle's height is adjusted to the measured rich-text height,
keeping the native minimum. The scroll viewport and text rectangle use the same
height. At the bottom, scrolling follows new output; browsing older output does
not force a jump to the end. One measurement is cached by text reference, width,
style and font size. The cache contains only the current string and measurement,
with no growing collections, update callback, background task or saved state.

Native commands, visibility filters, warning badges, error counts, ordinary
message truncation and optional file logging retain their original bodies.
Unity/inspection log subscribers are unchanged. Native IL write/rectangle counts
are checked when patching; unexpected method shapes reject the patch.

The custom-builder bugfix feature is `console-buffer`, independently selectable
without the noise filter or Inspection API. Normal deployment includes it.

`Tests/ConsoleBufferCheck` passes 3,058 checks against the shipped assembly and actual production
patch under bundled Mono. It reproduces unpatched trimming failure, checks long
lines/entries, multiline traces, budgets, repeated overflow, filters and counters,
and executes native `DrawConsole` through its scroll-view/text-area calls.
Engine input, screen dimensions, text measurement and GUI submission are fixture
boundaries. Actual Unity wrapping and visual interaction still require a live
check after deployment.
