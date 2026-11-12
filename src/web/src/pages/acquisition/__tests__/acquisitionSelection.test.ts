import { expect, it } from "vitest";
import { acquisitionActionLimits, captureFilteredSelection, validSelectionSnapshot } from "../acquisitionSelection";

it("captures the complete cross-page IDs without tying selection to the local processing limit", () => {
  const ids = Array.from({ length: 1001 }, (_, i) => i + 1);
  expect(captureFilteredSelection(ids, false)).toEqual(ids);
  expect(acquisitionActionLimits.continue).toBe(1000);
  expect(acquisitionActionLimits.delete).toBe(10000);
  const captured = captureFilteredSelection(ids, false)!;
  ids.push(1002);
  expect(captured).toHaveLength(1001);
});

it("does not label a truncated snapshot all results, while explicit first-N selection preserves server order", () => {
  const ids = [60, 2, 90, 4];
  expect(captureFilteredSelection(ids, true)).toBeUndefined();
  expect(captureFilteredSelection(ids, true, 2)).toEqual([60, 2]);
  expect(captureFilteredSelection(ids, false, 0)).toBeUndefined();
});

it.each([[1, 1], [1, -2], [1, 1.5], [], Array.from({ length: 10001 }, (_, i) => i + 1)])(
  "rejects invalid or oversized snapshots",
  (...ids) => {
    expect(validSelectionSnapshot(ids)).toBe(false);
    expect(captureFilteredSelection(ids, false)).toBeUndefined();
  },
);
