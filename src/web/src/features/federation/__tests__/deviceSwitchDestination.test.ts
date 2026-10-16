import type { IMenuItem } from "@/layouts/BasicLayout/components/PageNav/components/AntdMenu/menuConfig";

import { describe, expect, it } from "vitest";

import { deviceSwitchDestination } from "../deviceSwitchDestination";
import { deviceSwitchPath, deviceSwitchRoute } from "../deviceSwitchRoute";
import { withRoute } from "../switching";

import { mergeNavigationLayout } from "@/layouts/BasicLayout/components/PageNav/components/AntdMenu/navigationLayout";

const catalog: IMenuItem[] = [
  { name: "resources", path: "/resource" },
  { name: "tools", children: [{ name: "workflow", path: "/workflow" }] },
  { name: "devices", localNodeOnly: true, children: [{ name: "sync", path: "/data-sync" }] },
  {
    name: "desktop",
    pureClientOnly: true,
    children: [{ name: "mapping", path: "/client-path-mapping" }],
  },
];
const local = { isPureClient: false, isLocalNode: true };
const remote = { isPureClient: true, isLocalNode: false };

describe("device-switch pathname handoff", () => {
  it("keeps the one-time ticket outside the fragment and drops source owner/filter queries", () => {
    expect(
      withRoute(
        "http://localhost:4567/?__bakabase_switch=ticket#old",
        deviceSwitchRoute("/resource?owner=nas&resourceId=42#selection"),
      ),
    ).toBe("http://localhost:4567/?__bakabase_switch=ticket#/device-switch?path=%2Fresource");
  });

  it.each([
    "https://example.com",
    "//example.com",
    "/resource//x",
    "/../resource",
    "/%2Fresource",
    "/resource\\x",
    "/resource\nx",
    "/device-switch",
    "/device-switch/again",
    `/${"a".repeat(512)}`,
  ])("rejects an unsafe or recursive path: %s", (value) =>
    expect(deviceSwitchPath(value)).toBe("/"),
  );

  it("accepts a bounded pathname without interpreting a detail ID as a resource on the destination", () => {
    expect(deviceSwitchPath(`/${"a".repeat(511)}`)).toHaveLength(512);
    expect(
      deviceSwitchDestination("/workflow/source-owned-id/edit", catalog, undefined, local),
    ).toBe("/workflow");
    expect(deviceSwitchDestination("/workflows/42", catalog, undefined, local)).toBe("/");
  });
});

describe("destination menu resolution", () => {
  it("uses a visible matching destination menu and only falls home when absent", () => {
    expect(deviceSwitchDestination("/resource", catalog, undefined, remote)).toBe("/resource");
    expect(deviceSwitchDestination("/unknown-feature", catalog, undefined, local)).toBe("/");
  });

  it("respects hidden menus and hidden groups in the destination layout", () => {
    const layout = mergeNavigationLayout(catalog, undefined, (key) => key);

    layout.items.find((item) => item.id === "menu:resources")!.hidden = true;
    layout.items.find((item) => item.id === "directory:tools")!.hidden = true;
    expect(deviceSwitchDestination("/resource", catalog, layout, local)).toBe("/");
    expect(deviceSwitchDestination("/workflow/42", catalog, layout, local)).toBe("/");
  });

  it("uses destination capabilities rather than permissions from the source", () => {
    expect(deviceSwitchDestination("/data-sync", catalog, undefined, remote)).toBe("/");
    expect(deviceSwitchDestination("/data-sync", catalog, undefined, local)).toBe("/data-sync");
    expect(deviceSwitchDestination("/client-path-mapping", catalog, undefined, local)).toBe("/");
    expect(deviceSwitchDestination("/client-path-mapping", catalog, undefined, remote)).toBe(
      "/client-path-mapping",
    );
  });

  it("includes new catalog entries through the normal navigation merge", () => {
    const layout = mergeNavigationLayout(catalog, undefined, (key) => key);
    const updated = [...catalog, { name: "new", path: "/new-feature" }];

    expect(deviceSwitchDestination("/new-feature", updated, layout, local)).toBe("/new-feature");
  });

  it("merges translated catalog directory names exactly like the destination sidebar", () => {
    const saved = {
      version: 1,
      items: [{ id: "custom:tools", kind: "directory", name: "工具", hidden: true, children: [] }],
      knownCatalogIds: [],
    };
    const translate = (key: string) => (key === "tools" ? "工具" : key);

    expect(deviceSwitchDestination("/workflow", catalog, saved, local, translate)).toBe("/");
  });

  it("selects the closest visible menu ancestor, never the source detail page", () => {
    const nested = [...catalog, { name: "workflowReports", path: "/workflow/reports" }];

    expect(deviceSwitchDestination("/workflow/reports/123", nested, undefined, local)).toBe(
      "/workflow/reports",
    );
  });
});
