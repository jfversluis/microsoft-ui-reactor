# Phase 4c-2 gate: collections and items (spec 070 §3)

This phase brings the item controls the ports need to MAUI: list items that show their selection,
grouped lists, swipe actions, radio buttons with view content, picker display text and formatted
labels. PR #17, stacked on #16.

## What changed

- **Design (spec 070 §3).** Reviewed in three rounds by both model families before code. The rounds:
  - deferred CarouselView and IndicatorView to 4c-4 (§5). Their position couldn't be reported
    without either moving the carousel under the user's finger or guessing where a change came
    from, and no port needs them;
  - made radio groups Reactor's own, because MAUI's grouping can't see buttons inside direct
    layouts;
  - added a native-selection check on iOS and Mac Catalyst, after the review traced a MAUI bug.
- **Four premises were hard gates.** Device fixtures measured each before anything was built on it,
  and all held on every platform:
  1. a write's `SelectionChanged` is raised inside the write;
  2. the same for `CheckedChanged`;
  3. UIKit completes queued batches in order;
  4. MAUI applies a nested radio write after the notification returns.
- **CollectionView:**
  - **Item state:** `(item, ItemState)` templates show the declared selection and turn off the
    platform highlight.
  - **Grouping:** group keys are unique across the list.
  - **Selection:** `ScrollToKey`; the selection mode is written inside the echo scope; multiple
    selection is compared as key sets.
  - **Native selection check (iOS and Mac Catalyst):**
    - **Why:** MAUI applies a selection after its batch update and never checks that it's still the
      declared one, so an older selection can overwrite a newer one natively.
    - **What it does:** it reads the native selected rows behind UIKit's batch updates and fixes
      them.
    - **When:** after a selection or mode write, or after user input. User input is held until the
      next commit, so a check can't clear it first. An accepted tap costs one batch update.
- **SwipeView:**
  - Items on four sides, with modes, `Threshold`, view items and the swipe events.
  - `SwipeAction` is renamed to WinUI's `SwipeItemData`.
  - A side is rebuilt only when its structure changes, so an open swipe stays open on re-render.
  - An item invokes only while it is still the item at its index.
- **RadioButton:**
  - **Content:** view content (`ContentElement`), with or without the indicator. Without an
    accessible name it warns once.
  - **Groups:** Reactor groups the buttons on both renderers. Named groups are scoped to their page;
    unnamed ones to their layout, with cells and slots as boundaries.
  - **Taps:** a tap is one transaction. Peers are unchecked natively before any callback runs, and
    a check made from a callback is queued to a later turn.
  - **Errors:** a throw anywhere in the transaction goes to the error boundary, and every
    participant is restored.
  - **Lifetime:** a group disappears with its last member.
  - **Conflicts:** two buttons declared checked in one group are shown as declared, with a warning.
  - **Parameter name:** `onIsCheckedChanged`, as on WinUI.
- **Picker:** `Picker(items, display, index, …)`.
- **Label:** `Run` and `Hyperlink` inlines with MAUI's property names.
  - Spans update in place.
  - A hyperlink taps only while its recognizer is still installed.
  - `Underline` and `Strikethrough` each toggle only their own flag.
  - `LineHeight` and `TextTransform` are available on labels as well as runs.
- **Docs:** spec 070 §3 with its as-built record (§3.13), the §5 stub for 4c-4, spec 068 rows, spec 065
  and 067 notes, the README (with test-mirrored snippets) and the CHANGELOG.

## Tests

| Tier | Before (4c-1 head) | 4c-2 | Notes |
|---|--:|--:|---|
| MAUI renderer (headless) | 328 | 429 | item state, grouping, the selection check (every completion order, input before commit, entering `None`), swipe structure and identity, radio scopes, transactions, queue, errors and lifetime, spans, picker |
| MAUI engine (headless) | 80 | 80 | |
| MAUI device, Mac Catalyst | 114 + 7 skipped | 156 + 7 skipped | full run at `3b58d7a4`; radio fixtures rerun at the final head |
| MAUI device, iOS 27 | 114 + 7 skipped | 156 + 7 skipped | as above |
| MAUI device, Android 37 | 117 + 4 skipped | 158 + 5 skipped | as above; the new skip is the iOS-only batch-order gate |
| Release build | 0 warnings | 0 warnings | |

