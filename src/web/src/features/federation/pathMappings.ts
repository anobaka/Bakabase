import type { ManagedServerPathMapping } from "./types";

const isWindowsPath = (path: string) =>
  path.startsWith("//") ||
  (!path.startsWith("/") && (/^[a-z]:/i.test(path) || path.includes("\\")));
const segments = (path: string) => path.split(/[/\\]+/).filter(Boolean);
const remainder = (path: string, prefix: string): string | undefined => {
  if (prefix === "/")
    return path.startsWith("/") && !path.startsWith("//") ? path.replace(/^\/+/, "") : undefined;
  const parts = segments(path);
  const prefixParts = segments(prefix);

  if (
    !prefixParts.length ||
    prefixParts.length > parts.length ||
    !prefixParts.every((part, index) =>
      isWindowsPath(prefix)
        ? part.toLowerCase() === parts[index]!.toLowerCase()
        : part === parts[index],
    )
  )
    return undefined;

  return parts.slice(prefixParts.length).join("/");
};

/** Mirrors ManagedPathMapper: preview syntax belongs to the mapped paths, not the browser. */
export const mappedDirectoryPreview = (
  path: string,
  mappings: ManagedServerPathMapping[],
): string | undefined => {
  if (!path.trim()) return undefined;
  let best: { mapping: ManagedServerPathMapping; suffix: string } | undefined;

  for (const mapping of mappings) {
    if (!mapping.serverPath.trim() || !mapping.localPath.trim()) continue;
    const suffix = remainder(path, mapping.serverPath);

    if (suffix === undefined) continue;
    if (!best || mapping.serverPath.length > best.mapping.serverPath.length)
      best = { mapping, suffix };
  }
  if (!best) return undefined;
  const separator = isWindowsPath(best.mapping.localPath) ? "\\" : "/";
  let root = best.mapping.localPath.replace(/[/\\]+$/, "");

  if (!root && best.mapping.localPath.startsWith("/")) root = "/";
  if (/^[a-z]:$/i.test(root)) root += separator;

  return best.suffix.length
    ? `${root.replace(/[/\\]+$/, "")}${separator}${best.suffix.split("/").join(separator)}`
    : root;
};
