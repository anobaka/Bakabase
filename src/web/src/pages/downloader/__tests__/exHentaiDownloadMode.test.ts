import { describe, expect, it } from "vitest";

import { parseExHentaiDownloadOptions, resolveExHentaiDownloadMode } from "../exHentaiDownloadMode";

import { ExHentaiDownloadMode } from "@/sdk/constants";

describe("ExHentai acquisition settings", () => {
  it("preserves legacy torrent-file behavior without silently starting BT", () => {
    expect(resolveExHentaiDownloadMode()).toBe(ExHentaiDownloadMode.TorrentFile);
    expect(resolveExHentaiDownloadMode({ preferTorrent: true })).toBe(
      ExHentaiDownloadMode.TorrentFile,
    );
    expect(resolveExHentaiDownloadMode({ preferTorrent: false })).toBe(ExHentaiDownloadMode.Direct);
  });

  it("gives an explicit mode precedence over the legacy preference", () => {
    expect(
      resolveExHentaiDownloadMode({
        downloadMode: ExHentaiDownloadMode.Torrent,
        preferTorrent: false,
      }),
    ).toBe(ExHentaiDownloadMode.Torrent);
    expect(
      resolveExHentaiDownloadMode({
        downloadMode: ExHentaiDownloadMode.Direct,
        preferTorrent: true,
      }),
    ).toBe(ExHentaiDownloadMode.Direct);
  });

  it.each([undefined, "", "{broken", "null", "[]", '"text"'])(
    "opens settings with malformed legacy options: %s",
    (value) => {
      expect(parseExHentaiDownloadOptions(value)).toEqual({});
    },
  );

  it("preserves explicit no-fallback and seed retention options", () => {
    expect(
      parseExHentaiDownloadOptions(
        '{"downloadMode":2,"fallbackToDirect":false,"saveTorrentFile":true}',
      ),
    ).toEqual({ downloadMode: 2, fallbackToDirect: false, saveTorrentFile: true });
  });
});
