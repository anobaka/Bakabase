import { describe, expect, it } from "vitest";

import {
  parseExHentaiDownloadOptions,
  resolveExHentaiDownloadMode,
  resolveExHentaiPolicyMode,
  exHentaiStopsWithoutTorrent,
} from "../exHentaiDownloadMode";

import { ExHentaiDownloadMode, ExHentaiNoTorrentAction } from "@/sdk/constants";

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

  it("normalizes legacy known field casing while retaining unrelated settings", () => {
    const options = parseExHentaiDownloadOptions(
      '{"DownloadMode":2,"SaveTorrentFile":true,"FallbackToDirect":false,"PreferTorrent":false,"CustomSetting":"keep"}',
    );
    expect(options).toEqual({
      downloadMode: 2,
      saveTorrentFile: true,
      fallbackToDirect: false,
      preferTorrent: false,
      CustomSetting: "keep",
    });
    expect(resolveExHentaiDownloadMode(options)).toBe(ExHentaiDownloadMode.Torrent);
  });

  it("uses the final case-insensitive value just like the server", () => {
    expect(parseExHentaiDownloadOptions('{"downloadMode":3,"DOWNLOADMODE":2}')).toEqual({
      downloadMode: 2,
    });
  });

  it("defaults only future tasks to internal BT while existing tasks retain torrent-file semantics", () => {
    expect(resolveExHentaiPolicyMode()).toBe(ExHentaiDownloadMode.Torrent);
    expect(resolveExHentaiPolicyMode(undefined, true)).toBe(ExHentaiDownloadMode.TorrentFile);
    expect(resolveExHentaiPolicyMode({ preferTorrent: false })).toBe(ExHentaiDownloadMode.Direct);
    expect(resolveExHentaiPolicyMode({ downloadMode: ExHentaiDownloadMode.TorrentFile })).toBe(
      ExHentaiDownloadMode.TorrentFile,
    );
  });

  it("distinguishes explicit fallback, historical torrent-only tasks and future defaults", () => {
    const legacy = { downloadMode: ExHentaiDownloadMode.TorrentFile, fallbackToDirect: true };
    expect(exHentaiStopsWithoutTorrent(legacy, true)).toBe(true);
    expect(exHentaiStopsWithoutTorrent(legacy, false)).toBe(false);
    expect(
      exHentaiStopsWithoutTorrent(
        { ...legacy, noTorrentAction: ExHentaiNoTorrentAction.DirectDownload },
        true,
      ),
    ).toBe(false);
    expect(
      exHentaiStopsWithoutTorrent(
        {
          downloadMode: ExHentaiDownloadMode.Torrent,
          noTorrentAction: ExHentaiNoTorrentAction.Fail,
        },
        false,
      ),
    ).toBe(true);
    expect(parseExHentaiDownloadOptions('{"NoTorrentAction":1,"CustomSetting":"keep"}')).toEqual({
      noTorrentAction: 1,
      CustomSetting: "keep",
    });
  });
});
