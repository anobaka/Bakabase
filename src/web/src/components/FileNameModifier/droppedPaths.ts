const isAbsolutePath = (path: string) =>
  path.startsWith("/") || /^[a-zA-Z]:[\\/]/.test(path) || /^\\\\[^\\]+\\[^\\]+/.test(path);

function parsePath(value: string): string | null {
  const path = value.trim();

  if (path.startsWith("file://")) {
    try {
      const url = new URL(path);
      const pathname = decodeURIComponent(url.pathname);

      if (url.host && url.host !== "localhost")
        return `\\\\${url.host}${pathname.split("/").join("\\")}`;

      return /^\/[a-zA-Z]:\//.test(pathname) ? pathname.slice(1).split("/").join("\\") : pathname;
    } catch {
      return null;
    }
  }

  return isAbsolutePath(path) ? path : null;
}

function parseLines(value: string): string[] {
  if (!value.trim()) return [];

  try {
    const parsed: unknown = JSON.parse(value);

    // Bakabase's file explorer drags a JSON array of full paths.
    if (Array.isArray(parsed) && parsed.every((item) => typeof item === "string")) return parsed;
  } catch {
    // Ordinary path text and URI lists are not JSON.
  }

  return value.split(/\r?\n/).filter((line) => !line.trim().startsWith("#"));
}

export function getDroppedPaths(dataTransfer: DataTransfer): string[] {
  const candidates = [
    ...parseLines(dataTransfer.getData("text/plain")),
    ...parseLines(dataTransfer.getData("text/uri-list")),
    // Some embedded browsers expose this extension. Standard File.name is never a full path.
    ...Array.from(dataTransfer.files, (file) => (file as File & { path?: string }).path ?? ""),
  ];

  return [...new Set(candidates.map(parsePath).filter((path): path is string => !!path))];
}
