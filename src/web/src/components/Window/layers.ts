/** Floating players stay above ordinary page dialogs, including resource details. */
export const WINDOW_Z_INDEX_MIN = 1000;
export const WINDOW_Z_INDEX_MAX = 2000;

/** Opt in only for dialogs opened from a floating window. Keep both portal siblings above it. */
export const windowModalClassNames = {
  backdrop: "z-[2100]",
  wrapper: "z-[2101]",
};

/** A child picker must also cover its parent window dialog and backdrop. */
export const nestedWindowModalClassNames = {
  backdrop: "z-[2200]",
  wrapper: "z-[2201]",
};
