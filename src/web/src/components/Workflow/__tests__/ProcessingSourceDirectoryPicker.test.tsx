import { describe, expect, it } from "vitest";
import { relativeInputDirectory } from "../ProcessingSourceDirectoryPicker";

describe("relative input directory for a retained artifact", () => {
  it("preserves the selected subtree name on Docker and Windows", () => {
    expect(relativeInputDirectory("/work/items/input/", "/work/items/input/transfer/合集1")).toBe(
      "transfer/合集1",
    );
    expect(relativeInputDirectory("C:\\Work\\Input", "c:/Work/Input/合集1/Vol.1")).toBe(
      "合集1/Vol.1",
    );
  });

  it("rejects the root, sibling directories, and path traversal", () => {
    for (const selected of [
      "/work/input",
      "/work/input-old/files",
      "/work/input/../elsewhere",
      "/work/Input/files",
    ]) {
      expect(relativeInputDirectory("/work/input", selected)).toBeUndefined();
    }
  });
});
