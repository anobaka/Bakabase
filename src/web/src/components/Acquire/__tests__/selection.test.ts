import { describe, expect, it } from "vitest";
import { manualCandidate } from "../manualInputs";
import { toggleCandidateSelection, type DownloadCandidate } from "../models";

const candidates: DownloadCandidate[] = [
  { id: "a", url: "a", kind: "auto", groupId: "main" },
  { id: "b", url: "b", kind: "auto", groupId: "main" },
  { id: "c", url: "c", kind: "auto" },
  { id: "d", url: "d", kind: "auto" },
];
describe("acquisition content choices", () => {
  it("replaces only a declared mirror, preserving independently selected content", () => {
    expect(toggleCandidateSelection(["a", "c"], candidates[1], candidates, true, "files")).toEqual([
      "c",
      "b",
    ]);
    expect(toggleCandidateSelection(["a", "c"], candidates[3], candidates, true, "files")).toEqual([
      "a",
      "c",
      "d",
    ]);
  });
  it("can retain alternative leads without starting duplicate downloads", () => {
    expect(toggleCandidateSelection(["a"], candidates[1], candidates, true, "lead")).toEqual([
      "a",
      "b",
    ]);
  });
});

it("identifies an explicit Baidu remote path while leaving unknown URLs for server classification", () => {
  expect(manualCandidate("baiduRemote", { id: "1", url: "/folder" })).toMatchObject({
    kind: "remote",
    providerId: "baidu",
  });
  expect(
    manualCandidate("auto", { id: "2", url: "https://unrecognized.example/share" }),
  ).toMatchObject({ kind: "auto" });
  expect(
    manualCandidate("auto", { id: "2", url: "https://unrecognized.example/share" }).providerId,
  ).toBeUndefined();
});

it.each([
  ["pikpakRemote", "pikpak", "pikpak.rclone"],
  ["oneDriveRemote", "oneDrive", "oneDrive.rclone"],
  ["pixeldrainRemote", "pixeldrain", "pixeldrain.rcloneRemote"],
])("derives the explicit %s method from source metadata", (input, providerId, methodId) => {
  expect(manualCandidate(input, { id: "1", url: "/folder" })).toMatchObject({
    kind: "remote",
    providerId,
    methodId,
    url: "/folder",
  });
});
