# Socials PDA first-open delay

Investigated and implemented 2026-11-09, game 1.0.1.10. User reports a long delay
on the first Socials opening after loading a save. The cold composition
optimization below is offline-validated and live-verified for the captured
182-contact first opening: synchronous construction fell from 5.75 to 0.52 seconds.

## Native work

`GUIPDA.ShowSocials` synchronously destroys the old list, collects the selected
character's relationships, sorts them, resolves each contact, applies the native
filters, then instantiates and populates every remaining GUISocialsRow. There is
no yielding, row virtualization or viewport-limited portrait generation.

This traverses the character's social history, not every NPC in the universe.
`Social.GetAllPeople` copies its dictionary values. `GetKnownSocialContacts`
inserts familiar contacts at index zero, making that ordering quadratic in the
worst case; ShowSocials then sorts the result again. At the measured contact
count, this alone is not sufficient evidence for the reported long cold delay.

`GUISocialsRow.SetContact` calls `FaceAnim2.GetPNG` for contacts whose CO resolves.
GetPNG builds a composite key from the face layers and uses DataHandler's image
cache. On a cache miss it blends every pixel using Texture2D.GetPixel for each
layer and Texture2D.SetPixel for each output pixel, all synchronously on the main
thread. Native TXTFacePartOrder defines 11 layers; inspected portrait layers are
166 x 258 pixels. That is **471,108 GetPixel calls and 42,828 SetPixel calls per
cold composite** (513,936 total), plus blending, image loading and texture upload.
Subsequent calls reuse the generated composite through DataHandler.AddPNG and
LoadPNG, though they still do some array, key and layer lookup work.

Contacts with no resolved CO use a shared deceased/missing image instead. A dead
CO that still resolves follows the ordinary CO portrait branch; SetContact does
not test bAlive there. PersonSpec.GetCO only looks up/caches a live registry
reference; it does not create a missing NPC to populate this UI.

## Save census

Read directly from the `home` archive, retaining only a small summary:

- Ayden has 487 relationship records, including 387 with RELStranger.
- 208 contacts have a matching saved CO: 166 alive and 42 dead.
- 279 have no matching saved CO; 31 PersonSpec records explicitly report dead.
- Relationship event histories total 60 entries, at most three per contact.

Unresolved save records are not proof of a historical cause of death. Saved CO
presence also does not prove which COs or portrait cache entries are present at
the instant the live UI opens. User filters determine how many rows are displayed.
The pre-existing Ostrabus GetKnownSocialContacts postfix already removes contacts
whose CO no longer resolves, so those 279 unresolved records were not all rendered
rows. The subsequent QoL construction-drone filter also acts on that display list.
Private census source/results are in ignored obj/SocialsAudit and
obj/socials-save-summary.json; no large extracted save files were written.

## Assessment and follow-up

The cold portrait construction is the strongest source-level explanation for
the first-open versus repeat-open difference. Historical records additionally
increase synchronous row/layout work. A live timing breakdown has not been
captured, so no percentage or measured UI speedup is claimed.

Prioritize preserving exact portrait appearance and existing cache behavior while
replacing per-pixel Unity calls with bulk pixel access. Validate layer order,
alpha/color math, readable texture formats and allocation costs offline before
choosing an implementation. Avoid allocating fresh large buffers per contact or
retaining NPC references in another permanent cache. UI population can separately
be paced or virtualized if row/layout cost remains substantial.

Preserve social history, dead contacts, filters and native relationship behavior.
Deleting NPC/history records is not required for this performance repair.

## Implemented optimization

`PortraitCompositionPatch` adds one guarded transpiler on FaceAnim2.GetPNG. It
replaces only the cold pixel loop after native texture creation/filter setup and
before Apply. Native face-layer lookup, composite keys, cached-texture validation,
constructor, Apply, texture naming and DataHandler.AddPNG remain in place. Null
and crewless callers follow their original branches. Other GetPNG callers can
also benefit when they need a cold composite; no GUI rows or filters change.

