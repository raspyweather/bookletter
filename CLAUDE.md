# Bookletter — developer notes

This file holds implementation rationale, gotchas, and "why it's built this way"
detail for whoever (human or AI) is working on Bookletter's code. For what the tool
does and how to use it, see [readme.md](readme.md) instead.

## Layout

- [src/Bookletter](src/Bookletter) — the CLI.
- [src/Bookletter.Gui](src/Bookletter.Gui) — a WinForms front end for the same options.
- [src/Bookletter.Core](src/Bookletter.Core) — the imposition/rendering engine both of the above call into.
- [tools/SampleGenerator](tools/SampleGenerator) — throwaway tool that generates the fixtures under `samples/`.

## GUI implementation notes

### Icon

Its icon ([src/Bookletter.Gui/Assets/bookletter.ico](src/Bookletter.Gui/Assets/bookletter.ico))
is set once, via `<ApplicationIcon>` in the csproj - the window picks it up at
runtime by extracting it back out of its own running .exe
(`Icon.ExtractAssociatedIcon(Application.ExecutablePath)`), rather than loading the
.ico as a second, separately-maintained copy.

### Layout architecture

Each sidebar group (Input/Output, Signature & pages, Sheet & layout, Cut marks,
Output mode), the Run row, and the Preview tab's content are each their own
[UserControl](src/Bookletter.Gui/Controls) rather than one large form class — every
field-enabling rule that only concerns its own group (e.g. cut-mark sub-fields
dimming when "Enable cut marks" is off) lives inside that control, and a control
exposes only the typed properties and change events (e.g. `PreviewAffectingChanged`)
that the rest of the window actually needs.

The options panel has a fixed width and doesn't reflow when the window is resized;
the right side (the bulk of the window) is a tab control with three tabs: Preview,
Signature map, and Log. Clicking Run automatically switches to the Log tab so
progress is visible without having to go look for it.

### Status bar

A status bar along the bottom of the window shows the current high-level state
regardless of which tab is active, since the Preview tab's own per-sheet status line
is only visible while that tab is selected. Between transient messages it rests on
"No PDF selected" or the file name - instant, no PDF reading needed - rather than a
static "Ready". While generating, the text updates per sheet ("Generating booklet...
sheet 123 of 500") instead of sitting on one static message for the whole run.
Page/signature/sheet counts ("Pages: 2000" / "Signatures: 125" / "Sheets: 500") sit in
their own labels to the right, filled in from the same cheap, rasterization-free
lookup the preview already does - split out from the main status text specifically so
they survive a conversion run instead of being overwritten by "Generating booklet..."
for its duration. A progress bar sits between the status text and those count labels.

The status label itself doesn't use `Spring` (the usual WinForms way to make a status
bar item fill the remaining width) - a `StatusStrip` with its default Table layout
style mispositions its item (lands it well outside the strip's own bounds, entirely
invisible, with no exception or other symptom) whenever there's only one *visible*
item, confirmed with a minimal repro uninvolving any other part of this app. A
permanent 1px spacer item keeps the count at two or more at all times, and the label's
width is instead computed and set by hand
([MainForm.cs](src/Bookletter.Gui/MainForm.cs), `UpdateStatusLabelWidth`).

### Taskbar progress

The same progress also shows on the taskbar button itself (the green overlay Explorer
uses for a file copy) via the shell's `ITaskbarList3` - WinForms has no managed API
for that, unlike WPF, so it's done directly through COM interop
([TaskbarProgress.cs](src/Bookletter.Gui/TaskbarProgress.cs)); every call is a no-op
if the shell object can't be created, since it's a cosmetic feature, not worth
crashing the app over.

### Run / Cancel / Open output folder

Run and the row's left slot - which holds "Open output folder" or "Cancel",
depending on state - split the row evenly and each fill their own half, so they
render the same size whenever both happen to be visible at once. "Open output
folder", "Cancel", and the status bar's progress bar are all hidden entirely rather
than just disabled/grayed out when there's nothing for them to do: "Cancel" and the
progress bar only appear while a run is actually in progress, and "Open output
folder" only appears once one has finished successfully — so the idle state is just
the Run button, with nothing sitting around half-relevant. Cancelling stops
generation after the sheet currently in progress (checked once per sheet, not
mid-sheet) rather than instantly - clicking it disables the button immediately so a
second click can't fire again before the first takes effect - and leaves no output
files behind, since those are only written after every sheet has been composed.
While running, the bar shows real, per-sheet progress as sheets are composed (the
rasterization-heavy part of the work), then switches to an indeterminate marquee for
writing the output file(s) afterward — that part has no per-step granularity, and for
anything beyond a handful of sheets it can itself take several more seconds, so the
bar switches to "still working, just can't say how long" rather than sitting at a
misleading 100% in the meantime.

### Signature map performance

The cost behind the Signature map's slow resizing would be each signature's box being
left `AutoSize`: the outer list's wrap recalculation, which runs on every resize tick,
has to call `GetPreferredSize()` on every box to decide where lines wrap, and because a
box is itself a tree of `AutoSize` containers (cards, side rows, labels), that
recursively remeasures every label in every sheet card - every tick, even though a
box's content is fixed the moment it's built. Each box is sized once, right after it's
built, and frozen (`AutoSize = false`) from then on, so that cost is paid once instead
of on every resize - a ~6x speedup on its own (measured: ~152ms per resize tick for a
2000-page/125-signature document with boxes left `AutoSize`, vs. ~24-26ms frozen), and
unlike the suspend/resume below, it also keeps resizes that never fire that scheme's
events - maximizing the window, Aero-snap - fast. The freeze has to happen before a box
is parented to the list, not after: freezing right after adding it and running a real
layout pass sounds like it should be the more trustworthy moment to measure from, but
`GetPreferredSize()` gave a wrong, too-small answer immediately after parenting -
confirmed, repeatably - and only started returning the right one once the message loop
had processed something else first. Measuring while the box is still a self-contained,
freshly-built tree (nothing else competing for layout) sidesteps that.

