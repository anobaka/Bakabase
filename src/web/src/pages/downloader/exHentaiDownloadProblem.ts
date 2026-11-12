import type { TFunction } from "i18next";

const legacyNoTorrentMessage = "This gallery has no torrent. Direct download fallback is disabled.";

/** Explain this exact historical diagnostic without rewriting the saved error or guessing other failures. */
export function exHentaiDownloadProblemSummary(
  message: string | null | undefined,
  t: TFunction,
): string | undefined {
  return message?.includes(legacyNoTorrentMessage)
    ? t<string>("downloader.problem.exHentaiNoTorrentLegacy")
    : undefined;
}
