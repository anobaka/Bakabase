import type { IMenuItem } from "../menuConfig";
import type { NavigationLayout, NavigationNode } from "../navigationLayout";

import { describe, expect, it } from "vitest";

import {
  addNavigationDirectory,
  buildVisibleNavigationMenu,
  mergeNavigationLayout,
  moveNavigationNode,
  navigationCatalogId,
  removeNavigationDirectory,
  renameNavigationDirectory,
  setNavigationNodeHidden,
} from "../navigationLayout";

const labels: Record<string, string> = {
  "menu.resources": "Resources",
  "menu.tools": "Tools",
  "menu.downloads": "Downloads",
  "menu.files": "Files",
  "menu.more": "More",
  "menu.inspector": "Inspector",
  "menu.settings": "Settings",
  "menu.system": "System",
  "menu.computer": "This computer",
  "menu.mapping": "Path mapping",
  "menu.federation": "Sharing",
  "menu.devices": "Devices",
};
const t = (key: string) => labels[key] ?? key;
const catalog: IMenuItem[] = [
  { name: "menu.resources", path: "/resource" },
  {
    name: "menu.tools",
    children: [
      { name: "menu.downloads", path: "/downloads" },
      { name: "menu.files", path: "/files" },
    ],
  },
];
const empty: NavigationLayout = { version: 1, items: [], knownCatalogIds: [] };
const capabilities = { isPureClient: false, isLocalNode: false };
const cloneCatalog = () => structuredClone(catalog);
const allNodes = (layout: NavigationLayout) =>
  layout.items.flatMap((node) => [node, ...(node.children ?? [])]);
const ids = (nodes: NavigationNode[]) => nodes.map((node) => node.id);
const rootIds = (layout: NavigationLayout) => ids(layout.items);
const directory = (layout: NavigationLayout, id = "directory:menu.tools") =>
  layout.items.find((node) => node.id === id)!;
const visible = (config: IMenuItem[], layout: NavigationLayout) =>
  buildVisibleNavigationMenu(config, layout, capabilities, t);

