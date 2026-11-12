import { beforeEach, expect, it, vi } from "vitest";
import { acquisitionListPreferencesKey, readAcquisitionListPreferences, writeAcquisitionListPreferences } from "../listPreferences";

beforeEach(() => { localStorage.clear(); vi.restoreAllMocks(); });
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