`PortraitComposition` borrows mip-zero input data with GetPixelData<byte>(0),
blends in the original layer order, then calls SetPixels once using float Colors.
A 256-entry channel table is initialized using the shipped Color32 conversion.
The revised loop uses that table and direct equivalent interpolation, without
calling Color32 conversion and Color.Lerp for each layer/pixel. Transparent
samples are skipped; opaque samples retain the arithmetic rather than assigning
the source color directly, preserving intermediate float rounding. Layers are
scanned sequentially and intermediate results are not quantized. Inputs support
RGBA32, ARGB32 (A,R,G,B byte order) and RGB24 (opaque
alpha). ARGB32 matters because native DataHandler.LoadPNG uses LoadImage for
the shipped PNG portraits; all 290 inspected portrait PNGs have 8-bit RGBA data.
The decoded Unity format is checked at runtime rather than assumed from PNG bytes.

Unsupported formats, differing dimensions, unreadable input, reentrant calls,
more than 32 layers or more than 262,144 output pixels retain the native pixel
loop. Actual texture/format errors are not silently swallowed.

The helper retains a one-KiB scalar channel table and one thread-local Color[] for the most recent accepted size:
685,248 bytes of pixel storage for 166 x 258, with a four-MiB maximum payload.
The same size reuses that buffer; a size change replaces it. Layer views are
stack-local and borrowed, never disposed or cached. No NPC, crew, texture, native
view or pointer references survive the call. No layer pixel arrays, background
worker, coroutine, update hook, extra image cache or save data are introduced.

