import type { BlockPlacement, DetailLayoutConfig, SectionId, SectionMeta } from "./types";

export const ALL_SECTIONS: SectionMeta[] = [
  { id: "cover", label: "Cover", heightBehavior: "fixed" },
  { id: "rating", label: "Rating", heightBehavior: "fixed" },
  { id: "actions", label: "Actions", heightBehavior: "fixed" },
  { id: "basicInfo", label: "Basic Info", heightBehavior: "fixed" },
  { id: "hierarchy", label: "Hierarchy", heightBehavior: "dynamic" },
  { id: "introduction", label: "Introduction", heightBehavior: "dynamic" },
  { id: "playedAt", label: "Played At", heightBehavior: "fixed" },
  { id: "properties", label: "Properties", heightBehavior: "dynamic" },
  { id: "relatedDataCards", label: "Related Data Cards", heightBehavior: "dynamic" },
  { id: "mediaLibs", label: "Media Libraries", heightBehavior: "dynamic" },
  { id: "profiles", label: "Profiles", heightBehavior: "dynamic" },
];

export const SECTION_HEIGHT_BEHAVIOR: Record<SectionId, "fixed" | "dynamic"> = ALL_SECTIONS.reduce(
  (acc, s) => {
    acc[s.id] = s.heightBehavior;

    return acc;
  },
  {} as Record<SectionId, "fixed" | "dynamic">,
);

const VALID_SECTION_IDS = new Set<SectionId>(ALL_SECTIONS.map((s) => s.id));

// Default: left column stacks fixed-height blocks; right column carries
// wider dynamic-height content. Row spans are rough estimates — users tune
// them in the designer. Runtime uses column-aware waterfall, so mismatched
// designer row heights just affect visual preview, not actual rendering.
export const DEFAULT_LAYOUT: DetailLayoutConfig = {
  modalWidthPercent: 80,
  modalHeightPercent: 85,
  gridCols: 12,
  gap: 12,
  blocks: [
    { id: "cover", colStart: 0, colSpan: 4, rowStart: 0, rowSpan: 6 },
    { id: "rating", colStart: 0, colSpan: 4, rowStart: 6, rowSpan: 1 },
    { id: "actions", colStart: 0, colSpan: 4, rowStart: 7, rowSpan: 1 },
    { id: "basicInfo", colStart: 0, colSpan: 4, rowStart: 8, rowSpan: 2 },
    { id: "mediaLibs", colStart: 0, colSpan: 4, rowStart: 10, rowSpan: 2 },
    { id: "profiles", colStart: 0, colSpan: 4, rowStart: 12, rowSpan: 2 },
    { id: "hierarchy", colStart: 4, colSpan: 8, rowStart: 0, rowSpan: 2 },
    { id: "introduction", colStart: 4, colSpan: 8, rowStart: 2, rowSpan: 3 },
    { id: "playedAt", colStart: 4, colSpan: 8, rowStart: 5, rowSpan: 1 },
    { id: "properties", colStart: 4, colSpan: 8, rowStart: 6, rowSpan: 6 },
    { id: "relatedDataCards", colStart: 0, colSpan: 12, rowStart: 14, rowSpan: 4 },
  ],
  hidden: [],
};

// Tolerant reader for stored layouts. Sanitizes block/hidden entries, drops
// unknown section ids, and falls back to DEFAULT_LAYOUT if blocks is empty
// or absent — which is also how pre-blocks (band-based) configs land here:
// the backend stripped the old `Bands` field, so they arrive empty.
export function normalizeLayoutConfig(raw: unknown): DetailLayoutConfig {
  if (!raw || typeof raw !== "object") return DEFAULT_LAYOUT;
  const r = raw as Partial<DetailLayoutConfig>;
  const gridCols = r.gridCols ?? DEFAULT_LAYOUT.gridCols;

  const blocks: BlockPlacement[] = Array.isArray(r.blocks)
    ? r.blocks
        .filter((b) => b && VALID_SECTION_IDS.has(b.id))
        .map((b) => ({
          id: b.id,
          colStart: Math.max(0, Math.min(gridCols - 1, b.colStart ?? 0)),
          colSpan: Math.max(1, Math.min(gridCols, b.colSpan ?? 1)),
          rowStart: Math.max(0, b.rowStart ?? 0),
          rowSpan: Math.max(1, b.rowSpan ?? 1),
        }))
    : [];

  if (blocks.length === 0) return DEFAULT_LAYOUT;

  const hidden: BlockPlacement[] = Array.isArray(r.hidden)
    ? r.hidden
        .filter((h) => h && VALID_SECTION_IDS.has(h.id))
        .map((h) => ({
          id: h.id,
          colStart: h.colStart ?? 0,
          colSpan: h.colSpan ?? 4,
          rowStart: h.rowStart ?? 0,
          rowSpan: h.rowSpan ?? 2,
        }))
    : [];

  return {
    modalWidthPercent: r.modalWidthPercent ?? DEFAULT_LAYOUT.modalWidthPercent,
    modalHeightPercent: r.modalHeightPercent ?? DEFAULT_LAYOUT.modalHeightPercent,
    gridCols,
    gap: r.gap ?? DEFAULT_LAYOUT.gap,
    blocks,
    hidden,
  };
}