Interactive window resizing additionally suspends layout for the duration of the drag
and resolves it once when you let go, rather than recomputing on every pixel of
movement — scoped to just the Signature map tab, the only place still worth hiding
behind it (frozen boxes bring a tick down to ~24-26ms, still above a smooth-drag budget
for hundreds of signatures; everywhere else in the window - the sidebar, the Preview
tab, the Log tab - is already ~13-15ms per tick, fast enough to reflow live without
hiding it). With both in place, a drag on the Signature map tab measures ~14-15ms per
tick - on par with the rest of the window, which keeps resizing live during the drag
instead of jumping all at once at the end.

The Signature map is derived purely from the imposition math (no PDF rendering), so
it's instant regardless of document length. It refreshes automatically (reading just
the source PDF's page count on a background thread, so even a very large file doesn't
stutter the UI) whenever you switch to the tab or change a relevant setting while it's
visible, and has its own "Refresh" button for a manual nudge.

### Preview tab

Its toolbar is split into three stable rows instead of one flowing line: a controls
row (sheet selection on the left, a divider, then zoom/"Open image" on the right), a
fixed-height status line below it, and the rendered image beneath that — so a long
status message (e.g. an error) just ellipsizes instead of wrapping and shoving the
controls and image downward. There's no manual "Preview"/refresh button; the preview
is always kept current automatically. A "Sheet:" field picks which one to view, using
the same "global sheet" numbering as the Signature map tab; its range (1 to however
many sheets the current settings produce) is kept in sync with the real sheet count,
so typing or scrolling past it simply isn't possible rather than being allowed and
then visibly snapped back afterward.

Every preview renders in two passes: a fast one at a DPI just high enough for the
preview panel's current size (cheap regardless of your real `--dpi`, which can be far
higher than any screen needs), shown immediately, followed by one at the real
configured DPI that replaces it — so you get instant feedback on a setting change
without waiting for a potentially slow high-DPI render, and still end up with the real
thing a moment later. The preview also auto-refreshes (debounced by ~500ms) whenever
you change a setting that affects the rendered sheet — input PDF, pages, signature
size, page order, sheet size, DPI, margin/gap, background color, allow-upscale, or any
cut-mark setting — so it stays in sync as you tweak things; settings that only affect
which files get written (output folder, split-signatures, reverse-back-order, output
mode, image format/quality) don't trigger a re-render. Overlapping refresh requests
are coalesced rather than queued, so rapid changes always settle on a render of the
latest settings instead of stacking up stale ones.

The same debounce also drives the Pages field's live validity check
(`MainForm.ValidatePagesFieldAsync`, see `SignaturePagesPanel.SetPagesValidity`): it's
a separate, narrower re-check against the real source page count, not a messagebox, so
a field that's only briefly invalid mid-edit (e.g. the instant after deleting a comma)
doesn't interrupt typing to complain about it, and a problem elsewhere (e.g. an invalid
custom sheet size) doesn't get blamed on Pages.

### Drag and drop

The input PDF can also be set by dragging a `.pdf` file in from Explorer and dropping
it onto the window, the "Input / Output" group, or the input field itself (which
highlights while a valid PDF is being dragged over it) — an alternative to the
"Browse..." button, not a replacement for it.

## Imposition engine notes

`SignatureCalculator.Calculate` works on an abstract page *count* and produces
1-indexed logical positions within a signature; it has no notion of which real source
page a logical position maps to. That mapping is `PageSelector.Parse`'s job: it turns
a `--pages` spec into an ordered `IReadOnlyList<int>` of 0-indexed source page numbers,
and every "logical position → real page" lookup elsewhere (CLI console log, GUI
signature map, GUI preview) goes through that same list — so page numbers shown
anywhere are always real source page numbers, not positions within the selection.

## Verification methodology

This codebase has no automated test suite for the GUI yet. When changing GUI
behavior, verify it with a disposable WinForms harness instead of taking a build
success as proof of correctness:

- Scratch `.csproj` (in the scratchpad directory, never inside the repo) with
  `<TargetFramework>net10.0-windows</TargetFramework>`, `<UseWindowsForms>true</UseWindowsForms>`,
  and a `ProjectReference` to `src/Bookletter.Gui/Bookletter.Gui.csproj`.
- `internal static class TestProgram { [STAThread] static void Main() {...} }` —
  never top-level statements, which don't reliably get an STA thread.
- `Application.Run(form)` with a `form.Shown` handler for the actual test logic
  (required for `WindowsFormsSynchronizationContext` to correctly marshal
  `Task.Run` continuations back to the UI thread).
- Reach private fields/controls via reflection
  (`BindingFlags.NonPublic | BindingFlags.Instance`).
- `control.DrawToBitmap(...)` for screenshots/visual assertions.
- Always `rm -rf` the scratch directory when done, and finish with a full-solution
  `dotnet build` plus a real exe launch-and-kill check.

## Git hygiene

Review `git status`/`git diff` before staging — don't blindly `git add -A`. This repo
has previously had a large stray local test PDF (`apus.pdf`, used for the maintainer's
own manual testing) get accidentally committed; it's `.gitignore`d at the root now
(`/*.pdf`), but any new file dropped at the project root for ad hoc testing should be
assumed **not** safe to stage without checking first.
