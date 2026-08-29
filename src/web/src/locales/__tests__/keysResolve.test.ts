import { readFileSync, readdirSync, statSync } from "node:fs";
import { join, resolve } from "node:path";

import { describe, expect, it } from "vitest";

/**
 * Guards against a translation key that no locale file answers.
 *
 * The failure mode is quiet: i18next renders the key itself, so a mistyped or
 * wrongly-nested key looks like a label until someone reads the screen. This
 * caught a real one — a whole settings section added as a nested object while
 * its file uses flat dotted keys, so every lookup missed by one path segment.
 *
 * Nesting itself is fine; several files nest deliberately. What has to hold is
 * that the full dotted path a component asks for exists.
 */

const localesDir = resolve(__dirname, "..");
const srcDir = resolve(__dirname, "../..");

/**
 * Keys that were already unanswered before this check existed. They are real
 * gaps, listed rather than fixed so the check can go in without dragging along
 * unrelated changes — remove an entry when you add its translation.
 */
const KNOWN_MISSING = new Set([
  "FileNameModifier.Title",
  "Position.Beginning",
  "Position.End",
  "TextOperation.Backward",
  "TextOperation.Forward",
  "common.empty.noData",
  "common.error.invalidData",
  "common.label.selected",
  "common.label.tip",
  "common.success.deleted",
  "common.unit.months",
  "common.unit.years",
  "comparison.validation.propertyRequired",
  "comparison.validation.regexPatternRequired",
  "comparison.validation.vetoThresholdRange",
  "comparison.validation.weightPositive",
  "configuration.thirdParty.configure",
]);

const walk = (dir: string, match: (path: string) => boolean): string[] => {
  const out: string[] = [];

  for (const entry of readdirSync(dir)) {
    const path = join(dir, entry);

    if (statSync(path).isDirectory()) {
      if (entry === "node_modules" || entry === "sdk") continue;
      out.push(...walk(path, match));
    } else if (match(path)) {
      out.push(path);
    }
  }

  return out;
};

const flatten = (value: Record<string, unknown>, prefix = ""): string[] =>
  Object.entries(value).flatMap(([key, child]) =>
    child !== null && typeof child === "object" && !Array.isArray(child)
      ? flatten(child as Record<string, unknown>, `${prefix}${key}.`)
      : [`${prefix}${key}`],
  );

const loadLocale = (locale: string): Set<string> => {
  const keys = new Set<string>();

  for (const file of walk(join(localesDir, locale), (p) => p.endsWith(".json"))) {
    for (const key of flatten(JSON.parse(readFileSync(file, "utf8")))) {
      keys.add(key);
    }
  }

  return keys;
};

/**
 * Only fully-literal keys. An interpolated key (`t(\`a.b.${x}\`)`) cannot be
 * resolved statically, so it is left to the reader.
 */
const LOOKUP = /\bt(?:<[^>]*>)?\(\s*["']([a-zA-Z][\w]*(?:\.[\w]+)+)["']/g;

const collectUsedKeys = (): Map<string, string> => {
  const used = new Map<string, string>();

  for (const file of walk(srcDir, (p) => p.endsWith(".tsx") || p.endsWith(".ts"))) {
    const text = readFileSync(file, "utf8");

    for (const [, key] of text.matchAll(LOOKUP)) {
      if (!used.has(key)) {
        used.set(key, file.slice(srcDir.length + 1));
      }
    }
  }

  return used;
};

describe("translation keys", () => {
  const en = loadLocale("en");
  const cn = loadLocale("cn");
  const used = collectUsedKeys();

  it("finds keys to check", () => {
    expect(en.size).toBeGreaterThan(1000);
    expect(used.size).toBeGreaterThan(100);
  });

  it("every key a component asks for exists in both locales", () => {
    const missing: string[] = [];

    for (const [key, file] of used) {
      if (KNOWN_MISSING.has(key)) continue;

      const absent = [!en.has(key) && "en", !cn.has(key) && "cn"].filter(Boolean);

      if (absent.length > 0) {
        missing.push(`${key} — missing from ${absent.join(", ")} (used in ${file})`);
      }
    }

    expect(missing, `\n${missing.join("\n")}\n`).toEqual([]);
  });

  it("keeps the known-missing list honest", () => {
    // An entry that has since been translated should leave the list, otherwise
    // it quietly re-permits the same key going missing again later.
    const stale = [...KNOWN_MISSING].filter((key) => en.has(key) && cn.has(key));

    expect(stale, `translated but still allowlisted: ${stale.join(", ")}`).toEqual([]);
  });
});
