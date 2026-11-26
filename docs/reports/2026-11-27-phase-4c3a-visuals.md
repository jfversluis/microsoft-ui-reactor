# Phase 4c-3a gate: shapes, gradient brushes, ContentView, image source equality, property census (spec 070 §4)

This phase adds the visual elements the ports draw with: shapes (WeatherTwentyOne uses `Ellipse`),
gradient brushes, `ContentView`, and image sources that don't reload when an equal one is rendered
again. It also lands a property census that 4c-3b will use for the audit. PR #18, stacked on #17.

## What changed

- **Design (spec 070 §4).** Reviewed by both model families before code. The review:
  - split 4c-3 into 4c-3a and 4c-3b (the 🧪 rows and the ✅ audit);
  - made gradient equality exact (MAUI's tolerant stop comparison drifts over small changes);
  - made collections and brushes values;
  - kept a null-clearing `Stroke(Color?)`;
  - required the census to prove coverage rather than list names.
- **Shapes.** `Rectangle`, `Ellipse`, `Line` and `Path2D` with WinUI's names; `Polygon`, `Polyline`
  and `RoundRectangle` with MAUI's.
  - **Properties:** fill, stroke, thickness, dash, dash offset, cap, join, miter limit, fill rule and
    `Aspect`.
  - **Values:** point and dash arrays are copied at construction and compared by sequence; path
    markup is converted only when it changes.
  - **Clearing a path's transform:** MAUI's iOS and Android path mappers ignore a null transform,
    so a cleared transform stays drawn. Reactor watches the property and, whenever it goes from set
    to unset by any route, clears the platform drawable's own transform. The routes covered are a
    declaration, a style, a class style resolving late, a parent's resources and a ref write. An
    earlier write-identity-then-clear workaround didn't reset the drawable when run inside the
    clearing notification.
- **Brushes.**
  - **Factories:** `LinearGradientBrush` and `RadialGradientBrush`.
  - **Equality:** brushes compare by exact value with matching hashes, wherever a brush is used.
  - **Contract:** rendered brushes are immutable by contract.
  - **`Border.Stroke`:** now a `Brush?`. A `Color?` overload keeps existing calls compiling and
    clears the stroke on `null`.
- **`ContentView`,** with padding.
- **Image sources:**
  - **Equality:** compared by value for font, file and URI sources. A URI compares its original
    string, including user info, fragment and caching settings.
  - **Streams:** `StreamImage(key, open)` gives a stream an identity.
  - **Coverage:** every image consumer uses this, including the Shell icons and back button.
- **Property census and matrix:**
  - **Census:** every public element property must be covered by a matrix case or a claim, or sit
    on a frozen pending list. Matrix cases set, update, clear and render equal on both registries.
    A claim names a discovered test and lists its facets.
  - **Pending list:** 348 properties, pinned by hash. It can only shrink, and its length must
    equal its ceiling.
  - **Coverage now:** 58 element types; this phase's elements and Border, Image and ImageButton
    have nothing pending.
- **Shipped props.** The test projects, sample and benchmark now import the package's
  `buildTransitive` props instead of copying them, so the shipped file is compiled. It adds
  `PenLineCap`, `PenLineJoin` and `FillRule` aliases. Importing the shapes namespace would also bring
  in MAUI's `Path` and `Rectangle`, so the props uses aliases instead.
- **Docs:** spec 070 §4 with its as-built record and deviations (§4.13), spec 068 rows, spec 065, the
  README (snippets compiled by tests), and the CHANGELOG.

## Tests

| Tier | Before (4c-2 head) | 4c-3a | Notes |
|---|--:|--:|---|
| MAUI renderer (headless) | 429 | 686 | the matrix (90+ property cases on both registries), brush and image-source rule tables, cumulative drift, collections as values, path data, the transform watch on every route, the census gates |
| MAUI engine (headless) | 80 | 80 | |
| MAUI device, Mac Catalyst | 156 + 7 skipped | 174 + 7 skipped | full run after the first review round; visual fixtures rerun at the final head |
| MAUI device, iOS 27 | 156 + 7 skipped | 174 + 7 skipped | as above |
| MAUI device, Android 37 | 158 + 5 skipped | 176 + 5 skipped | as above |
| Release build | 0 warnings | 0 warnings | |

- **Characterization:** checked on all three platforms before relying on any of it:
  - the pixel sampler reads exact colours;
  - a plain transform clear stays drawn (the MAUI bug);
  - a stream source is opened once per mount;
  - a Border insets its content by padding plus stroke.
- **Pixel oracles** predict a colour at fixed points for each shape, using 8-point strokes,
  asymmetric `Aspect` cases, a changed gradient stop and a non-default radial centre. Image "no
  reload" checks only count once a decoded image has been witnessed.
- **Mutation checks:** over 40 headless, census and device mutants across the build and three review
  rounds, all red. One equivalent mutant survives: a `Color?` overload writing `Brush.Default`,
  which the comparer already treats as unset.

## Performance

`/perf` ([run 45885205164](https://github.com/jfversluis/microsoft-ui-reactor/actions/runs/45885205164)),
StocksGrid against `main`:

| Metric | `main` | 4c-3a | Δ (95% CI) |
|---|--:|--:|--:|
| Renders/sec ↑ | 3.64 | 3.55 | -0.5% [-6.2, +5.2] |
| Avg Reconcile (ms) ↓ | 101.2 | 98.7 | -0.9% [-4.6, +2.9] |
| Avg Diff (ms) ↓ | 91.2 | 88.0 | -1.4% [-5.6, +2.9] |
| Avg Memory (MB) ↓ | 289.9 | 290.1 | +0.2% [-0.5, +0.8] |

- **Headline metrics:** within noise.
- **Flags:** the low-mutation section flags memory at +0.4% and allocations at +0.5%. Both are under
  the gate's thresholds, and this phase changes nothing under `src/Reactor`, `src/Reactor.Core` or
  the benchmark.
- **MAUI benchmark (Mac Catalyst):** within noise or faster, with identical allocations.

## Review

`pr-review` ran 7 dimensions on Claude plus a gpt-6.1-sol cross-check, then three verification rounds
with both families.

- **The path transform clear (high).**
  - **Rounds 1–3:** gpt-6.1-sol reproduced three routes that left a cleared transform drawn: a style
    removed, a class style resolved after attachment, and a parent's resources replaced. Claude
    independently confirmed the second.
  - **Fix:** each per-route repair left another route open, so the fix moved to the property itself.
  - **Final round:** both families report the final version clean.
- **Medium, fixed:**
  - the census's pending list could grow again or swap entries; it is now frozen;
  - WinUI's `StrokeDashOffset`, `StrokeMiterLimit` and `FillRule` modifiers were missing;
  - the new aliases were undocumented, and the shipped props file was never compiled.
- **Low, fixed:**
  - URI equality ignored credentials;
  - `Border.Stroke`'s thickness rule differed from the shapes';
  - `Brush.Default` forced a Controls view;
  - a duplicate comparer;
  - untested back-button fields;
  - a vacuous stream-open assertion;
  - the line coordinates' clear claim.

## Decisions for the gate

1. **Split 4c-3 into 4c-3a (this PR) and 4c-3b (the 🧪 rows, the ✅ audit, and the FlyoutPage
   split-mode and stale toolbar-item bugs).** Recommendation: accept.
2. **Brushes and stream image keys are immutable by contract**, and gradients compare exactly.
   Recommendation: accept.
3. **`Border.Stroke` becomes a `Brush?`**, a breaking change to a preview API that the `Color?`
   overload softens. Recommendation: accept.
4. **`Line`'s coordinates and `RoundRectangle.CornerRadius` are always written**, so a style setter
   can't apply to them. Recommendation: accept; documented.

## Open issues and follow-ups

- **To file in dotnet/maui:** the path `RenderTransform` mapper ignores a null transform on iOS,
  Android and Windows.
- **Windows (4g):** the drawable reset is compiled for iOS, Mac Catalyst and Android only. Windows
  needs the same branch, verified on the Dev Box.
- **4c-3b:** the 348 pending census properties, the remaining 🧪 rows, FlyoutPage split mode (guarded
  in both `Update` and `SyncSlots`), and stale toolbar items.
