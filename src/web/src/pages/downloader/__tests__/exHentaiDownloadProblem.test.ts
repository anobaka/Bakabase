import { createInstance } from "i18next";
import { expect, it } from "vitest";
import cn from "@/locales/cn/pages/downloader.json";
import en from "@/locales/en/pages/downloader.json";
import { exHentaiDownloadProblemSummary } from "../exHentaiDownloadProblem";

it.each(["cn", "en"])(
  "explains the exact old no-torrent error in %s without rewriting it",
  async (language) => {
    const i18n = createInstance();
    await i18n.init({
      lng: language,
      resources: { cn: { translation: cn }, en: { translation: en } },
    });
    const message =
      "System.InvalidOperationException: This gallery has no torrent. Direct download fallback is disabled.\n   at Downloader()";
    const summary = exHentaiDownloadProblemSummary(message, i18n.t);
    expect(summary).toBe(
      (language === "cn" ? cn : en)["downloader.problem.exHentaiNoTorrentLegacy"],
    );
    expect(summary).not.toContain("System.InvalidOperationException");
    expect(message).toContain("at Downloader()");
    expect(
      exHentaiDownloadProblemSummary("The torrent page could not be read", i18n.t),
    ).toBeUndefined();
    expect(exHentaiDownloadProblemSummary("BT transfer has no peers", i18n.t)).toBeUndefined();
  },
);
