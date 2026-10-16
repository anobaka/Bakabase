import type { IMenuItem } from "@/layouts/BasicLayout/components/PageNav/components/AntdMenu/menuConfig";
import type {
  NavigationCapabilities,
  NavigationMenuItem,
} from "@/layouts/BasicLayout/components/PageNav/components/AntdMenu/navigationLayout";

import { deviceSwitchPath } from "./deviceSwitchRoute";

import {
  buildVisibleNavigationMenu,
  mergeNavigationLayout,
} from "@/layouts/BasicLayout/components/PageNav/components/AntdMenu/navigationLayout";

/** Resolve against the destination's catalog, permissions and saved layout, never the source's. */
export function deviceSwitchDestination(
  requestedPath: string,
  catalog: readonly IMenuItem[],
  savedLayout: unknown,
  capabilities: NavigationCapabilities,
  translate: (key: string) => string = (key) => key,
): string {
  const layout = mergeNavigationLayout(catalog, savedLayout, translate);
  const visible = buildVisibleNavigationMenu(catalog, layout, capabilities, translate);
  const path = deviceSwitchPath(requestedPath);
  let match = "/";
  const visit = (items: readonly NavigationMenuItem[]) => {
    for (const item of items) {
      const candidate = item.path;

      // Detail IDs are only candidates for finding a menu ancestor. They are never
      // opened on another owner, even if that owner happens to have the same ID.
      if (
        candidate &&
        candidate !== "/" &&
        deviceSwitchPath(candidate) === candidate &&
        (path === candidate || path.startsWith(`${candidate}/`)) &&
        candidate.length > match.length
      )
        match = candidate;
      if (item.children) visit(item.children);
    }
  };

  visit(visible);

  return match;
}
