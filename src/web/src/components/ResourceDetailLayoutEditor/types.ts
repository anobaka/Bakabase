export type SectionId =
  | "cover"
  | "rating"
  | "actions"
  | "basicInfo"
  | "hierarchy"
  | "introduction"
  | "playedAt"
  | "properties"
  | "relatedDataCards"
  | "mediaLibs"
  | "profiles";

// A single section placed on the grid. Width (colSpan) and column position
// (colStart) are stable — they are honored exactly at runtime. Row position
// (rowStart) and height (rowSpan) are only a stacking-priority hint: at
// runtime the waterfall packer re-derives actual Y based on content-driven
// heights of blocks above this one whose columns overlap.
export type BlockPlacement = {
  id: SectionId;
  colStart: number;
  colSpan: number;
  rowStart: number;
  rowSpan: number;
};

export type DetailLayoutConfig = {
  modalWidthPercent: number;
  modalHeightPercent: number;
  gridCols: number;
  gap: number;
  blocks: BlockPlacement[];
  // Hidden blocks keep their col/row info so restore snaps back to the same
  // spot. If that spot is now occupied, the restore logic bumps neighbors.
  hidden: BlockPlacement[];
};

// Whether a section's height is driven by its content (dynamic) or roughly
// fixed regardless of data (fixed). Used to flag that the designer's
// stacking is approximate — at runtime, dynamic blocks may shift below
// their designer position.
export type SectionHeightBehavior = "fixed" | "dynamic";

export type SectionMeta = {
  id: SectionId;
  label: string;
  heightBehavior: SectionHeightBehavior;
};

export type ComputedPosition = {
  top: number;
  left: number;
  width: number;
  height: number;
};