- **Mutation checks:**
  - **Headless:** over 75 mutants.
  - **On device:** 7 mutants, each turning its fixture red on the platforms that can show it.
  - **Survivors:** four radio paths that no test can tell apart from the real code, kept as
    defensive code: the membership recheck, restores in `finally`, restoring a dropped queued
    check, and the drain's re-post.
- **MAUI's selection bug, reproduced:** with the native check switched off, iOS and Mac Catalyst
  end with item A selected natively while `SelectedItem` says B. With it on, B is selected and
  nothing extra is reported.
- **The Android WebView flake from 4c-1** (2 of 3 full runs) was found to be two timing races in
  that test, not product code:
  - after a late reload cancel, the test recovered through a history entry Android skips on back;
  - MAUI's `EvaluateJavaScriptAsync` never completes when a navigation replaces the page.

  Both are fixed in the test, and three full Android runs in a row then passed.

## Performance

`/perf` on PR #17, StocksGrid against `main`:

| Metric | `main` | 4c-2 | Δ (95% CI) |
|---|--:|--:|--:|
| Renders/sec ↑ | 2.69 | 2.71 | -1.2% [-5.9, +3.5] |
| Avg Reconcile (ms) ↓ | 134.7 | 133.7 | +2.6% [-3.7, +8.8] |
| Avg Diff (ms) ↓ | 123.1 | 123.7 | +3.9% [-2.3, +10.1] |
| Avg Memory (MB) ↓ | 289.2 | 287.4 | -0.7% [-1.5, 0.0] |

- The headline metrics are within noise, and allocation bytes per render are within noise in every
  section.
- The low-mutation section flags reconcile and diff at +2.2% and Gen0 collections at +2.3%. All are
  under the gate's +5% threshold.
- This phase changes no file under `src/Reactor`, `src/Reactor.Core` or the benchmark, so the
  measured code is identical.
- On the MAUI benchmark (Mac Catalyst), every scenario was within noise, with identical allocations.

## Review

`pr-review` ran 7 dimensions on Claude plus a gpt-6.1-sol cross-check, then three verification rounds
with both families. Security and packaging were clean.

- **High, fixed and confirmed by the other family:**
  - radio group generations were never pruned, so a page checked once stayed alive after removal
    (reproduced with a weak-reference probe);
  - user input could be cleared by a native-selection check before its accepting render, leaving
    native and declared selection different for good (reproduced in single and multiple modes);
  - a throw inside a radio transaction left the group half-settled and escaped to the platform
    dispatcher (reproduced).
- **Medium, fixed:**
  - the selection check ran an empty batch update, and so a relayout, on every commit;
  - a check queued from a callback was overwritten when the callback had set state first;
  - removing `.Semantics(description:)` lost a radio button's accessible name;
  - `Underline` and `Strikethrough` on runs replaced each other and cleared a hyperlink's underline;
  - many test oracles were strengthened: positive witnesses, real tap dispatch on iOS and Mac
    Catalyst, the Execute side below its threshold, the missing `SwipeView_ContentItems` fixture,
    and the planned mutants that hadn't been run.
- **Low, fixed:** label `LineHeight` and `TextTransform`, needless swipe rebuilds, the
  `onIsCheckedChanged` name, a drain that wasn't exception-safe, two batch updates per tap, and docs.

## Decisions for the gate

1. **CarouselView and IndicatorView move to 4c-4**, with the review's findings as their requirements.
   Recommendation: accept.
2. **Radio groups are Reactor's own.** Each native button gets an internal group name, and MAUI's
   `RadioButtonGroup` attached properties aren't supported. Recommendation: accept.
3. **Conflicting radio declarations are shown as declared, with a warning.** State stays the source
   of truth. Recommendation: accept.
4. **`SwipeAction` is renamed to `SwipeItemData`, and `onCheckedChanged` to `onIsCheckedChanged`**,
   matching WinUI. Both are breaking changes to a preview API. Recommendation: accept.

## Open issues and follow-ups

- **To file in dotnet/maui:**
  - iOS `ValidateAndSelectItem` doesn't check its item is still selected;
  - Android loops forever on a radio button written from its own `CheckedChanged`;
  - `BindableObject` keeps a property "being set" forever after its handler throws, so later writes
    queue forever;
  - Android's `EvaluateJavaScriptAsync` never completes when a navigation replaces the page;
  - a content swipe item's `Threshold` is capped at its width.
- **The verification when entering `None` has no device witness.** UIKit and Android already
  clear the native selection then, so it's covered headlessly only.
- **Not in 4c-2:** CarouselView and IndicatorView (4c-4); Windows (4g).
