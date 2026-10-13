import type { IMenuItem } from "./menuConfig";

export interface NavigationNode {
  id: string;
  kind: "directory" | "menu";
  /** A user label or a label override for a directory. Menu labels stay in the catalog. */
  name?: string;
  hidden?: boolean;
  children?: NavigationNode[];
}

export interface NavigationLayout {
  version: 1;
  items: NavigationNode[];
  knownCatalogIds: string[];
  directoryAliases?: Record<string, string>;
}

export interface NavigationCapabilities {
  isPureClient: boolean;
  isLocalNode: boolean;
}

export interface NavigationMenuItem extends Omit<IMenuItem, "children"> {
  id: string;
  kind: NavigationNode["kind"];
  label: string;
  children?: NavigationMenuItem[];
}

type Translate = (key: string) => string;
type CatalogEntry = {
  id: string;
  kind: NavigationNode["kind"];
  item: IMenuItem;
  parentId?: string;
  pureClientOnly: boolean;
  localNodeOnly: boolean;
};

export const NAVIGATION_MAX_NODES = 512;
export const NAVIGATION_MAX_NAME_LENGTH = 80;
export const NAVIGATION_MAX_ID_LENGTH = 128;
const MAX_KNOWN_IDS = 1024;
const MAX_DIRECTORY_ALIASES = 512;
const CONTROL_CHARACTERS = /\p{Cc}/u;

/** A route path is deliberately not identity: an official name-token change is a new item. */
export function navigationCatalogId(item: Pick<IMenuItem, "name" | "children">): string {
  return `${item.children !== undefined ? "directory" : "menu"}:${item.name}`;
}

function catalogEntries(catalog: readonly IMenuItem[]): CatalogEntry[] {
  const result: CatalogEntry[] = [];
  const seen = new Set<string>();
  const visit = (
    items: readonly IMenuItem[],
    parentId?: string,
    inheritedPureClient = false,
    inheritedLocalNode = false,
  ) => {
    for (const item of items) {
      if (!item || typeof item.name !== "string" || !item.name) continue;
      const id = navigationCatalogId(item);
      const pureClientOnly = inheritedPureClient || !!item.pureClientOnly;
      const localNodeOnly = inheritedLocalNode || !!item.localNodeOnly;
      const kind = item.children !== undefined ? "directory" : "menu";

      if (!seen.has(id)) {
        result.push({ id, kind, item, parentId, pureClientOnly, localNodeOnly });
        seen.add(id);
      }
      if (item.children) visit(item.children, id, pureClientOnly, localNodeOnly);
    }
  };

  visit(catalog);

  return result;
}

function validId(value: unknown): value is string {
  return (
    typeof value === "string" &&
    value.length > 0 &&
    value.length <= NAVIGATION_MAX_ID_LENGTH &&
    value.trim() === value &&
    !CONTROL_CHARACTERS.test(value)
  );
}

function normalizedName(value: string): string {
  return value.trim().normalize("NFC").toLowerCase();
}

function sanitizeLayout(stored: unknown): NavigationLayout {
  const empty: NavigationLayout = { version: 1, items: [], knownCatalogIds: [] };

  if (!stored || typeof stored !== "object") return empty;
  const raw = stored as Partial<NavigationLayout>;

  if (raw.version !== 1 || !Array.isArray(raw.items)) return empty;
  const seen = new Set<string>();
  let count = 0;
  const readNodes = (nodes: unknown[], root: boolean): NavigationNode[] => {
    const items: NavigationNode[] = [];

    for (const value of nodes.slice(0, NAVIGATION_MAX_NODES)) {
      if (!value || typeof value !== "object" || count >= NAVIGATION_MAX_NODES) continue;
      const node = value as Partial<NavigationNode>;

      if (
        !validId(node.id) ||
        seen.has(node.id) ||
        (node.kind !== "menu" && node.kind !== "directory") ||
        (!root && node.kind === "directory")
      )
        continue;
      seen.add(node.id);
      count++;
      const next: NavigationNode = { id: node.id, kind: node.kind };

      if (typeof node.hidden === "boolean") next.hidden = node.hidden;
      if (node.kind === "directory") {
        if (
          typeof node.name === "string" &&
          node.name.trim() &&
          !CONTROL_CHARACTERS.test(node.name.trim())
        ) {
          next.name = node.name.trim().slice(0, NAVIGATION_MAX_NAME_LENGTH);
        }
        next.children = Array.isArray(node.children) ? readNodes(node.children, false) : [];
      }
      items.push(next);
    }

    return items;
  };
  const items = readNodes(raw.items, true);
  const knownCatalogIds = Array.isArray(raw.knownCatalogIds)
    ? [...new Set(raw.knownCatalogIds.filter(validId))].slice(0, MAX_KNOWN_IDS)
    : [];
  const aliasEntries =
    raw.directoryAliases && typeof raw.directoryAliases === "object"
      ? Object.entries(raw.directoryAliases)
          .filter(
            ([id, target]) =>
              validId(id) && id.startsWith("directory:") && validId(target) && id !== target,
          )
          .slice(0, MAX_DIRECTORY_ALIASES)
      : [];

  return {
    version: 1,
    items,
    knownCatalogIds,
    ...(aliasEntries.length ? { directoryAliases: Object.fromEntries(aliasEntries) } : {}),
  };
}

