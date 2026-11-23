import { beforeEach, expect, it, vi } from "vitest";
import { acquisitionListPreferencesKey, readAcquisitionListPreferences, writeAcquisitionListPreferences } from "../listPreferences";

beforeEach(() => { localStorage.clear(); vi.restoreAllMocks(); });

it("removes the retired plan tag while preserving specific filters and search preferences", () => {
  localStorage.setItem(acquisitionListPreferencesKey(), JSON.stringify({ version: 1,
    stage: "attention", keyword: "#940", sort: "sizeAsc",
    attentionTags: ["planNeedsInput", "downloadSourceUnsupported"], facts: ["downloadedPending"] }));
  expect(readAcquisitionListPreferences()).toEqual({ stage: "attention", keyword: "#940", sort: "sizeAsc",
    attentionTags: ["downloadSourceUnsupported"], facts: ["downloadedPending"] });
});

it("fails safely for unavailable storage and malformed or future records", () => {
  for (const value of ["not-json", "null", JSON.stringify({ version: 2 }), JSON.stringify({ version: 1,
    stage: "bad", keyword: {}, sort: "bad", attentionTags: [null, "localStorage", "localStorage", "../unsafe"], facts: ["bad", "spaceInsufficient"] })]) {
    localStorage.setItem(acquisitionListPreferencesKey(), value);
    const result = readAcquisitionListPreferences();
    expect(result.stage).toBe("all"); expect(result.keyword).toBe(""); expect(result.sort).toBe("newest");
    expect(result.attentionTags.every(tag => tag === "localStorage")).toBe(true);
    expect(result.facts.every(fact => fact === "spaceInsufficient")).toBe(true);
  }
  vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => { throw new Error("denied"); });
  expect(readAcquisitionListPreferences().stage).toBe("all");
  vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => { throw new Error("denied"); });
  expect(() => writeAcquisitionListPreferences(readAcquisitionListPreferences())).not.toThrow();
});

it("persists a known Download Center source key and drops invalid or unsupported saved choices", () => {
  const saved = { ...readAcquisitionListPreferences(), downloadSource: "site:14", keyword: "archive" };
  writeAcquisitionListPreferences(saved);
  expect(readAcquisitionListPreferences()).toMatchObject({ downloadSource: "site:14", keyword: "archive" });
  for (const invalid of ["baidu.pcs", "site:999999", "site:014", "../http", [], null]) {
    localStorage.setItem(acquisitionListPreferencesKey(), JSON.stringify({ version: 1, downloadSource: invalid }));
    expect(readAcquisitionListPreferences().downloadSource).toBeUndefined();
  }
  writeAcquisitionListPreferences({ ...saved, downloadSource: undefined });
  expect(readAcquisitionListPreferences().downloadSource).toBeUndefined();
});
