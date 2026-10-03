import type { PostParserTask } from "@/core/models/PostParserTask";

import { describe, expect, it } from "vitest";

import { buildTaskSearchText } from "../search";

import { PostParseTarget, PostParserSource } from "@/sdk/constants";

const task: PostParserTask = {
  id: 42,
  source: PostParserSource.SoulPlus,
  title: "Original Post",
  link: "https://example.com/Post/42",
  text: "Pasted Content",
  content: "Fetched Body",
  error: "Provider Error",
  targets: [PostParseTarget.DownloadInfo],
};

describe("local post search", () => {
  it("includes post information and download links, codes and passwords from legacy wrapped results", () => {
    const text = buildTaskSearchText({
      ...task,
      results: {
        DownloadInfo: {
          data: {
            title: "Parsed Resource",
            resources: [
              { link: "https://pan.example/File", code: "Ab12", password: "Archive Key" },
            ],
          },
          error: "Partial Failure",
        },
      },
    });

    for (const keyword of [
      "42",
      "original post",
      "example.com/post/42",
      "pasted content",
      "fetched body",
      "provider error",
      "parsed resource",
      "pan.example/file",
      "ab12",
      "archive key",
      "partial failure",
    ]) {
      expect(text).toContain(keyword);
    }
  });

  it("handles records without titles or parsed results", () => {
    expect(
      buildTaskSearchText({ ...task, title: undefined, error: undefined, text: undefined }),
    ).toBe("42\nhttps://example.com/post/42\nfetched body");
  });
});
