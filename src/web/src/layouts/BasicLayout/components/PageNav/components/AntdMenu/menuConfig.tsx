import type { IconType } from "react-icons";
import type { RouteMenuItem } from "@/components/routesMenuConfig";

import { routesMenuConfig } from "@/components/routesMenuConfig";

export interface IMenuItem {
  name: string;
  path?: string;
  icon?: IconType;
  children?: IMenuItem[];
  isBeta?: boolean;
  isDeprecated?: boolean;
  pureClientOnly?: boolean;
  localNodeOnly?: boolean;
  hideInConsole?: boolean;
  nameInConsole?: string;
}

function extractMenu(config: RouteMenuItem[]): IMenuItem[] {
  return config
    .filter((r) => r.menu !== false) // Only include menu: true or undefined (default true)
    .map((r) => {
      const item: IMenuItem = {
        name: r.name,
        path: r.path,
        icon: r.icon,
        isBeta: r.isBeta,
        isDeprecated: r.isDeprecated,
        pureClientOnly: r.pureClientOnly,
        localNodeOnly: r.localNodeOnly,
        hideInConsole: r.hideInConsole,
        nameInConsole: r.nameInConsole,
      };

      if (r.children) item.children = extractMenu(r.children);

      return item;
    });
}

export const asideMenuConfig: IMenuItem[] = extractMenu(routesMenuConfig);
export const headerMenuConfig: IMenuItem[] = [];
