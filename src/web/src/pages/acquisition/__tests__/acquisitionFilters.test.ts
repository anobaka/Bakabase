import { expect, it } from "vitest";
import { normalizeAcquisitionFilters, toggleAcquisitionFilter } from "../acquisitionFilters";

it("canonicalizes each AND set independently without changing the caller's selection", () => {
  const input = { attentionTags: ["networkIssue", "localStorage", "networkIssue"],
    facts: ["spaceInsufficient", "downloadedPending", "spaceInsufficient"] };
  expect(normalizeAcquisitionFilters(input)).toEqual({
    attentionTags: ["localStorage", "networkIssue"], facts: ["downloadedPending", "spaceInsufficient"],
  });
  expect(input.attentionTags).toEqual(["networkIssue", "localStorage", "networkIssue"]);
  expect(input.facts).toEqual(["spaceInsufficient", "downloadedPending", "spaceInsufficient"]);
});

it("includes legacy reasons in the AND set without discarding unknown conditions", () => {
  expect(normalizeAcquisitionFilters({ attentionTag: "localStorage", attentionTags: ["networkIssue", "localStorage"],
    facts: ["futureFact"] })).toEqual({ attentionTags: ["localStorage", "networkIssue"], facts: ["futureFact"] });
  expect(normalizeAcquisitionFilters({})).toEqual({ attentionTags: [], facts: [] });
});

it("toggles only the requested condition and produces a canonical set", () => {
  expect(toggleAcquisitionFilter(["spaceInsufficient"], "downloadedPending"))
    .toEqual(["downloadedPending", "spaceInsufficient"]);
  expect(toggleAcquisitionFilter(["downloadedPending", "spaceInsufficient"], "downloadedPending"))
    .toEqual(["spaceInsufficient"]);
});
