import { describe, expect, it } from "vitest";

import { audienceOf, notices } from "../registry";

import cnNotices from "@/locales/cn/components/notices.json";
import enNotices from "@/locales/en/components/notices.json";
import { devicesRoute } from "@/features/federation/switching";
import { helpTopics } from "@/components/HelpCenter/topics";

const en = enNotices as Record<string, string>;
const cn = cnNotices as Record<string, string>;

/** `{{name}}` placeholders, sorted, so both languages can be compared as sets. */
const placeholders = (text: string) =>
  [...text.matchAll(/{{\s*(\w+)\s*}}/g)].map((match) => match[1]).sort();

const keysOf = (notice: (typeof notices)[number]) => [
  notice.titleKey,
  notice.bodyKey,
  ...(notice.pointKeys ?? []),
  ...(notice.action ? [notice.action.labelKey] : []),
];

describe("notice registry", () => {
  it("gives every notice an id that can be stored and never collides", () => {
    const ids = notices.map((notice) => notice.id);

    expect(new Set(ids).size).toBe(ids.length);
    for (const id of ids) {
      // A short slug: the server ignores ids longer than 128 characters.
      expect(id).toMatch(/^[a-z0-9]+(-[a-z0-9]+)*$/);
      expect(id.length).toBeLessThanOrEqual(128);
    }
  });

  it("orders notices unambiguously", () => {
    const orders = notices.map((notice) => notice.order);

    expect(new Set(orders).size).toBe(orders.length);
  });

  it("has every text in both languages, with the same placeholders", () => {
    const keys = notices.flatMap(keysOf);

    for (const key of keys) {
      expect(en, key).toHaveProperty([key]);
      expect(cn, key).toHaveProperty([key]);
    }
  });

  it("keeps the notices namespace complete in both languages", () => {
    expect(Object.keys(cn).sort()).toEqual(Object.keys(en).sort());
    for (const key of Object.keys(en)) {
      expect(en[key]!.trim(), key).not.toBe("");
      expect(cn[key]!.trim(), key).not.toBe("");
      expect(placeholders(cn[key]!), key).toEqual(placeholders(en[key]!));
    }
    expect(placeholders(en["notices.introducedIn"]!)).toEqual(["version"]);
    expect(placeholders(en["notices.dialog.position"]!)).toEqual(["current", "total"]);
  });

  it("only points actions at places that exist", () => {
    const topicIds = new Set(helpTopics.map((topic) => topic.id));

    for (const notice of notices) {
      const action = notice.action;

      if (action?.kind === "help") expect(topicIds.has(action.topic)).toBe(true);
      if (action?.kind === "route") expect(action.route.startsWith("/")).toBe(true);
    }
  });

  it("ships the multi-device notice, opening the multi-device guide", () => {
    const notice = notices.find((item) => item.id === "multi-device")!;

    expect(notice.action).toMatchObject({ kind: "help", topic: "multiDevice" });
    expect(audienceOf(notice)).toEqual(["local"]);
    expect(notice.upgradeOnly).toBe(true);
    expect(notice.showOnFreshInstallWhen).toBeUndefined();
  });

  it("ships the thin client's retirement as an upgrade-only notice for this device's window", () => {
    const notice = notices.find((item) => item.id === "thin-client-discontinued")!;

    expect(notice.action).toEqual({
      kind: "route",
      labelKey: "notices.item.thinClient.action",
      route: devicesRoute("servers"),
    });
    expect(audienceOf(notice)).toEqual(["local"]);
    expect(notice.upgradeOnly).toBe(true);
    // ...and for a fresh install whose first start brought the thin client's pairings over.
    expect(notice.showOnFreshInstallWhen).toBe("thinClientPairingsImported");
  });

  it("defaults the audience to this install's own window", () => {
    expect(audienceOf({ ...notices[0]!, audience: undefined })).toEqual(["local"]);
  });
});