Unity documents [borrowed pixel data and its lifetime](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Texture2D.GetPixelData.html),
[ARGB32 byte order](https://docs.unity3d.com/cn/6000.0/ScriptReference/TextureFormat.ARGB32.html),
and [bulk writes](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Texture2D.SetPixels.html).

## Validation

[`Tests/PortraitCompositionCheck`](../Tests/PortraitCompositionCheck/README.md)
executes the shipped GetPNG under bundled Mono, first native and then with the
actual production transpiler. 49 checks pass, including pixel-by-pixel float
equivalence, RGB24/ARGB32/RGBA32 mixes, transparency, non-square dimensions, cold
generation, warm hits, invalid cache size, null/crewless branches, fallback,
exceptions, reentrancy and unknown-IL refusal.

For the controlled full-size cold case, individual GetPixel calls fall from
471,108 to zero, and SetPixel calls from 42,828 to zero. There are 11 borrowed
input reads, one bulk output write and the original single Apply. Warm cache hits
do neither composition nor upload. Warmed buffer identity is verified across
1,000 calls. Calibration found that this bundled Mono runtime returns zero from
GetAllocatedBytesForCurrentThread even for deliberate allocations, so the earlier
zero readings are not treated as allocation proof. The kernel contains no new
managed allocations and reuses its scalar output storage; no claim of measured
zero total Unity/texture IO allocation is made.

Texture storage/read/write operations are boundary substitutes in the offline
fixture. The tests validate the production control flow, byte addressing and
native managed color arithmetic; they do not execute Unity's native PNG decoder,
GPU upload or full Socials layout. Controlled fixture timings are printed only
as diagnostic data, not a predicted live speedup. Live opening timings are
recorded below; the offline float-pixel comparison remains the appearance oracle.

## First live result and temporary timing capture

The user reports no noticeable first-opening improvement after deployment. The
installed DLL hash matches the local build; construction-drone filtering works.
All 290 shipped portrait PNGs have matching 166 x 258 dimensions. This rules out
an older deployed DLL or differing shipped layer sizes as simple explanations;
it does not establish which texture formats or cache paths ran in that session.
The bulk compositor remains a verified reduction in pixel API calls, **not a
demonstrated fix for the Socials stall**. No new speculative performance patch
has been applied to UI population.

`SocialsProfiling` is an explicitly armed, temporary observer. It has no startup
Harmony attribute and installs no hooks until POST /profiling/socials/start or
POST /profiling/socials/open. The latter uses the normal PDA state transition to
open Socials for the operator, avoiding a human-operated test. It does not evict
textures or manufacture a cold cache. GET /profiling/socials reads detached results
off the main thread, including while a UI action is busy.

Capture separates ShowSocials, contact collection, row population, resource
loading/instantiation, career/ship-link strings, GetPNG, bulk composition, native
pixel fallback, deferred layout, TMP rebuild/text generation and canvas update.
It records inclusive/exclusive times, overlapping inclusive allocation totals,
calls/errors, and the first compositor input's scalar dimensions/formats. No
people, textures or UI object references are retained.

After the first opening and three deferred frames, all 19 observed methods are
unpatched automatically. Cancellation, 180 seconds without an opening, scene/
system changes and API shutdown also remove the hooks. While armed, timing affects
the measured calls; deferred frame metrics include other active UI. The capture
does not alter sort/filter/row construction or change any native exception.

18 offline checks verify binding, no startup/GET hooks, nesting, timing counts,
fallback observation, deferred layout, exception propagation, cancellation,
timeout and automatic unpatching. Native/UI boundaries are substituted in that
fixture; it validates the instrumentation lifecycle, not the cause of the live
stall. See Tests/SocialsProfilingCheck. Captures below use the first Socials
opening after a fresh load without evicting image-cache entries.

## First live capture and kernel refinement

Captured 2026-11-09 through POST /profiling/socials/open, with the expected DLL
verified. The UI action exceeded the inspection request timeout, but completed;
detached results were recovered via GET without retrying the UI action. Capture
completed without diagnostic errors and removed its own hooks.

| Work | Calls | Inclusive time |
|---|---:|---:|
| ShowSocials | 1 | 5750.54 ms |
| SetContact | 182 | 5660.74 ms |
| GetPNG | 182 | 5649.42 ms |
| TryCompose | 181 | 5372.60 ms |
| LoadPNG | 2190 | 234.44 ms |
| Object.Instantiate | 182 | 83.38 ms |
| Deferred canvas updates | 4 | 315.78 ms |
| Native pixel fallback | 0 | 0 ms |

Times overlap; do not add nested rows. The first input used 11 readable ARGB32
layers with expected dimensions. All 181 cold composites used the bulk path;
no native per-pixel fallback ran. The first optimization executed but remaining
composition work still dominated. UI layout was smaller. The capture did not
separately time Blend and SetPixels, so it does not assign every TryCompose
millisecond to one subphase.

The initial bulk loop still converted bytes to Color and called Color.Lerp for
every layer/pixel. The revision removes those calls/divisions, reuses exact byte
normalization, skips zero-alpha samples, and scans layers sequentially. Native
arithmetic/order is kept; clamping is unnecessary for byte-derived [0,1] inputs.

Under bundled Mono, ten-round warmed managed-kernel comparisons measured
21.27 -> 3.89 ms for dense mixed-format inputs and 21.45 -> 2.03 ms for a fixture
with many transparent samples. These are controlled CPU measurements excluding
texture IO, not predicted whole-UI times. Both kernels compare exactly against
the native managed blend oracle in the fixture. The live follow-up is below.
The temporary observer separately times Blend, SetPixels and Apply,
and reports allocation metrics as unavailable when calibration fails.

Private first-capture artifacts: obj/SocialsLive-20261109-175636; the current path
is recorded in obj/socials-live-path.txt. Kernel checks are in ignored
obj/portrait-revised-kernel-tests.txt. No save or portrait cache was modified to
manufacture the measurement.

## Revised kernel: live validation

2026-11-09, revised DLL hash
1AEFAAF884C0F1D060A905CBB8AB9131619348A61F6D470BE1631D90C231DA6F
matched the installed plugin. The first Socials opening again displayed 182
contacts, hid 307 missing/drone contacts, and generated 181 cold portraits.

| Work | Initial bulk version | Revised kernel |
|---|---:|---:|
| Synchronous ShowSocials | 5750.54 ms | 515.57 ms |
| GetPNG, 182 calls | 5649.42 ms | 456.20 ms |
| TryCompose, 181 cold portraits | 5372.60 ms | 246.45 ms |
| Deferred canvas updates, four calls | 315.78 ms | 327.38 ms |

The synchronous opening improved by 11.15x (91.03% reduction); composition by
21.80x (95.41%). These are two comparable captures from this save, not a
guarantee for every contact list or machine. Nested metric times overlap.

The revised capture separates 207.79 ms in managed Blend, 30.15 ms in SetPixels,
and 11.99 ms in native Apply. Native fallback remained unused. Image lookups took
176.92 ms; row instantiation 48.91 ms. Deferred TMP generation still accounts for
246.19 ms within the roughly 327-ms canvas update total. That is a remaining
shorter UI cost, not evidence of another multi-second portrait stall.

The diagnostic allocation counter failed calibration and is reported unavailable,
not zero. Capture completed one opening and three deferred frames with zero
diagnostic errors, no new warning/error logs, and all profiler hooks removed.
No further implementation change or reload was required after this capture.

Raw evidence is ignored obj/SocialsRevisedLive-20261109-180733, especially
completed.json, comparison.json and logs.json; the path is recorded in
obj/socials-revised-live-path.txt.
