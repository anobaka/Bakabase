import { createInstance } from "i18next";
import { expect, it, vi } from "vitest";
import { workflowNodeDisplayName } from "../nodeDisplayNames";
import cn from "@/locales/cn/pages/cloudDownload.json";
import en from "@/locales/en/pages/cloudDownload.json";

vi.mock("../Activities", () => ({ getWorkflowActivityUI: (kind: string) => kind === "fs.publishArtifacts"
  ? { displayNameKey: "workflow.activity.fsPublishArtifacts.name" } : undefined }));
vi.mock("../Triggers", () => ({ getWorkflowTriggerUI: () => undefined }));
const instance = createInstance();
await instance.init({ lng: "cn", keySeparator: false, resources: {
  cn: { translation: cn }, en: { translation: en },
} });

it.each([["cn", "交付文件产物"], ["en", "Deliver file artifacts"]])(
  "shows the localized activity rather than the generated slot in %s", (language, expected) => {
    const node = Object.freeze({ kind: "fs.publishArtifacts", nodeId: "files.deliver" });
    expect(workflowNodeDisplayName(instance.getFixedT(language), node)).toBe(expected);
    expect(node.nodeId).toBe("files.deliver");
  },
);

it.each(["交付给我的媒体库", "parent/files.deliver", "toString", "constructor"])(
  "preserves the custom or nested node ID %s", nodeId => {
    expect(workflowNodeDisplayName(instance.getFixedT("cn"), { kind: "fs.publishArtifacts", nodeId }))
      .toBe(`交付文件产物 · ${nodeId}`);
  },
);

it("does not interpret the ID as a built-in kind or replace an unknown activity's fallback", () => {
  const t = instance.getFixedT("cn");
  expect(workflowNodeDisplayName(t, { kind: "custom.publish", nodeId: "files.deliver" }, "用户自定义步骤"))
    .toBe("用户自定义步骤 · files.deliver");
  expect(workflowNodeDisplayName(t, { kind: "fs.publishArtifacts" })).toBe("交付文件产物");
});
