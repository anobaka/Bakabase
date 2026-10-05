import type { DownloadResource, ExtractionPlan } from "../results";

import { describe, expect, it } from "vitest";

import { groupDownloadResources } from "../resourceDeduplication";

const link = "https://files.example/Archive";
const plan = (evidence = ["Use the password to extract."]): ExtractionPlan => ({
  requirement: "required",
  steps: [{ id: "unpack", op: "extractArchive", input: "$download", password: "secret" }],
  evidence,
});

describe("post download resource deduplication", () => {
  it("combines complementary credentials, instructions and evidence while preserving original indices and input", () => {
    const resources: DownloadResource[] = [
      {
        link,
        code: "access123",
        extraction: { requirement: "unknown", steps: [], evidence: ["Read the post."] },
      },
      { link: "https://files.example/Other" },
      { link, password: "secret", extraction: plan(["Read the post.", "Extract with secret."]) },
    ];
    const before = structuredClone(resources);
    const groups = groupDownloadResources(resources);

    expect(groups).toHaveLength(2);
    expect(groups[0]).toMatchObject({
      sourceIndices: [0, 2],
      resource: {
        link,
        code: "access123",
        password: "secret",
        extraction: plan(["Read the post.", "Extract with secret."]),
      },
    });
    expect(groups[1].sourceIndices).toEqual([1]);
    expect(resources).toEqual(before);
  });

  it.each([
    ["https://pan.baidu.com/s/Share?pwd=Ab12", "https://pan.baidu.com/s/Share", "Ab12"],
    [
      "https://yun.baidu.com/s/Share?foo=Bar&pwd=A%2Bb",
      "https://yun.baidu.com/s/Share?foo=Bar",
      "A+b",
    ],
    [
      "https://pan.baidu.com/s/Share?pwd=A+b&foo=Bar#Preview",
      "https://pan.baidu.com/s/Share?foo=Bar#Preview",
      "A b",
    ],
  ])(
    "combines a Baidu embedded access code with its separate-code variant (%s)",
    (embedded, plain, code) => {
      const groups = groupDownloadResources([{ link: embedded }, { link: plain, code }]);

      expect(groups).toHaveLength(1);
      expect(groups[0]).toMatchObject({
        sourceIndices: [0, 1],
        resource: { link: embedded, code },
      });
    },
  );

  it("retains resources whose embedded Baidu code conflicts with their explicit code", () => {
    const groups = groupDownloadResources([
      { link: "https://pan.baidu.com/s/Share?pwd=old", code: "new" },
      { link: "https://pan.baidu.com/s/Share", code: "new" },
      { link: "https://pan.baidu.com/s/Share?pwd=other" },
    ]);

    expect(groups.map((group) => group.sourceIndices)).toEqual([[0], [1], [2]]);
  });

  it.each([
    [{ code: "one" }, { code: "two" }],
    [{ password: "one" }, { password: "two" }],
    [{ password: " secret " }, { password: "secret" }],
    [{ code: " code " }, { code: "code" }],
    [{ extraction: plan() }, { extraction: { ...plan(), requirement: "notRequired", steps: [] } }],
    [
      { extraction: plan() },
      { extraction: { ...plan(), steps: [{ ...plan().steps[0], password: "different" }] } },
    ],
    [{ linkHealth: { status: "available" } }, { linkHealth: { status: "unavailable" } }],
    [
      { linkHealth: { status: "unknown", reason: "A" } },
      { linkHealth: { status: "unknown", reason: "B" } },
    ],
  ] as [Partial<DownloadResource>, Partial<DownloadResource>][])(
    "keeps incompatible credentials, plans or health results as separate choices (%j, %j)",
    (first, second) => {
      const groups = groupDownloadResources([
        { link, ...first },
        { link, ...second },
      ]);

      expect(groups.map((group) => group.sourceIndices)).toEqual([[0], [1]]);
    },
  );

  it("preserves meaningful whitespace in passwords and access codes", () => {
    const groups = groupDownloadResources([
      { link },
      { link, password: "  password with spaces  ", code: " A1 " },
    ]);

    expect(groups).toHaveLength(1);
    expect(groups[0].resource).toMatchObject({
      password: "  password with spaces  ",
      code: " A1 ",
    });
  });

  it.each([
    ["https://mega.nz/file/id#first-key", "https://mega.nz/file/id#second-key"],
    ["https://files.example/Archive", "https://files.example/archive"],
    ["https://files.example/Archive?a=1&b=2", "https://files.example/Archive?b=2&a=1"],
    ["https://files.example/Archive?sig=A%2Bb", "https://files.example/Archive?sig=A%2bb"],
    ["https://files.example/Archive?sig=A%2Bb", "https://files.example/Archive?sig=A+b"],
    ["https://files.example/Archive?sig=AbC", "https://files.example/Archive?sig=abc"],
    ["https://pan.baidu.com/s/Share?pwd=a&pwd=b", "https://pan.baidu.com/s/Share?pwd=a"],
  ])(
    "does not merge distinct paths, signed query strings or fragments (%s, %s)",
    (first, second) => {
      const groups = groupDownloadResources([{ link: first }, { link: second }]);

      expect(groups.map((group) => group.resource.link)).toEqual([first, second]);
      expect(groups.map((group) => group.sourceIndices)).toEqual([[0], [1]]);
    },
  );

  it("normalizes only the identity host and scheme, retaining the first URL's exact spelling", () => {
    const original = "HTTPS://CDN.EXAMPLE:443/Archive%2fFile?Signature=AbC%2B123&part=02#Key";
    const equivalent = "https://cdn.example/Archive%2fFile?Signature=AbC%2B123&part=02#Key";
    const groups = groupDownloadResources([{ link: original }, { link: equivalent, code: "123" }]);

    expect(groups).toHaveLength(1);
    expect(groups[0].resource.link).toBe(original);
  });

  it("preserves unknown resource and plan fields when combining compatible future formats", () => {
    const firstPlan = {
      ...plan(["First evidence"]),
      outputFormat: null,
      preview: { type: "archive" },
    };
    const nextPlan = { ...plan(["Second evidence"]), outputFormat: "folder", futureFlag: true };
    const groups = groupDownloadResources([
      { link, extraction: firstPlan, checksum: null, sourceFile: "archive.zip" },
      { link, extraction: nextPlan, checksum: "sha256:example", future: { enabled: true } },
    ]);

    expect(groups).toHaveLength(1);
    expect(groups[0].resource).toMatchObject({
      checksum: "sha256:example",
      sourceFile: "archive.zip",
      future: { enabled: true },
      extraction: {
        outputFormat: "folder",
        preview: { type: "archive" },
        futureFlag: true,
        evidence: ["First evidence", "Second evidence"],
      },
    });
  });

  it("retains conflicting unknown fields rather than silently discarding newer metadata", () => {
    const firstPlan = { ...plan(), futureFlag: "first" };
    const secondPlan = { ...plan(), futureFlag: "second" };

    expect(
      groupDownloadResources([
        { link, checksum: "first" },
        { link, checksum: "second" },
      ]),
    ).toHaveLength(2);
    expect(
      groupDownloadResources([
        { link, extraction: firstPlan },
        { link, extraction: secondPlan },
      ]),
    ).toHaveLength(2);
  });

  it.each([undefined, "invalid date", "2026-10-01T00:00:00Z"])(
    "takes the latest health check while preserving complementary unknown fields (%s)",
    (oldDate) => {
      const firstHealth = {
        status: "available" as const,
        checkedAt: oldDate,
        providerResponse: "first",
      };
      const latestHealth = {
        status: "available" as const,
        checkedAt: "2026-10-05T00:00:00Z",
        latencyMs: 10,
      };
      const groups = groupDownloadResources([
        { link, linkHealth: firstHealth },
        { link, linkHealth: latestHealth },
      ]);

      expect(groups).toHaveLength(1);
      expect(groups[0].resource.linkHealth).toEqual({ ...firstHealth, ...latestHealth });
    },
  );

  it("keeps absent-link placeholders and independent text-only processing plans in their original positions", () => {
    const groups = groupDownloadResources([
      {},
      { link: "" },
      { link: "  " },
      { extraction: plan() },
      { extraction: plan() },
    ]);

    expect(groups.map((group) => group.sourceIndices)).toEqual([[0], [1], [2], [3], [4]]);
  });

  it("does not flatten malformed plans or lose evidence beyond the supported combined limit", () => {
    const malformed = { requirement: "required", evidence: [] } as unknown as ExtractionPlan;

    expect(
      groupDownloadResources([
        { link, extraction: malformed },
        { link, extraction: malformed },
      ]),
    ).toHaveLength(2);
    expect(
      groupDownloadResources([
        { link, extraction: plan(Array.from({ length: 32 }, (_, index) => `Evidence ${index}`)) },
        { link, extraction: plan(["Additional evidence"]) },
      ]),
    ).toHaveLength(2);
  });
});
