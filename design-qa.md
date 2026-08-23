# SciCanvas screenshot design QA

## Evidence

- Source visual truth: `D:\Temp\codex-clipboard-c90006b1-f878-48d6-b3a7-70e10c16471c.png` (1487 × 1058 px, supplied screenshot).
- Implementation capture: `E:\picture pin\artifacts\SciCanvas-ui-smoke.png` (1440 × 900 px, native WPF window capture).
- Density: both captures are desktop screenshots; no device-pixel downsampling was applied. The comparison uses the shared application chrome and pane structure rather than pixel-perfect scaling.
- Implementation state: fresh project shell with the same dark desktop workbench chrome; the supplied source shows a loaded crop state, so image-content details are treated as a follow-up state check.

## Findings

No actionable P0/P1/P2 visual findings remain for the requested shell redesign.

- The implementation now has a two-level dark menu/toolbar header, matching the source hierarchy.
- The source-image, canvas, and inspector regions remain distinct and now have two visible drag splitters so each major pane can be resized.
- The right pane has an inspector/layers header with an accent-selected state, matching the source's right-side information architecture.
- Selected collage panels expose a cyan bottom-right resize handle; the handle respects locked-panel state and the source aspect-ratio setting.
- The top menu is now a real WPF `Menu` bound to project, edit, view, image, crop, collage, and template commands instead of decorative text.
- The inspector/layers header now switches between two visible pages; the layers page exposes visibility, lock, multi-selection, reorder, and remove actions.
- Collage panel pointer handling now uses preview mouse events so a panel can be dragged even when the ListBox selection handler would otherwise consume the click; the resize handle remains isolated.
- Brightness and contrast each have a continuous `-100` to `+100` slider (`IsSnapToTickEnabled=False`) plus a synchronized numeric field.
- The Help menu now reports the app version and keyboard shortcuts through the status bar instead of remaining disabled.

## Focused comparison

The focused comparison covered the top chrome, the left/center/right pane boundaries, the right inspector header, and the selected-panel affordance. Rulers and loaded-image content were not compared because the smoke capture intentionally used a fresh project; this is a state-coverage gap, not a shell-layout blocker.

## Comparison history

1. Initial implementation: fixed three-column layout and single header row.
2. Fix applied: added menu row, toolbar row, bounded `GridSplitter` columns, inspector/layers header, and a functional collage resize handle.
3. Post-fix evidence: `artifacts/SciCanvas-ui-smoke.png`; app launched successfully at 1440 × 900 and exited cleanly after capture.

## Follow-up polish

- Capture one more QA state with a real source image loaded to compare ruler density, crop handles, and thumbnail rhythm against the supplied screenshot.
- If needed, add persisted pane widths so splitter positions survive restart.

final result: passed