function locate(
  items: NavigationNode[],
  id: string,
):
  | {
      node: NavigationNode;
      siblings: NavigationNode[];
      index: number;
      parent?: NavigationNode;
    }
  | undefined {
  for (let index = 0; index < items.length; index++) {
    const node = items[index];

    if (node.id === id) return { node, siblings: items, index };
    const childIndex = node.children?.findIndex((child) => child.id === id) ?? -1;

    if (childIndex >= 0) {
      return {
        node: node.children![childIndex],
        siblings: node.children!,
        index: childIndex,
        parent: node,
      };
    }
  }
}

function resolveAlias(id: string, aliases: Record<string, string>): string | undefined {
  const seen = new Set<string>();
  let current = id;

  while (Object.prototype.hasOwnProperty.call(aliases, current)) {
    if (seen.has(current)) return undefined;
    seen.add(current);
    current = aliases[current];
  }

  return current;
}

/** Merge catalog additions while retaining user placement, hidden state and unknown saved nodes. */
export function mergeNavigationLayout(
  catalog: readonly IMenuItem[],
  stored: unknown,
  translate: Translate,
): NavigationLayout {
  const layout = sanitizeLayout(stored);
  const entries = catalogEntries(catalog);
  const byId = new Map(entries.map((entry) => [entry.id, entry]));
  const known = new Set(layout.knownCatalogIds);
  const aliases = { ...layout.directoryAliases };
  const directoryLabel = (node: NavigationNode) =>
    node.name ?? (byId.get(node.id) ? translate(byId.get(node.id)!.item.name) : "");
  const isRetiredDirectory = (node: NavigationNode) =>
    node.kind === "directory" &&
    !node.name &&
    known.has(node.id) &&
    node.id.startsWith("directory:") &&
    !byId.has(node.id);
  const getDirectory = (id: string): NavigationNode | undefined => {
    const direct = layout.items.find(
      (node) => node.id === id && node.kind === "directory" && !isRetiredDirectory(node),
    );

    if (direct) return direct;
    const targetId = resolveAlias(id, aliases);

    return targetId
      ? layout.items.find(
          (node) => node.id === targetId && node.kind === "directory" && !isRetiredDirectory(node),
        )
      : undefined;
  };
  const transferRetiredChildren = (parentId: string | undefined, destination: NavigationNode[]) => {
    for (const retired of layout.items.filter(isRetiredDirectory)) {
      const liveChildren = (retired.children ?? []).filter(
        (child) => byId.get(child.id)?.kind === "menu" && byId.get(child.id)?.parentId === parentId,
      );

      retired.children = (retired.children ?? []).filter((child) => !liveChildren.includes(child));
      destination.push(...liveChildren);
    }
  };

  for (const entry of entries) {
    if (entry.kind === "directory") {
      const aliasedId = resolveAlias(entry.id, aliases);
      const aliasTargetRetired =
        aliasedId !== entry.id &&
        layout.items.some((node) => node.id === aliasedId && isRetiredDirectory(node));

      if (aliasTargetRetired) delete aliases[entry.id];
      let directory = getDirectory(entry.id);

      if (!directory) {
        const match = layout.items.find(
          (node) =>
            node.kind === "directory" &&
            !isRetiredDirectory(node) &&
            normalizedName(directoryLabel(node)) === normalizedName(translate(entry.item.name)),
        );

        if (match) {
          aliases[entry.id] = match.id;
          directory = match;
        } else if (!known.has(entry.id) || aliasTargetRetired) {
          directory = { id: entry.id, kind: "directory", children: [] };
          layout.items.push(directory);
        }
      }
      if (directory) {
        directory.children ??= [];
        transferRetiredChildren(entry.id, directory.children);
      }
      continue;
    }

    const existing = locate(layout.items, entry.id);
    const directory = entry.parentId ? getDirectory(entry.parentId) : undefined;

    if (existing) {
      // An officially renamed directory is appended like a new directory. Its
      // live children follow only if still under the retired built-in directory;
      // user-moved menus, and each child's own hidden state, remain untouched.
      if (!entry.parentId && existing.parent && isRetiredDirectory(existing.parent)) {
        transferRetiredChildren(undefined, layout.items);
      }
      continue;
    }
    if (known.has(entry.id)) continue;
    const node: NavigationNode = { id: entry.id, kind: "menu" };

    if (directory) {
      directory.children ??= [];
      directory.children.push(node);
    } else {
      layout.items.push(node);
    }
  }

  layout.knownCatalogIds = [
    ...new Set([...layout.knownCatalogIds, ...entries.map((entry) => entry.id)]),
  ];
  if (Object.keys(aliases).length) layout.directoryAliases = aliases;
  else delete layout.directoryAliases;

  return layout;
}

