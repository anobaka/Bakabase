import type { NavigationEditorProps } from "@/layouts/BasicLayout/components/PageNav/components/NavigationEditor";

import NavigationEditor from "@/layouts/BasicLayout/components/PageNav/components/NavigationEditor";
import { asideMenuConfig } from "@/layouts/BasicLayout/components/PageNav/components/AntdMenu/menuConfig";

export default function Editor(props: Omit<NavigationEditorProps, "catalog">) {
  return <NavigationEditor {...props} catalog={asideMenuConfig} />;
}
