/**
 * Product code (番号) extraction.
 *
 * Input is almost always pasted from somewhere else: one per line, comma
 * separated, mixed with URLs and titles, full-width and half-width jumbled
 * together. The strategy is normalize first, then match token by token, and
 * lean towards over-matching — every hit shows up as a removable chip, so a
 * false positive costs one click while a miss costs a manual retype.
 */

/** Full-width -> half-width, and every dash variant -> ASCII hyphen. */
export function normalizeText(input: string): string {
  return input
    .replace(/[！-～]/g, (c) => String.fromCharCode(c.charCodeAt(0) - 0xfee0))
    .replace(/　/g, " ")
    .replace(/[‐-―−ー]/g, "-");
}

/** `SSIS 123` -> `SSIS-123`, otherwise tokenizing would split the pair apart. */
function joinSpaced(text: string): string {
  return text.replace(
    /([A-Za-z]{2,7})[ \t]+(\d{2,6})(?![0-9])/g,
    (_m, letters, digits) => `${letters}-${digits}`,
  );
}

/**
 * One token -> a code, or null when it doesn't look like one. Covers
 * `SSIS-123` / `ssis123` / `300MAAN-456` / `SAVR-01069` / `012345_001`, and
 * eats a trailing `-C` / `-UC` subtitle marker since javbus indexes the bare
 * code (the subtitled release shows up as one of its magnets, not a page).
 */
export function normalizeCode(token: string): string | null {
  const t = token.toUpperCase().replace(/^[-_]+|[-_]+$/g, "");

  // Uncensored date-serial style: 012345_001
  const serial = t.match(/^(\d{6})[-_](\d{2,4})$/);

  if (serial) return `${serial[1]}_${serial[2]}`;

  const standard = t.match(/^(\d{0,4}[A-Z]{2,7})[-_]?(\d{2,6})(?:-?(?:C|U|UC|CH))?$/);

  if (standard) return `${standard[1]}-${standard[2]}`;

  return null;
}

function dedupe(values: string[]): string[] {
  const seen = new Set<string>();

  return values.filter((v) => (seen.has(v) ? false : (seen.add(v), true)));
}

/**
 * A leftover line is only worth keeping when it might be a code the parser
 * missed. Prose, URLs and stray punctuation just clutter the box, so require
 * a token carrying at least two letters and two digits — that keeps
 * `FC2-PPV-123456` around while dropping `mp4`, `x264` and `1080P`.
 */
function looksSalvageable(line: string): boolean {
  return (line.match(/[0-9A-Za-z_-]+/g) ?? []).some(
    (t) => (t.match(/[A-Za-z]/g) ?? []).length >= 2 && (t.match(/\d/g) ?? []).length >= 2,
  );
}

export interface ExtractResult {
  codes: string[];
  /** Unparsed leftovers worth showing, so a missed code isn't lost silently. */
  rest: string;
  /** How many leftover lines were dropped as noise. */
  ignored: number;
}

export function extractCodes(raw: string): ExtractResult {
  const text = joinSpaced(normalizeText(raw));
  const tokenRe = /[0-9A-Za-z][0-9A-Za-z_-]*/g;
  const codes: string[] = [];
  const rest: string[] = [];
  let last = 0;
  let m: RegExpExecArray | null;

  while ((m = tokenRe.exec(text)) !== null) {
    const code = normalizeCode(m[0]);

    rest.push(text.slice(last, m.index));
    if (code) codes.push(code);
    else rest.push(m[0]);
    last = m.index + m[0].length;
  }
  rest.push(text.slice(last));

  const leftover = rest
    .join("")
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean);
  const kept = leftover.filter(looksSalvageable);

  return {
    codes: dedupe(codes),
    rest: kept.join("\n"),
    ignored: leftover.length - kept.length,
  };
}

/**
 * Auto-extraction leaves the fragment after the last separator alone — the
 * user may be halfway through typing, and turning `SSIS-12` into a chip right
 * before they hit `3` is maddening.
 */
export function splitPending(text: string): [head: string, pending: string] {
  const trailing = text.match(/[0-9A-Za-z_-]*$/);
  const cut = text.length - (trailing ? trailing[0].length : 0);

  return [text.slice(0, cut), text.slice(cut)];
}

/** Escape hatch: no recognition at all, just split on lines/commas as-is. */
export function splitVerbatim(raw: string): string[] {
  return dedupe(
    normalizeText(raw)
      .split(/[\r\n,;|]+/)
      .map((s) => s.trim())
      .filter(Boolean),
  );
}