describe("navigation catalog reconciliation", () => {
  it("uses kind and official name token rather than route path or translated text as identity", () => {
    expect(navigationCatalogId({ name: "same" })).toBe("menu:same");
    expect(navigationCatalogId({ name: "same", children: [] })).toBe("directory:same");
    const layout = mergeNavigationLayout(catalog, undefined, t);

    expect(rootIds(layout)).toEqual(["menu:menu.resources", "directory:menu.tools"]);
    expect(ids(directory(layout).children!)).toEqual(["menu:menu.downloads", "menu:menu.files"]);
    expect(layout.knownCatalogIds).toEqual([
      "menu:menu.resources",
      "directory:menu.tools",
      "menu:menu.downloads",
      "menu:menu.files",
    ]);
    expect(mergeNavigationLayout(catalog, layout, t)).toEqual(layout);
  });

  it("appends new top-level items and new children without changing user placement or visibility", () => {
    let layout = mergeNavigationLayout(catalog, undefined, t);

    layout = addNavigationDirectory(layout, "custom-work", "My work");
    layout = moveNavigationNode(layout, "menu:menu.downloads", "custom-work");
    layout = setNavigationNodeHidden(layout, "menu:menu.files", true);
    const next = cloneCatalog();

    next[1].children!.unshift({ name: "menu.inspector", path: "/inspector" });
    next.unshift({ name: "menu.settings", path: "/settings" });
    next.push({ name: "menu.system", children: [{ name: "menu.more", path: "/more" }] });

    const merged = mergeNavigationLayout(next, layout, t);

    expect(rootIds(merged)).toEqual([
      "menu:menu.resources",
      "directory:menu.tools",
      "custom-work",
      "menu:menu.settings",
      "directory:menu.system",
    ]);
    expect(ids(directory(merged).children!)).toEqual(["menu:menu.files", "menu:menu.inspector"]);
    expect(directory(merged).children![0].hidden).toBe(true);
    expect(ids(directory(merged, "custom-work").children!)).toEqual(["menu:menu.downloads"]);
    expect(allNodes(merged).filter((node) => node.id === "menu:menu.downloads")).toHaveLength(1);
    expect(
      visible(next, merged).flatMap((node) => [
        node.label,
        ...(node.children?.map((child) => child.label) ?? []),
      ]),
    ).not.toContain("Files");
    expect(mergeNavigationLayout(next, merged, t)).toEqual(merged);
  });

  it("merges a new default directory into a same-name custom directory and remembers that alias", () => {
    let layout = mergeNavigationLayout(catalog.slice(0, 1), undefined, t);

    layout = addNavigationDirectory(layout, "user-tools", " Tools ");
    layout = moveNavigationNode(layout, "menu:menu.resources", "user-tools");
    const merged = mergeNavigationLayout(catalog, layout, t);

    expect(rootIds(merged)).toEqual(["user-tools"]);
    expect(ids(directory(merged, "user-tools").children!)).toEqual([
      "menu:menu.resources",
      "menu:menu.downloads",
      "menu:menu.files",
    ]);
    expect(merged.directoryAliases).toEqual({ "directory:menu.tools": "user-tools" });

    const renamed = renameNavigationDirectory(merged, "user-tools", "My utilities");
    const next = cloneCatalog();

    next[1].children!.push({ name: "menu.inspector", path: "/inspector" });
    const updated = mergeNavigationLayout(next, renamed, t);

    expect(rootIds(updated)).toEqual(["user-tools"]);
    expect(ids(directory(updated, "user-tools").children!)).toEqual([
      "menu:menu.resources",
      "menu:menu.downloads",
      "menu:menu.files",
      "menu:menu.inspector",
    ]);
    expect(directory(updated, "user-tools").name).toBe("My utilities");
  });

  it("does not merge a same-name menu and directory or distinct functions with the same translated label", () => {
    const config: IMenuItem[] = [
      { name: "menu.tools", path: "/tools" },
      { name: "menu.tools", children: [{ name: "menu.files", path: "/files" }] },
      { name: "menu.a", path: "/a" },
      { name: "menu.b", path: "/b" },
    ];
    const translate = (key: string) => (key === "menu.a" || key === "menu.b" ? "Overview" : t(key));
    const layout = mergeNavigationLayout(config, undefined, translate);

    expect(rootIds(layout)).toEqual([
      "menu:menu.tools",
      "directory:menu.tools",
      "menu:menu.a",
      "menu:menu.b",
    ]);
    const rendered = buildVisibleNavigationMenu(config, layout, capabilities, translate);

    expect(rendered.filter((node) => node.label === "Overview").map((node) => node.path)).toEqual([
      "/a",
      "/b",
    ]);
  });

  it("retains an unknown former menu in saved state and appends an officially renamed menu even at the same path", () => {
    let layout = mergeNavigationLayout(catalog, undefined, t);

    layout = setNavigationNodeHidden(layout, "menu:menu.resources", true);
    const next = cloneCatalog();

    next[0].name = "menu.newResources";
    const merged = mergeNavigationLayout(next, layout, t);

    expect(rootIds(merged)).toEqual([
      "menu:menu.resources",
      "directory:menu.tools",
      "menu:menu.newResources",
    ]);
    expect(merged.items[0]).toEqual({ id: "menu:menu.resources", kind: "menu", hidden: true });
    expect(merged.items[merged.items.length - 1]?.hidden).toBeUndefined();
    expect(visible(next, merged).find((node) => node.path === "/resource")?.id).toBe(
      "menu:menu.newResources",
    );
    expect(visible(catalog, merged).some((node) => node.id === "menu:menu.newResources")).toBe(
      false,
    );
  });

  it("appends an officially renamed directory and transfers only children still under its retired predecessor", () => {
    let layout = mergeNavigationLayout(catalog, undefined, t);

    layout = addNavigationDirectory(layout, "my-work", "My work");
    layout = moveNavigationNode(layout, "menu:menu.downloads", "my-work");
    layout = setNavigationNodeHidden(layout, "directory:menu.tools", true);
    layout = setNavigationNodeHidden(layout, "menu:menu.files", true);
    const next = cloneCatalog();

    next[1].name = "menu.newTools";
    next[1].children!.unshift({ name: "menu.inspector", path: "/inspector" });
    const merged = mergeNavigationLayout(next, layout, t);

    expect(rootIds(merged)).toEqual([
      "menu:menu.resources",
      "directory:menu.tools",
      "my-work",
      "directory:menu.newTools",
    ]);
    expect(directory(merged).children).toEqual([]);
    expect(ids(directory(merged, "my-work").children!)).toEqual(["menu:menu.downloads"]);
    expect(directory(merged, "directory:menu.newTools").children).toEqual([
      { id: "menu:menu.files", kind: "menu", hidden: true },
      { id: "menu:menu.inspector", kind: "menu" },
    ]);
    expect(directory(merged, "directory:menu.newTools").hidden).toBeUndefined();
    expect(visible(next, merged).map((node) => node.id)).not.toContain("directory:menu.tools");
    expect(mergeNavigationLayout(next, merged, t)).toEqual(merged);
  });

  it("preserves user child order when transferring a renamed directory before appending its new default children", () => {
    let layout = mergeNavigationLayout(catalog, undefined, t);

    layout = moveNavigationNode(layout, "menu:menu.files", "directory:menu.tools", 0);
    const next = cloneCatalog();

    next[1].name = "menu.newTools";
    next[1].children!.unshift({ name: "menu.inspector", path: "/inspector" });
    const merged = mergeNavigationLayout(next, layout, t);

    expect(ids(directory(merged, "directory:menu.newTools").children!)).toEqual([
      "menu:menu.files",
      "menu:menu.downloads",
      "menu:menu.inspector",
    ]);
  });

  it("does not mistake a custom directory for a retired official directory", () => {
    let layout = mergeNavigationLayout(catalog, undefined, t);

    layout = addNavigationDirectory(layout, "directory:user-prefix", "My tools");
    layout = moveNavigationNode(layout, "menu:menu.files", "directory:user-prefix");
    const next = cloneCatalog();

    next[1].name = "menu.newTools";
    const merged = mergeNavigationLayout(next, layout, t);

    expect(ids(directory(merged, "directory:user-prefix").children!)).toEqual(["menu:menu.files"]);
    expect(visible(next, merged).find((node) => node.id === "directory:user-prefix")?.label).toBe(
      "My tools",
    );
  });

  it("keeps a user-renamed official directory and its menus when the official name token changes", () => {
    let layout = mergeNavigationLayout(catalog, undefined, t);

    layout = renameNavigationDirectory(layout, "directory:menu.tools", "Foo");
    const next = cloneCatalog();

    next[1].name = "menu.newTools";
    next[1].children!.push({ name: "menu.inspector", path: "/inspector" });
    const merged = mergeNavigationLayout(next, layout, t);

    expect(directory(merged).name).toBe("Foo");
    expect(ids(directory(merged).children!)).toEqual(["menu:menu.downloads", "menu:menu.files"]);
    expect(ids(directory(merged, "directory:menu.newTools").children!)).toEqual([
      "menu:menu.inspector",
    ]);
    expect(visible(next, merged).find((node) => node.id === "directory:menu.tools")?.label).toBe(
      "Foo",
    );
    expect(rootIds(merged)[rootIds(merged).length - 1]).toBe("directory:menu.newTools");
  });

  it("keeps aliases, identity and placement across language switches and ordinary translation edits", () => {
    let layout = addNavigationDirectory(empty, "user-tools", "工具");
    const cn = (key: string) => (key === "menu.tools" ? "工具" : `中文:${key}`);

    layout = mergeNavigationLayout(catalog, layout, cn);
    expect(layout.directoryAliases).toEqual({ "directory:menu.tools": "user-tools" });
    const english = mergeNavigationLayout(catalog, layout, t);

    expect(english).toEqual(layout);
    expect(visible(catalog, english).find((node) => node.id === "user-tools")?.label).toBe("工具");
    const editedTranslation = (key: string) =>
      key === "menu.resources" ? "Library items" : t(key);

    expect(mergeNavigationLayout(catalog, english, editedTranslation)).toEqual(english);
    expect(
      buildVisibleNavigationMenu(catalog, english, capabilities, editedTranslation)[0].label,
    ).toBe("工具");
  });

  it("retains unknown saved items without rendering them and restores them when their catalog returns", () => {
    const original = mergeNavigationLayout(catalog, undefined, t);
    const olderCatalog = catalog.slice(0, 1);
    const downgraded = mergeNavigationLayout(olderCatalog, original, t);

    expect(downgraded).toEqual(original);
    expect(visible(olderCatalog, downgraded).map((node) => node.id)).toEqual([
      "menu:menu.resources",
    ]);
    expect(mergeNavigationLayout(catalog, downgraded, t)).toEqual(original);
    expect(visible(catalog, downgraded)).toHaveLength(2);
  });

  it("recovers live menus to the top level when their official former directory is removed", () => {
    const original = mergeNavigationLayout(catalog, undefined, t);
    const next = [catalog[0], ...catalog[1].children!];
    const merged = mergeNavigationLayout(next, original, t);

    expect(rootIds(merged)).toEqual([
      "menu:menu.resources",
      "directory:menu.tools",
      "menu:menu.downloads",
      "menu:menu.files",
    ]);
    expect(directory(merged).children).toEqual([]);
    expect(visible(next, merged).map((node) => node.path)).toEqual([
      "/resource",
      "/downloads",
      "/files",
    ]);
  });

  it("ignores malformed nodes, nested directories, duplicate IDs and user-provided menu labels", () => {
    const malformed = {
      version: 1,
      items: [
        null,
        { id: "", kind: "menu" },
        { id: "bad-kind", kind: "link" },
        {
          id: "menu:menu.resources",
          kind: "menu",
          name: "Rename a fixed function",
          children: [{ id: "nested", kind: "menu" }],
        },
        {
          id: "user-tools",
          kind: "directory",
          name: "My tools",
          children: [
            { id: "menu:menu.resources", kind: "menu" },
            { id: "three-levels", kind: "directory", children: [] },
            { id: "menu:menu.files", kind: "menu", hidden: "yes" },
          ],
        },
      ],
      knownCatalogIds: ["menu:menu.resources", "menu:menu.files", null, "menu:menu.files"],
      directoryAliases: { bad: "user-tools", "directory:self": "directory:self" },
    };
    const merged = mergeNavigationLayout(catalog, malformed, t);

    expect(allNodes(merged).filter((node) => node.id === "menu:menu.resources")).toHaveLength(1);
    expect(
      allNodes(merged).find((node) => node.id === "menu:menu.resources")?.name,
    ).toBeUndefined();
    expect(allNodes(merged).find((node) => node.id === "menu:menu.files")?.hidden).toBeUndefined();
    expect(
      allNodes(merged).some((node) => node.id === "three-levels" || node.id === "nested"),
    ).toBe(false);
    expect(visible(catalog, merged)[0].label).toBe("Resources");
    expect(merged.directoryAliases).toBeUndefined();
  });

  it.each([null, false, "invalid", { version: 2, items: [] }, { version: 1, items: "invalid" }])(
    "falls back to the current defaults for an unusable saved layout: %j",
    (stored) => {
      expect(mergeNavigationLayout(catalog, stored, t)).toEqual(
        mergeNavigationLayout(catalog, undefined, t),
      );
    },
  );

  it("does not loop on corrupt aliases or mutate either its saved layout or catalog", () => {
    const saved: NavigationLayout = {
      version: 1,
      items: [{ id: "user-tools", kind: "directory", name: "Tools", children: [] }],
      knownCatalogIds: [],
      directoryAliases: {
        "directory:menu.tools": "directory:cycle",
        "directory:cycle": "directory:menu.tools",
      },
    };
    const before = structuredClone(saved);
    const catalogBefore = cloneCatalog();
    const merged = mergeNavigationLayout(catalog, saved, t);

    expect(rootIds(merged)).toEqual(["user-tools", "menu:menu.resources"]);
    expect(merged.directoryAliases?.["directory:menu.tools"]).toBe("user-tools");
    expect(saved).toEqual(before);
    expect(catalog).toEqual(catalogBefore);
  });

  it("recreates a directory whose earlier same-name merge target has retired, without losing its live children", () => {
    const before: IMenuItem[] = [
      { name: "menu.tools", children: [{ name: "menu.files", path: "/files" }] },
      { name: "menu.utilities", children: [{ name: "menu.downloads", path: "/downloads" }] },
    ];
    const translate = (key: string) => (key === "menu.utilities" ? "Tools" : t(key));
    const saved = mergeNavigationLayout(before, undefined, translate);

    expect(saved.directoryAliases).toEqual({ "directory:menu.utilities": "directory:menu.tools" });
    const after = [
      {
        name: "menu.utilities",
        children: [
          { name: "menu.inspector", path: "/inspector" },
          { name: "menu.downloads", path: "/downloads" },
        ],
      },
    ];
    const merged = mergeNavigationLayout(after, saved, translate);

    expect(rootIds(merged)).toEqual(["directory:menu.tools", "directory:menu.utilities"]);
    expect(ids(directory(merged, "directory:menu.utilities").children!)).toEqual([
      "menu:menu.downloads",
      "menu:menu.inspector",
    ]);
    expect(merged.directoryAliases).toBeUndefined();
    expect(
      buildVisibleNavigationMenu(after, merged, capabilities, translate).map((node) => node.id),
    ).toEqual(["directory:menu.utilities"]);
  });
  it.each([
    { "directory:menu.tools": "directory:cycle", "directory:cycle": "directory:menu.tools" },
    { "directory:menu.tools": "missing-target" },
  ])(
    "recovers ordinary defaults despite a corrupt directory alias target: %j",
    (directoryAliases) => {
      const merged = mergeNavigationLayout(catalog, { ...empty, directoryAliases }, t);

      expect(rootIds(merged)).toEqual(["menu:menu.resources", "directory:menu.tools"]);
      expect(ids(directory(merged).children!)).toEqual(["menu:menu.downloads", "menu:menu.files"]);
      expect(visible(catalog, merged)).toHaveLength(2);
    },
  );
});

