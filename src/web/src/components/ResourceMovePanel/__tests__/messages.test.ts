import { describe, expect, it } from "vitest";

import { getKnownMoveReasonCode, getMoveReasonText } from "../messages";

describe("move eligibility reasons", () => {
  it.each([
    "steamManaged",
    "sourceContextRequired",
    "foreignMoveSource",
    "sourceContextChanged",
    "invalidMoveSourceReferences",
    "containsSteamManagedResource",
    "sourceMoveUnsupported",
    "sourceRecordMissing",
    "sourceLocationChanged",
    "legacySourcePlanMissing",
    "noLocalFiles",
    "resourceLocked",
    "destinationMissing",
    "sourceMissing",
    "destinationInsideSource",
    "alreadyAtDestination",
  ])("explains %s in English and Chinese without leaking the backend code", (reason) => {
    const en = getMoveReasonText(reason, "en");
    const cn = getMoveReasonText(reason, "zh-CN");

    expect(en).not.toContain(reason);
    expect(cn).not.toContain(reason);
    expect(en).not.toBe(cn);
    expect(cn).toMatch(/[\u4e00-\u9fff]/);
  });
  it("recognizes validation prefixes and gives unknown exclusions a readable fallback", () => {
    expect(getKnownMoveReasonCode("sourceLocationChanged: location no longer matches")).toBe(
      "sourceLocationChanged",
    );
    expect(getMoveReasonText("futureServerCode", "cn")).toBe(
      "此资源当前无法移动，请刷新资源并检查源位置与目标位置。",
    );
    expect(getMoveReasonText("steamManaged", "cn")).toContain("源位置或目标位置");
    expect(getMoveReasonText("steamManaged", "en")).toContain("source or destination");
    expect(getMoveReasonText("legacySourcePlanMissing", "cn")).toContain("重试不能补回缺失的计划");
  });
});
