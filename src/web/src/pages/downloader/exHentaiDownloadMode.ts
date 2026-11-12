import { ExHentaiDownloadMode, ExHentaiNoTorrentAction } from "@/sdk/constants";

export interface ExHentaiDownloadOptions {
  downloadMode?: ExHentaiDownloadMode | null;
  preferTorrent?: boolean | null;
  saveTorrentFile?: boolean | null;
  fallbackToDirect?: boolean | null;
  noTorrentAction?: ExHentaiNoTorrentAction | null;
}

export interface ExHentaiDownloadPatch {
  downloadMode?: ExHentaiDownloadMode;
  saveTorrentFile?: boolean;
  fallbackToDirect?: boolean;
  preferTorrent?: boolean;
  noTorrentAction?: ExHentaiNoTorrentAction;
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
      notorrentaction: "noTorrentAction",
    };
    // Match the server's case-insensitive options reader, preserving unrelated settings.
    return Object.fromEntries(
      Object.entries(parsed).map(([key, option]) => [knownKeys[key.toLowerCase()] ?? key, option]),
    );
  } catch {
    return {};
  }
}

/** New tasks have an explicit fallback policy; reading old tasks must retain their old route. */
export function resolveExHentaiPolicyMode(
  options?: ExHentaiDownloadOptions,
  isExistingTask = false,
): ExHentaiDownloadMode {
  if (isExistingTask) return resolveExHentaiDownloadMode(options);
  return (
    options?.downloadMode ??
    (options?.preferTorrent === false ? ExHentaiDownloadMode.Direct : ExHentaiDownloadMode.Torrent)
  );
}

export function exHentaiStopsWithoutTorrent(
  options: ExHentaiDownloadOptions | undefined,
  isExistingTask: boolean,
): boolean {
  const mode = resolveExHentaiPolicyMode(options, isExistingTask);
  if (mode === ExHentaiDownloadMode.Direct) return false;
  if (options?.noTorrentAction != null)
    return options.noTorrentAction === ExHentaiNoTorrentAction.Fail;
  if (!isExistingTask) return false;
  return mode === ExHentaiDownloadMode.TorrentFile || options?.fallbackToDirect === false;
}

/** Called only after an explicit user choice, never while mounting or reading legacy settings. */
export function exHentaiDownloadPolicyPatch(
  preferTorrent: boolean,
  saveTorrentFile: boolean,
  downloadContents: boolean,
): ExHentaiDownloadPatch {
  return {
    downloadMode: !preferTorrent
      ? ExHentaiDownloadMode.Direct
      : downloadContents
        ? ExHentaiDownloadMode.Torrent
        : ExHentaiDownloadMode.TorrentFile,
    preferTorrent,
    saveTorrentFile: saveTorrentFile && downloadContents,
    fallbackToDirect: true,
    noTorrentAction: ExHentaiNoTorrentAction.DirectDownload,
  };
}