describe("capabilities after custom menu placement", () => {
  const restricted: IMenuItem[] = [
    catalog[0],
    {
      name: "menu.computer",
      pureClientOnly: true,
      children: [{ name: "menu.mapping", path: "/mapping" }],
    },
    {
      name: "menu.federation",
      localNodeOnly: true,
      children: [{ name: "menu.devices", path: "/devices" }],
    },
  ];

  it("does not let moving a restricted function to a custom directory bypass its original ancestor flags", () => {
    let layout = mergeNavigationLayout(restricted, undefined, t);

    layout = addNavigationDirectory(layout, "work", "My work");
    layout = moveNavigationNode(layout, "menu:menu.mapping", "work");
    layout = moveNavigationNode(layout, "menu:menu.devices", "work");
    layout = moveNavigationNode(layout, "menu:menu.resources", "work");
    expect(
      visible(restricted, layout)
        .find((node) => node.id === "work")
        ?.children?.map((node) => node.path),
    ).toEqual(["/resource"]);
    const desktop = buildVisibleNavigationMenu(
      restricted,
      layout,
      { isPureClient: true, isLocalNode: false },
      t,
    );

    expect(desktop.find((node) => node.id === "work")?.children?.map((node) => node.path)).toEqual([
      "/mapping",
      "/resource",
    ]);
    const localNode = buildVisibleNavigationMenu(
      restricted,
      layout,
      { isPureClient: false, isLocalNode: true },
      t,
    );

    expect(
      localNode.find((node) => node.id === "work")?.children?.map((node) => node.path),
    ).toEqual(["/devices", "/resource"]);
  });

  it("does not apply a destination directory's default restrictions to an ordinary menu moved into it", () => {
    let layout = mergeNavigationLayout(restricted, undefined, t);

    layout = moveNavigationNode(layout, "menu:menu.resources", "directory:menu.computer");
    const rendered = visible(restricted, layout);

    expect(rendered).toHaveLength(1);
    expect(rendered[0]).toMatchObject({ id: "directory:menu.computer", pureClientOnly: false });
    expect(rendered[0].children?.map((node) => node.path)).toEqual(["/resource"]);
    expect(directory(layout, "directory:menu.computer").children).toHaveLength(2);
  });

  it("keeps empty and currently unavailable directories in saved state but hides them from the navbar", () => {
    const layout = mergeNavigationLayout(restricted, undefined, t);

    expect(rootIds(layout)).toHaveLength(3);
    expect(visible(restricted, layout).map((node) => node.id)).toEqual(["menu:menu.resources"]);
    expect(mergeNavigationLayout(restricted, layout, t)).toEqual(layout);
  });
});

