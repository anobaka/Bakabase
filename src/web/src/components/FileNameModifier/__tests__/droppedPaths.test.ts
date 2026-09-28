import { describe, expect, it } from "vitest";

import { getDroppedPaths } from "../droppedPaths";

function droppedData(
  plain = "",
  uriList = "",
  files: Array<File & { path?: string }> = [],
): DataTransfer {
  return {
    getData: (type: string) =>
      type === "text/plain" ? plain : type === "text/uri-list" ? uriList : "",
    files,
  } as unknown as DataTransfer;
}

describe("dropped file paths", () => {
  it("reads Bakabase file explorer's JSON path array and deduplicates it", () => {
    const payload = JSON.stringify(["/library/a.jpg", "/library/a.jpg", "C:\\Library\\b.jpg"]);

    expect(getDroppedPaths(droppedData(payload))).toEqual(["/library/a.jpg", "C:\\Library\\b.jpg"]);
  });

  it("accepts absolute path text and file URIs from drag sources", () => {
    expect(
      getDroppedPaths(
        droppedData(
          "/library/first.jpg\nrelative.jpg\n\\\\server\\share\\second.jpg",
          "# copied files\nfile:///library/third%20file.jpg\nhttps://example.com/file.jpg",
        ),
      ),
    ).toEqual(["/library/first.jpg", "\\\\server\\share\\second.jpg", "/library/third file.jpg"]);
  });

  it("uses a host-provided absolute File.path but never a File.name or relative path", () => {
    const nameOnly = new File(["data"], "name-only.jpg");
    const fullPath = Object.assign(new File(["data"], "full-path.jpg"), {
      path: "/library/full-path.jpg",
    });
    const relativePath = Object.assign(new File(["data"], "relative.jpg"), {
      path: "library/relative.jpg",
    });

    expect(getDroppedPaths(droppedData("", "", [nameOnly, fullPath, relativePath]))).toEqual([
      "/library/full-path.jpg",
    ]);
    expect(getDroppedPaths(droppedData("", "", [nameOnly]))).toEqual([]);
  });
});
