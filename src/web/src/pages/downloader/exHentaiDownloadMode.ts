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
    return parsed && typeof parsed === "object" && !Array.isArray(parsed) ? parsed : {};
  } catch {
    return {};
  }
}
