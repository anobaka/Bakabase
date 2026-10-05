import type { DownloadContentGroup, DownloadInfoData } from "../results";
import type { PostParserTask } from "@/core/models/PostParserTask";

import { describe, expect, it } from "vitest";

import { getDownloadContentGroups, normalizeDownloadContentGroups } from "../resourceGroups";
import { buildExportRows, buildInstructionsJson, getDownloadInfo } from "../results";
import { buildTaskSearchText } from "../search";

import { PostParserSource, PostParseTarget } from "@/sdk/constants";

const main: DownloadContentGroup = {
  id: "full",
  title: "Full album",
  kind: "main",
  summary: "Same full album on different hosts",
  evidence: ["Baidu and MEGA contain the full album"],
};
const preview: DownloadContentGroup = {
  id: "preview",
  title: "Preview clips",
  kind: "preview",
  evidence: ["Samples only"],
};
const info: DownloadInfoData = {
  schemaVersion: 3,
  groups: [preview, main],
  resources: [
    { link: "https://mega.nz/file/preview#key", groupId: "preview" },
    { link: "https://pan.baidu.com/s/album", code: "abcd", groupId: "full" },
    { link: "https://mega.nz/file/album#key", groupId: "full", password: "mega-password" },
    { link: "https://pan.baidu.com/s/album?pwd=abcd", groupId: "full", password: "baidu-password" },
    { link: "https://example.com/unidentified" },
  ],
};
const task: PostParserTask = {
  id: 14,
  source: PostParserSource.SoulPlus,
  link: "https://example.com/post/14",
  targets: [PostParseTarget.DownloadInfo],
  results: { DownloadInfo: info },
};

describe("download content groups", () => {
  it("puts main content first without merging different mirrors or changing selection indices", () => {
    const original = JSON.stringify(info);
    const sections = getDownloadContentGroups(info);

    expect(sections.map((section) => section.group?.kind)).toEqual(["main", "preview", undefined]);
    expect(sections[0].resources.map((resource) => resource.sourceIndices)).toEqual([[1, 3], [2]]);
    expect(sections[0].resources.map(({ resource }) => resource.password)).toEqual([
      "baidu-password",
      "mega-password",
    ]);
    expect(sections[1].resources[0].sourceIndices).toEqual([0]);
    expect(sections[2].resources[0].sourceIndices).toEqual([4]);
    expect(JSON.stringify(info)).toBe(original);
  });

  it("does not group unrelated content by domain or merge the same URL across explicit conflicting groups", () => {
    const data: DownloadInfoData = {
      groups: [main, preview],
      resources: [
        { link: "https://pan.baidu.com/s/shared", groupId: "full" },
        { link: "https://pan.baidu.com/s/shared", groupId: "preview" },
        { link: "https://pan.baidu.com/s/other" },
      ],
    };

    expect(
      getDownloadContentGroups(data).map((section) =>
        section.resources.map(({ sourceIndices }) => sourceIndices),
      ),
    ).toEqual([[[0]], [[1]], [[2]]]);
  });

  it("can complete a duplicate's missing group while retaining its original binding index", () => {
    const sections = getDownloadContentGroups({
      groups: [main],
      resources: [
        { link: "https://example.com/full", password: "archive" },
        { link: "https://example.com/full", groupId: "full" },
      ],
    });

    expect(sections).toHaveLength(1);
    expect(sections[0].group?.id).toBe("full");
    expect(sections[0].resources[0]).toMatchObject({
      resource: { password: "archive", groupId: "full" },
      sourceIndices: [0, 1],
    });
  });

  it("keeps old and dangling references unclassified, including invalid resource placeholders", () => {
    const legacy = getDownloadInfo({
      ...task,
      results: {
        DownloadInfo: {
          resources: [
            null,
            { link: "https://example.com/a", groupId: "missing" },
            { link: "https://example.com/b" },
          ],
        },
      },
    });
    const sections = getDownloadContentGroups(legacy!);

    expect(sections).toHaveLength(1);
    expect(sections[0].key).toBe("ungrouped");
    expect(sections[0].resources.map((resource) => resource.sourceIndices)).toEqual([
      [0],
      [1],
      [2],
    ]);
    expect(getDownloadContentGroups({ resources: [] })).toEqual([]);
  });

  it("discards all ambiguous IDs and ignores empty, oversized or unreferenced groups", () => {
    const resources = [{ groupId: "full" }, { groupId: "empty" }, { groupId: "x".repeat(81) }];
    const groups = [
      main,
      { ...main, id: " full " },
      { ...main, id: "empty", title: " " },
      { ...main, id: "x".repeat(81) },
      preview,
      null,
    ];

    expect(normalizeDownloadContentGroups(groups, resources)).toEqual([]);
  });

  it("normalizes bounded metadata, maps unknown purposes safely and preserves extension fields", () => {
    const groups = normalizeDownloadContentGroups(
      [
        {
          ...main,
          id: " full ",
          kind: "OTHER",
          title: "t".repeat(200),
          summary: "s".repeat(600),
          evidence: [
            null,
            " ",
            ...Array.from({ length: 10 }, (_, index) => `${index}${"e".repeat(400)}`),
          ],
          futureField: { retained: true },
        },
      ],
      [{ groupId: "full" }],
    );

    expect(groups[0]).toMatchObject({
      id: "full",
      kind: "unknown",
      futureField: { retained: true },
    });
    expect(groups[0].title).toHaveLength(160);
    expect(groups[0].summary).toHaveLength(500);
    expect(groups[0].evidence).toHaveLength(8);
    expect(groups[0].evidence.every((text) => text.length === 300)).toBe(true);
  });

  it("caps groups without losing any source links", () => {
    const groups = Array.from({ length: 130 }, (_, index) => ({ ...main, id: String(index) }));
    const resources = groups.map((group) => ({
      link: `https://example.com/${group.id}`,
      groupId: group.id,
    }));
    const sections = getDownloadContentGroups({ groups, resources });

    expect(sections.filter((section) => section.group)).toHaveLength(128);
    expect(sections.at(-1)?.resources.map((resource) => resource.sourceIndices)).toEqual([
      [128],
      [129],
    ]);
    expect(sections.reduce((count, section) => count + section.resources.length, 0)).toBe(130);
  });

  it("keeps groups and mirror-specific credentials in JSON and spreadsheet exports", () => {
    const exported = JSON.parse(buildInstructionsJson([task]));

    expect(exported.schemaVersion).toBe(3);
    expect(exported.tasks[0].downloadInfo.groups).toEqual([preview, main]);
    expect(exported.tasks[0].downloadInfo.resources).toHaveLength(4);
    expect(exported.tasks[0].downloadInfo.resources[1]).toMatchObject({
      groupId: "full",
      code: "abcd",
      password: "baidu-password",
    });
    const rows = buildExportRows([task], (key) => key);

    expect(rows[1]).toMatchObject({
      "Content Group ID": "full",
      "Content Group": "Full album",
      "Content Role": "main",
      "Group Summary": main.summary,
    });
    expect(JSON.parse(String(rows[1]["Group Evidence"]))).toEqual(main.evidence);
    expect(rows[2]["Password"]).toBe("mega-password");
    expect(rows[3]["Content Group"]).toBe("");
    expect(getDownloadInfo(task)?.resources).toHaveLength(5);
  });

  it("makes group names, summaries and evidence searchable", () => {
    const search = buildTaskSearchText(task);

    expect(search).toContain("full album");
    expect(search).toContain("same full album on different hosts");
    expect(search).toContain("samples only");
  });
});
