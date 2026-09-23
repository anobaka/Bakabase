import { beforeEach, describe, expect, it, vi } from "vitest";

import {
  parseConnectionHints,
  mergeConnectionHints,
  saveConnectionHintDraft,
  loadConnectionHintDraft,
  clearConnectionHintDraft,
  CONNECTION_HINT_DRAFT_KEY,
} from "../migration";

const hints = () => ({
  format: "bakabase-client-connection-hints",
  version: 1,
  servers: [
    {
      name: "Media PC",
      address: "192.168.1.8:34567",
      pathMappings: [{ serverPath: "D:/Media", localPath: "/Volumes/Media" }],
    },
  ],
});

describe("migration credential boundary", () => {
  it("copies only named fields and strips credentials, options, identifiers and unknown nested data", () => {
    const input = hints();
    const result = parseConnectionHints({
      ...input,
      deviceKey: "secret",
      options: { updateFeed: "client-feed" },
      servers: input.servers.map((server) => ({
        ...server,
        deviceKey: "admin-secret",
        serverId: "old-id",
        isActive: true,
        pathMappings: server.pathMappings.map((mapping) => ({
          ...mapping,
          sourceRootId: "do-not-reuse",
          grantId: "secret",
        })),
      })),
    });

    expect(result).toEqual({
      ...input,
      servers: [{ ...input.servers[0], address: "http://192.168.1.8:34567" }],
    });
    expect(JSON.stringify(result)).not.toMatch(/secret|grantId|sourceRootId|updateFeed|old-id/);
  });
  it("refuses credential-bearing addresses and unsupported file formats", () => {
    for (const address of [
      "https://admin:key@example.com",
      "https://example.com?deviceKey=secret",
      "https://example.com#key=secret",
      "file:///data/library.db",
      "javascript:alert(1)",
    ]) {
      expect(
        () => parseConnectionHints({ ...hints(), servers: [{ address, pathMappings: [] }] }),
        address,
      ).toThrow();
    }
    for (const value of [
      null,
      [],
      { ...hints(), version: 2 },
      { ...hints(), format: "options" },
      { ...hints(), servers: new Array(65).fill(hints().servers[0]) },
    ])
      expect(() => parseConnectionHints(value)).toThrow();
  });
  it("treats legacy path prefixes only as text hints, never as current mapping IDs", () => {
    const result = parseConnectionHints(hints());

    expect(Object.keys(result.servers[0].pathMappings[0])).toEqual(["serverPath", "localPath"]);
  });
});

describe("persistent migration drafts", () => {
  beforeEach(() => localStorage.clear());
  it("restores only whitelisted hints and merges repeat imports by normalized address and path pair", () => {
    const input = { ...hints(), deviceKey: "secret", options: { updateFeed: "legacy" } };
    const once = mergeConnectionHints(undefined, input);
    const twice = mergeConnectionHints(once, {
      ...input,
      servers: [{ ...input.servers[0], address: "http://192.168.1.8:34567/" }],
    });

    expect(twice).toEqual(once);
    saveConnectionHintDraft(twice);
    expect(loadConnectionHintDraft()).toEqual(once);
    expect(localStorage.getItem(CONNECTION_HINT_DRAFT_KEY)).not.toMatch(
      /secret|deviceKey|options|updateFeed/,
    );
    const spy = vi.spyOn(Storage.prototype, "setItem");

    saveConnectionHintDraft(twice);
    expect(spy).not.toHaveBeenCalled();
    spy.mockRestore();
  });
  it("keeps conflicting old paths as hints until a user explicitly binds a current root", () => {
    const previous = parseConnectionHints(hints());
    const merged = mergeConnectionHints(previous, {
      ...hints(),
      servers: [
        {
          ...hints().servers[0],
          pathMappings: [{ serverPath: "D:/Media", localPath: "/Volumes/Other" }],
        },
      ],
    });

    expect(merged.servers).toHaveLength(1);
    expect(merged.servers[0].pathMappings).toHaveLength(2);
    expect(JSON.stringify(merged)).not.toContain("sourceRootId");
  });
  it("rejects corrupt stored drafts and clearing affects only our hints key", () => {
    localStorage.setItem("unrelated", "keep");
    localStorage.setItem(CONNECTION_HINT_DRAFT_KEY, '{"deviceKey":"secret"}');
    expect(loadConnectionHintDraft).toThrow();
    clearConnectionHintDraft();
    expect(loadConnectionHintDraft()).toBeUndefined();
    expect(localStorage.getItem("unrelated")).toBe("keep");
  });
});
