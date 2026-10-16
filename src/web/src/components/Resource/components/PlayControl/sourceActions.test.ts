import { describe, expect, it } from "vitest";

import { originalSourceActions, sourceActionTarget } from "./sourceActions";

import { ResourceSource } from "@/sdk/constants";

describe("validated source action targets", () => {
  it.each(["RJ123456", "BJ01234", "vj10000001"])("builds canonical DLsite URL for %s", (key) => {
    expect(sourceActionTarget(ResourceSource.DLsite, key)).toEqual({
      kind: "web",
      href: `https://www.dlsite.com/maniax/work/=/product_id/${key.toUpperCase()}.html`,
    });
  });
  it("builds gallery and application targets separately", () => {
    expect(sourceActionTarget(ResourceSource.ExHentai, "123/abc123")).toEqual({
      kind: "web",
      href: "https://exhentai.org/g/123/abc123/",
    });
    expect(sourceActionTarget(ResourceSource.Steam, "123")).toEqual({
      kind: "protocol",
      href: "steam://rungameid/123",
    });
  });
  it.each([
    "javascript:alert(1)",
    "file:///tmp/test",
    "https://evil.example/",
    "123/../../../",
    "123%0A--command",
    "123?command=x",
    "123\\evil",
    "",
  ])("rejects non-identifiers: %s", (key) => {
    for (const origin of [ResourceSource.Steam, ResourceSource.DLsite, ResourceSource.ExHentai]) {
      expect(sourceActionTarget(origin, key)).toBeUndefined();
    }
  });
});

it("keeps origin enums distinct and deduplicates original links, including Steam store", () => {
  const links = [
    { source: ResourceSource.DLsite, sourceKey: "RJ123456" },
    { source: ResourceSource.DLsite, sourceKey: "rj123456" },
    { source: ResourceSource.ExHentai, sourceKey: "12/abc" },
    { source: ResourceSource.Steam, sourceKey: "123" },
    { source: ResourceSource.PathMark, sourceKey: "RJ123456" },
    { source: ResourceSource.ExHentai, sourceKey: "javascript:alert(1)" },
  ] as any;

  expect(originalSourceActions(links)).toEqual([
    {
      source: ResourceSource.DLsite,
      href: "https://www.dlsite.com/maniax/work/=/product_id/RJ123456.html",
    },
    { source: ResourceSource.ExHentai, href: "https://exhentai.org/g/12/abc/" },
    { source: ResourceSource.Steam, href: "https://store.steampowered.com/app/123/" },
  ]);
});
