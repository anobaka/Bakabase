/*
 * Setting names on the map's cards. Measured, not rendered: the layout sizes cards from
 * names before anything is drawn, and the drawing shortens what still does not fit.
 */

const NARROW = new Set(Array.from("iljftrI1|!.,:;'`· -()[]"));
const WIDE = new Set(Array.from("mwMW@%"));

/** How wide one character sets, in ems, erring wide: a name must never run past its room. */
const charWidth = (char: string) => {
  if (char.codePointAt(0)! > 0x2e80) return 1; // CJK and the like are square.
  if (NARROW.has(char)) return 0.34;
  if (WIDE.has(char)) return 0.88;
  if (/[A-Z]/.test(char)) return 0.7;
  if (/\d/.test(char)) return 0.62;

  return 0.56;
};

/**
 * Roughly how wide a string sets in the UI's sans-serif — measured from character classes
 * rather than the browser, so the same names always give the same layout.
 */
export const textWidth = (text: string, fontSize: number) =>
  Array.from(text).reduce((sum, char) => sum + charWidth(char) * fontSize, 0);

/** Names are set semibold: a little wider than the same text in regular weight. */
export const SEMIBOLD = 1.06;

const ELLIPSIS = "…";

/** A line of words shortened at its end to fit — for what a card says under its name. */
export const fitEnd = (text: string, maxWidth: number, fontSize: number) => {
  if (textWidth(text, fontSize) <= maxWidth) return text;
  const chars = Array.from(text);

  while (chars.length > 1 && textWidth(`${chars.join("")}${ELLIPSIS}`, fontSize) > maxWidth)
    chars.pop();

  return `${chars.join("")}${ELLIPSIS}`;
};

/**
 * A name shortened in the middle to fit, keeping its end: default machine names share their
 * start ("DESKTOP-7F3K2QH", "DESKTOP-7F3K2QX") and differ at the end, which a cut at the end
 * would lose. `from` keeps the end from that character on, when there is room for it.
 */
export const fitMiddle = (text: string, maxWidth: number, fontSize: number, from?: number) => {
  if (textWidth(text, fontSize) <= maxWidth) return text;
  const chars = Array.from(text);
  const fits = (label: string) => textWidth(label, fontSize) <= maxWidth;

  if (from !== undefined) {
    // Where the name differs from another one: keep all of it after a little of the start.
    const tail = chars.slice(Math.max(1, from - 1)).join("");

    for (let head = Math.min(from - 1, 6); head >= 1; head--) {
      const label = `${chars.slice(0, head).join("")}${ELLIPSIS}${tail}`;

      if (fits(label)) return label;
    }
    if (fits(`${ELLIPSIS}${tail}`)) return `${ELLIPSIS}${tail}`;
    // Not even that: the part that differs, cut at both ends.
    const part = Array.from(tail);

    while (part.length > 1 && !fits(`${ELLIPSIS}${part.join("")}${ELLIPSIS}`)) part.pop();

    return `${ELLIPSIS}${part.join("")}${ELLIPSIS}`;
  }

  // As many characters as fit, a little more of the end than of the start.
  for (let kept = chars.length - 1; kept >= 2; kept--) {
    const tail = Math.ceil(kept * 0.55);
    const label = `${chars.slice(0, kept - tail).join("")}${ELLIPSIS}${chars
      .slice(chars.length - tail)
      .join("")}`;

    if (fits(label)) return label;
  }

  return `${chars[0]}${ELLIPSIS}`;
};

/**
 * Every card's name, shortened where it must be — and never to the same text as another
 * card's different name: where two would read alike, each keeps what tells it apart.
 */
export const fitNames = (
  items: { id: string; name: string; maxWidth: number; fontSize: number }[],
): Map<string, string> => {
  const labels = new Map(
    items.map((item) => [item.id, fitMiddle(item.name, item.maxWidth, item.fontSize)]),
  );
  const byLabel = new Map<string, typeof items>();

  for (const item of items) {
    const label = labels.get(item.id)!;

    byLabel.set(label, [...(byLabel.get(label) ?? []), item]);
  }
  for (const group of byLabel.values()) {
    if (new Set(group.map((item) => item.name)).size < 2) continue;
    for (const item of group) {
      const chars = Array.from(item.name);
      // The first character at which this name parts from any other in the group.
      const differs = Math.min(
        ...group
          .filter((other) => other.name !== item.name)
          .map((other) => {
            const theirs = Array.from(other.name);
            let at = 0;

            while (at < chars.length && chars[at] === theirs[at]) at++;

            return at;
          }),
      );

      labels.set(item.id, fitMiddle(item.name, item.maxWidth, item.fontSize, differs));
    }
  }

  return labels;
};
