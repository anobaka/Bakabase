import type { PostParserTask } from "@/core/models/PostParserTask";

import { describe, expect, it } from "vitest";

import {
  buildExportRows,
  buildInstructionsJson,
  getDownloadInfo,
  getTargetResult,
} from "../results";

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
  it.each(["DownloadInfo", String(PostParseTarget.DownloadInfo)])(
    "deduplicates %s exports while keeping persisted selection indices and complementary instructions",
    (key) => {
      const extraction = {
        requirement: "required",
        steps: [{ id: "unpack", op: "extractArchive", input: "download", password: "archive" }],
        evidence: ["Author instructions"],
      };
      const record = {
        ...task,
        results: {
          [key]: {
            futureField: "keep",
            resources: [
              { link: "https://pan.baidu.com/s/shared", code: "1234" },
              { link: "https://example.com/other", password: "other" },
              { link: "https://pan.baidu.com/s/shared?pwd=1234", password: "archive", extraction },
            ],
          },
        },
      };

      expect(getDownloadInfo(record)?.resources).toHaveLength(3);
      const info = JSON.parse(buildInstructionsJson([record])).tasks[0].downloadInfo;

      expect(info.futureField).toBe("keep");
      expect(info.resources).toHaveLength(2);
      expect(info.resources[0]).toMatchObject({
        link: "https://pan.baidu.com/s/shared?pwd=1234",
        code: "1234",
        password: "archive",
        extraction,
      });
      const rows = buildExportRows([record], (target) => target);

      expect(rows).toHaveLength(2);
      expect(rows[0]["Password"]).toBe("archive");
      expect(JSON.parse(String(rows[0]["Extraction Plan"]))).toEqual(extraction);
      expect(rows[1]["Resource Link"]).toBe("https://example.com/other");
      expect(getDownloadInfo(record)?.resources?.[2].password).toBe("archive");
    },
  );
  it("preserves ordered multi-layer instructions, completeness and link evidence through JSON and spreadsheet exports", () => {
    const extraction = {
      requirement: "required",
      evidence: ["Rename to .zip then use password one; repeat with .7z and two"],
      steps: [
        { id: "rename-1", op: "renameExtension", input: "download", extension: ".zip" },
        { id: "extract-1", op: "extractArchive", input: "rename-1", password: "one" },
        {
          id: "rename-2",
          op: "renameExtension",
          input: "extract-1",
          extension: ".7z",
          selector: "*.dat",
        },
        { id: "extract-2", op: "extractArchive", input: "rename-2", password: "two" },
      ],
    };
    const info = {
      ...result,
      schemaVersion: 2,
      isComplete: false,
      warnings: ["Still locked"],
      futureField: { retained: true },
      resources: [
        {
          ...result.resources[0],
          extraction,
          linkHealth: { status: "unknown", reason: "Access code is locked" },
        },
      ],
    };
    const record = { ...task, results: { DownloadInfo: info } };

    expect(getDownloadInfo(record)).toMatchObject(info);
    expect(JSON.parse(buildInstructionsJson([record])).tasks[0].downloadInfo).toEqual(info);
    const row = buildExportRows([record], (key) => key)[0];

    expect(JSON.parse(String(row["Extraction Plan"]))).toEqual(extraction);
    expect(row.Complete).toBe("false");
    expect(JSON.parse(String(row["Link Health"])).status).toBe("unknown");
  });
  it("exports usable Baidu URLs without overwriting embedded codes or losing mixed processing instructions", () => {
    const extraction = {
      requirement: "required",
      evidence: [],
      steps: [
        { id: "rename", op: "renameFile", input: "download", targetName: "archive.zip" },
        { id: "move", op: "moveFile", input: "rename", targetDirectory: "ready" },
        { id: "extract", op: "extractArchive", input: "move", password: "pass" },
      ],
    };
    const record = {
      ...task,
      results: {
        DownloadInfo: {
          resources: [
            { link: "https://pan.baidu.com/s/abc", code: "1234", extraction },
            { link: "https://pan.baidu.com/s/def?pwd=old", code: "new" },
          ],
        },
      },
    };
    const rows = buildExportRows([record], (key) => key);
    const info = JSON.parse(buildInstructionsJson([record])).tasks[0].downloadInfo;

    expect(rows[0]["Resource Link"]).toBe("https://pan.baidu.com/s/abc?pwd=1234");
    expect(rows[1]["Resource Link"]).toBe("https://pan.baidu.com/s/def?pwd=old");
    expect(info.resources[0].link).toBe(rows[0]["Resource Link"]);
    expect(info.resources[0].extraction).toEqual(extraction);
    expect(record.results.DownloadInfo.resources[0].link).toBe("https://pan.baidu.com/s/abc");
  });
  it("renders and exports all current bare result links and their individual passwords", () => {
    const record = {
      ...task,
      createdAt: "2026-10-01T07:08:09Z",
      completedAt: "2026-10-01T07:09:10Z",
      results: { [PostParseTarget.DownloadInfo]: result },
    };

    expect(getDownloadInfo(record)).toEqual(result);
    const rows = buildExportRows([record], (key) => key);

    expect(rows).toHaveLength(2);
    expect(rows[0]).toMatchObject({
      Title: result.title,
      "Resource Link": result.resources[0].link,
      "Access Code": "aB12",
      Password: "archive-1",
      CreatedAt: record.createdAt,
      CompletedAt: record.completedAt,
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
      CreatedAt: "",
      CompletedAt: "",
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