/** Capability checks come from the original catalog ancestry, even after moving a menu. */
export function buildVisibleNavigationMenu(
  catalog: readonly IMenuItem[],
  layout: NavigationLayout,
  capabilities: NavigationCapabilities,
  translate: Translate,
): NavigationMenuItem[] {
  const entries = catalogEntries(catalog);
  const byId = new Map(entries.map((entry) => [entry.id, entry]));
  const known = new Set(layout.knownCatalogIds);
  const admitted = (entry: CatalogEntry) =>
    (!entry.pureClientOnly || capabilities.isPureClient) &&
    (!entry.localNodeOnly || capabilities.isLocalNode);
  const convert = (nodes: NavigationNode[]): NavigationMenuItem[] =>
    nodes.flatMap<NavigationMenuItem>((node) => {
      if (node.hidden) return [];
      const entry = byId.get(node.id);

      if (entry && entry.kind !== node.kind) return [];
      if (node.kind === "menu") {
        if (!entry || !admitted(entry)) return [];

        return [
          {
            ...entry.item,
            children: undefined,
            id: node.id,
            kind: "menu" as const,
            label: translate(entry.item.name),
            pureClientOnly: entry.pureClientOnly,
            localNodeOnly: entry.localNodeOnly,
          },
        ];
      }
      if (!entry && !node.name && known.has(node.id) && node.id.startsWith("directory:")) return [];
      const label = node.name ?? (entry ? translate(entry.item.name) : "");

      if (!label.trim()) return [];
      const children = convert(node.children ?? []);

      if (children.length === 0) return [];

      return [
        {
          ...entry?.item,
          id: node.id,
          kind: "directory" as const,
          name: entry?.item.name ?? node.name ?? "",
          label,
          children,
          pureClientOnly: false,
          localNodeOnly: false,
        },
      ];
    });

  return convert(sanitizeLayout(layout).items);
}

function copyLayout(layout: NavigationLayout): NavigationLayout {
  return {
    ...layout,
    items: layout.items.map((node) => ({
      ...node,
      ...(node.children ? { children: node.children.map((child) => ({ ...child })) } : {}),
    })),
    knownCatalogIds: [...layout.knownCatalogIds],
    ...(layout.directoryAliases ? { directoryAliases: { ...layout.directoryAliases } } : {}),
  };
}

export function moveNavigationNode(
  layout: NavigationLayout,
  id: string,
  parentId: string | null,
  index?: number,
): NavigationLayout {
  const next = copyLayout(layout);
  const found = locate(next.items, id);

  if (!found || (found.node.kind === "directory" && parentId !== null)) return layout;
  const parent =
    parentId === null
      ? undefined
      : next.items.find((node) => node.id === parentId && node.kind === "directory");

  if (parentId !== null && !parent) return layout;
  const destination = parent ? (parent.children ??= []) : next.items;

  found.siblings.splice(found.index, 1);
  const position =
    index === undefined || !Number.isFinite(index)
      ? destination.length
      : Math.max(0, Math.min(destination.length, Math.trunc(index)));

  destination.splice(position, 0, found.node);

  return next;
}

export function renameNavigationDirectory(
  layout: NavigationLayout,
  id: string,
  name: string,
): NavigationLayout {
  const label = name.trim();

  if (!label || label.length > NAVIGATION_MAX_NAME_LENGTH || CONTROL_CHARACTERS.test(label))
    return layout;
  const next = copyLayout(layout);
  const found = locate(next.items, id);

  if (!found || found.node.kind !== "directory") return layout;
  found.node.name = label;

  return next;
}

export function setNavigationNodeHidden(
  layout: NavigationLayout,
  id: string,
  hidden: boolean,
): NavigationLayout {
  const next = copyLayout(layout);
  const found = locate(next.items, id);

  if (!found) return layout;
  found.node.hidden = hidden;

  return next;
}

export function addNavigationDirectory(
  layout: NavigationLayout,
  id: string,
  name: string,
): NavigationLayout {
  const label = name.trim();

  if (
    !validId(id) ||
    !label ||
    label.length > NAVIGATION_MAX_NAME_LENGTH ||
    CONTROL_CHARACTERS.test(label) ||
    locate(layout.items, id) ||
    layout.items.reduce((count, node) => count + 1 + (node.children?.length ?? 0), 0) >=
      NAVIGATION_MAX_NODES
  )
    return layout;
  const next = copyLayout(layout);

  next.items.push({ id, kind: "directory", name: label, children: [] });

  return next;
}

/** Removing a folder keeps its menus at the folder's old position on the top level. */
export function removeNavigationDirectory(layout: NavigationLayout, id: string): NavigationLayout {
  const next = copyLayout(layout);
  const found = locate(next.items, id);

  if (!found || found.parent || found.node.kind !== "directory") return layout;
  const children = (found.node.children ?? []).map((child) => ({
    ...child,
    ...(found.node.hidden ? { hidden: true } : {}),
  }));

  next.items.splice(found.index, 1, ...children);

  return next;
}
