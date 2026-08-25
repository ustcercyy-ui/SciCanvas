# Scientific Figure Workspace V2

This implementation introduces the V2 scientific-figure domain without replacing
the existing .NET 10/WPF/MVVM architecture. Source files remain immutable and final
exports continue to render from verified original files.

## Delivered behavior

- ScientificAsset is independent from FigurePanel; assets carry fingerprint,
  source revision, kind, metadata, tags, preview identity, link state and calibration.
- Panel layout is expressed as FigureRectMm; crop is a normalized source-space
  rectangle. Explicit Source to Panel to Figure transforms prevent resizing a panel
  from changing source-coordinate measurements.
- Fit, Fill and Manual Crop share the same crop calculator. Effective DPI uses the
  visible source pixels and the physical panel size rather than a hardcoded canvas DPI.
- Replace Source preserves frame, label, z-order and visual adjustments. Scale bars,
  measurements, ROI, Insets and colorbars receive explicit validity or review-required
  results when their scientific assumptions no longer hold.
- Source tracking verifies path, fingerprint, dimensions and timestamps. Relink keeps
  the asset identity; accepting changed content increments the source revision.
- Project Style resolves Project to Figure to Panel to Scientific Object with local
  overrides and reset/copy semantics. Built-in 2×2, 2×3 and 3×2 millimeter templates
  and reversible multi-panel layout mutations are available in Core.
- Scientific Figure QC is rule-based and deterministic. It covers canvas bounds,
  alignment, spacing, typography, effective DPI, calibration, scale bar validity,
  label completeness and source integrity. The project DPI threshold is configurable,
  persisted and enforced during export preflight.
- Auto Trim detects white/transparent preview borders, maps the suggestion back to
  immutable source pixels and only applies it as an undoable editor action.
- Project schema 2.0 persists workspace/Figure metadata, source revision, normalized
  crop, millimeter frame, fit mode and scientific validity. The explicit migration
  pipeline upgrades 0.1, 0.9, 1.1 and 1.2 documents and records an audit entry.

## UI

The desktop workspace now exposes Assets, Figures, Layers and Templates primary
navigation, a searchable Asset Library with type/usage/revision/link-state badges,
millimeter Panel Frame editing, Fit/Fill/Manual Crop, replacement validity, Project
Style inheritance guidance and a configurable Figure QC section.

## Verification

- Architecture audit: docs/V2_ARCHITECTURE_AUDIT.md
- Generated visual concept: docs/design/scicanvas-v2-workspace-concept.png
- Native WPF shell capture: artifacts/scicanvas-v2-workspace-qa.png
- Native WPF panel-inspector capture:
  artifacts/scicanvas-v2-workspace-qa-panel.png
- Build: zero warnings and zero errors.
- Automated tests: 52 Core tests and 104 Windows integration/UI tests.
- Dedicated visual smoke state: a calibrated scientific asset, Fill crop, scale bar,
  selected panel, Figure QC and 1600×1000 native WPF render.

## Compatibility notes

- Legacy pixel transforms remain serialized alongside V2 millimeter/normalized fields
  so existing export and recovery paths stay compatible.
- The domain and schema reserve arbitrary rotation and multi-Figure collections.
  The production WPF editor intentionally exposes only transforms that the current
  final exporters can reproduce end to end.
