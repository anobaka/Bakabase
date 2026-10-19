import { ExHentaiDownloadMode } from "@/sdk/constants";

export interface ExHentaiDownloadOptions {
  downloadMode?: ExHentaiDownloadMode | null;
  preferTorrent?: boolean | null;
  saveTorrentFile?: boolean | null;
  fallbackToDirect?: boolean | null;
}

export interface ExHentaiDownloadPatch {
  downloadMode?: ExHentaiDownloadMode;
  saveTorrentFile?: boolean;
  fallbackToDirect?: boolean;
}

/** An unset mode preserves the old save-torrent behavior of existing settings/tasks. */
export function resolveExHentaiDownloadMode(
  options?: ExHentaiDownloadOptions,
): ExHentaiDownloadMode {
  return (
    options?.downloadMode ??
    (options?.preferTorrent === false
      ? ExHentaiDownloadMode.Direct
      : ExHentaiDownloadMode.TorrentFile)
  );
}

export function parseExHentaiDownloadOptions(value?: string | null): ExHentaiDownloadOptions {
  try {
    const parsed = value ? JSON.parse(value) : {};
    if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) return {};
    const knownKeys: Record<string, string> = {
      downloadmode: "downloadMode",
      prefertorrent: "preferTorrent",
      savetorrentfile: "saveTorrentFile",
      fallbacktodirect: "fallbackToDirect",
    };
    // Match the server's case-insensitive options reader, preserving unrelated settings.
    return Object.fromEntries(
      Object.entries(parsed).map(([key, option]) => [knownKeys[key.toLowerCase()] ?? key, option]),
    );
  } catch {
    return {};
  }
}