describe("immutable navigation editing", () => {
  it("allows only top-level directories and fixed-label menus at root or under a directory", () => {
    const original = mergeNavigationLayout(catalog, undefined, t);
    const added = addNavigationDirectory(original, "custom", "My work");
    const renamed = renameNavigationDirectory(added, "custom", "Work");

    expect(renameNavigationDirectory(renamed, "menu:menu.resources", "Edited function")).toBe(
      renamed,
    );
    expect(moveNavigationNode(renamed, "directory:menu.tools", "custom")).toBe(renamed);
    expect(moveNavigationNode(renamed, "menu:menu.files", "menu:menu.resources")).toBe(renamed);
    const moved = moveNavigationNode(renamed, "menu:menu.files", "custom");

    expect(ids(directory(moved, "custom").children!)).toEqual(["menu:menu.files"]);
    const promoted = moveNavigationNode(moved, "menu:menu.files", null, 0);

    expect(rootIds(promoted)[0]).toBe("menu:menu.files");
    expect(rootIds(original)).toEqual(["menu:menu.resources", "directory:menu.tools"]);
    expect(ids(directory(original).children!)).toEqual(["menu:menu.downloads", "menu:menu.files"]);
  });

  it("removes a directory without losing its functions, preserving their order and effective hidden state", () => {
    const original = mergeNavigationLayout(catalog, undefined, t);
    const hidden = setNavigationNodeHidden(original, "directory:menu.tools", true);
    const removed = removeNavigationDirectory(hidden, "directory:menu.tools");

    expect(rootIds(removed)).toEqual([
      "menu:menu.resources",
      "menu:menu.downloads",
      "menu:menu.files",
    ]);
    expect(removed.items.slice(1).map((node) => node.hidden)).toEqual([true, true]);
    expect(removeNavigationDirectory(removed, "menu:menu.resources")).toBe(removed);
    expect(mergeNavigationLayout(catalog, removed, t)).toEqual(removed);
  });

  it("rejects empty or too-long labels, duplicate IDs and missing destinations without changing saved state", () => {
    const layout = mergeNavigationLayout(catalog, undefined, t);

    expect(addNavigationDirectory(layout, "custom", " ")).toBe(layout);
    expect(addNavigationDirectory(layout, "custom", "a".repeat(81))).toBe(layout);
    expect(addNavigationDirectory(layout, "custom", "Work\nTools")).toBe(layout);
    expect(addNavigationDirectory(layout, "bad\u0001id", "Work")).toBe(layout);
    expect(addNavigationDirectory(layout, "menu:menu.resources", "Work")).toBe(layout);
    expect(renameNavigationDirectory(layout, "directory:menu.tools", " ")).toBe(layout);
    expect(renameNavigationDirectory(layout, "directory:menu.tools", "Work\nTools")).toBe(layout);
    expect(setNavigationNodeHidden(layout, "missing", true)).toBe(layout);
    expect(moveNavigationNode(layout, "missing", null)).toBe(layout);
    expect(moveNavigationNode(layout, "menu:menu.resources", "missing")).toBe(layout);
  });
});
