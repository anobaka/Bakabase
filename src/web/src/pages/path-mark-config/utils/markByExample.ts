/**
 * Marking by example: you point at one folder you already have and say "everything at this level
 * is a resource", instead of working out which layer number that is.
 *
 * A layer mark is written relative to the folder the mark sits on, so what has to be computed is
 * how far the sample is below each of its ancestors. That is arithmetic on path segments and
 * nothing else, which is why it lives here rather than in a component.
 */

/** Splits a path into segments, tolerating either separator and a trailing one. */
export const segmentsOf = (path: string): string[] =>
  path
    .replace(/\\/g, "/")
    .split("/")
    .filter((s) => s.length > 0);

/** Normalized for comparison: one separator, no trailing one, case kept. */
export const normalizePath = (path: string): string => {
  const unified = path.replace(/\\/g, "/").replace(/\/+$/, "");

  // A Windows drive on its own ("C:") keeps its separator; a Unix root is just "/".
  return unified.length === 0 ? "/" : unified;
};

/**
 * How many levels below `rootPath` the sample sits, or null when the root is not above it.
 *
 * 1 means the sample is a direct child, which is what a layer mark of 1 means too — the numbering
 * is the backend's, not a new one.
 */
export const layerBetween = (rootPath: string, samplePath: string): number | null => {
  const root = segmentsOf(rootPath);
  const sample = segmentsOf(samplePath);

  if (sample.length <= root.length) return null;

  for (let i = 0; i < root.length; i++) {
    // Case-insensitively, because the same folder is spelled both ways on Windows and a mismatch
    // here would silently offer no ancestors at all.
    if (root[i].toLowerCase() !== sample[i].toLowerCase()) return null;
  }

  return sample.length - root.length;
};

/** One place the mark could go, and what its layer would be. */
export interface MarkByExampleCandidate {
  /** The folder the mark would sit on. */
  rootPath: string;
  /** What the mark's Layer would be, counted from that folder. */
  layer: number;
  /** Whether that folder already carries a mark, which usually means it is the intended root. */
  hasMarks: boolean;
}

/**
 * Where a mark inferred from this sample could go: every ancestor of it, nearest first.
 *
 * Every ancestor rather than a guess, because only the user knows which folder is the library —
 * the guess is which one to preselect, and an ancestor that already carries marks is the best
 * evidence available for that.
 */
export const markByExampleCandidates = (
  samplePath: string,
  pathHasMarks: (path: string) => boolean,
): MarkByExampleCandidate[] => {
  const segments = segmentsOf(samplePath);
  const normalized = normalizePath(samplePath);
  const isWindows = /^[a-z]:$/i.test(segments[0] ?? "");
  const candidates: MarkByExampleCandidate[] = [];

  // Down to 1, not 0: the sample's own folder is not somewhere a mark about the sample can go.
  for (let depth = segments.length - 1; depth >= 1; depth--) {
    const joined = segments.slice(0, depth).join("/");
    const rootPath = isWindows ? joined : `/${joined}`;
    const layer = layerBetween(rootPath, normalized);

    if (layer == null) continue;

    candidates.push({ rootPath, layer, hasMarks: pathHasMarks(rootPath) });
  }

  return candidates;
};

/**
 * Which candidate to offer first: the nearest ancestor that already carries a mark, and failing
 * that the sample's own parent — the reading of "everything at this level" that needs the least
 * explanation.
 */
export const preferredCandidate = (
  candidates: MarkByExampleCandidate[],
): MarkByExampleCandidate | undefined => candidates.find((c) => c.hasMarks) ?? candidates[0];
