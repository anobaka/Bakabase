import type { PostParserTask } from "@/core/models/PostParserTask";

import { describe, expect, it } from "vitest";

import { buildExportRows, getDownloadInfo, getTargetResult } from "../results";

import { PostParseTarget, PostParserSource } from "@/sdk/constants";

const result = {
  title: "Two mirrors for a resource",
  resources: [
    { link: "https://pan.example/a", code: "aB12", password: "archive-1" },
    { link: "magnet:?xt=urn:btih:abc", code: null, password: "archive-2" },
  ],
};
const task: PostParserTask = {
  id: 14,
  source: PostParserSource.SoulPlus,
  link: "https://example.com/post/14",
  title: "Post title",
  targets: [PostParseTarget.DownloadInfo],
};

describe("post parsing result compatibility", () => {
  it("renders and exports all current bare result links and their individual passwords", () => {
    const record = { ...task, results: { [PostParseTarget.DownloadInfo]: result } };

    expect(getDownloadInfo(record)).toEqual(result);
    const rows = buildExportRows([record], (key) => key);

    expect(rows).toHaveLength(2);
    expect(rows[0]).toMatchObject({
      Title: result.title,
      "Resource Link": result.resources[0].link,
      "Access Code": "aB12",
      Password: "archive-1",
    });
    expect(rows[1]).toMatchObject({
      "Resource Link": result.resources[1].link,
      "Access Code": "",
      Password: "archive-2",
    });
  });

  it("keeps historical named targets and data envelopes readable and exportable", () => {
    const record = {
      ...task,
      results: { DownloadInfo: { data: result, parsedAt: "2026-09-14", error: "partial" } },
    };

    expect(getDownloadInfo(record)).toEqual(result);
    expect(getTargetResult(record, PostParseTarget.DownloadInfo)?.error).toBe("partial");
    expect(buildExportRows([record], (key) => key)[1]).toMatchObject({
      Password: "archive-2",
      ParsedAt: "2026-09-14",
      Error: "partial",
    });
  });

  it("preserves failures when exporting a task with no extracted result", () => {
    expect(
      buildExportRows([{ ...task, error: "Unable to read post" }], (key) => key)[0],
    ).toMatchObject({ ID: 14, Link: task.link, Error: "Unable to read post" });
  });

  it("leaves unknown targets and empty extracted results available for export", () => {
    const rows = buildExportRows(
      [
        {
          ...task,
          results: { DownloadInfo: { resources: [] }, FutureTarget: { note: "retained" } },
        },
      ],
      (key) => key,
    );

    expect(rows).toHaveLength(2);
    expect(rows[1]).toMatchObject({ Target: "FutureTarget", note: "retained" });
  });

  it("keeps persisted link indices stable when old result entries are malformed", () => {
    const data = getDownloadInfo({
      ...task,
      results: {
        DownloadInfo: { resources: [null, { link: "https://example.com/file", code: "1234" }] },
      },
    });

    expect(data?.resources).toHaveLength(2);
    expect(data?.resources?.[0].link).toBeUndefined();
    expect(data?.resources?.[1]).toMatchObject({ link: "https://example.com/file", code: "1234" });
  });
});
