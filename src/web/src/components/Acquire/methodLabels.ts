import type { TFunction } from "i18next";
import type { DownloadMethod } from "./models";

export function downloadMethodLabel(
  method: Pick<DownloadMethod, "id" | "displayName">,
  t: TFunction,
): string {
  const key = `acquire.methodName.${method.id}`;
  const translated = t<string>(key);
  // parseMissingKeyHandler can return the key even when defaultValue was supplied.
  if (translated && translated !== key) return translated;

  const name = method.displayName?.trim();
  if (name) {
    // Catalog entries may use a translation token; never show an unresolved token.
    if (!/^(?:acquire|cloudDownload)\./.test(name)) return name;
    const translatedName = t<string>(name);
    if (translatedName && translatedName !== name) return translatedName;
  }
  const fallbackKey = "acquire.method.label";
  const fallback = t<string>(fallbackKey);
  return fallback && fallback !== fallbackKey ? fallback : method.id;
}
