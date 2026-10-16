import type { BakabaseInsideWorldModelsConfigsUINavigationNode } from "@/sdk/Api";
import type { NavigationNode } from "@/layouts/BasicLayout/components/PageNav/components/AntdMenu/navigationLayout";

import { lazy, Suspense, useState } from "react";
import { useTranslation } from "react-i18next";
import { MdOutlineDashboardCustomize } from "react-icons/md";

import { Button } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { useUiOptionsStore } from "@/stores/options";
import { useIsPureClient, useRemoteAccessStore } from "@/stores/remoteAccess";

// The route catalog imports this settings page. Load the catalog with the editor
// only after startup, so route initialization never reads its own unfinished export.
const NavigationEditor = lazy(() => import("./Editor"));

const navigationApiNode = (
  node: NavigationNode,
): BakabaseInsideWorldModelsConfigsUINavigationNode => ({
  id: node.id,
  kind: node.kind,
  name: node.name,
  hidden: node.hidden ?? false,
  children: node.children?.map(navigationApiNode),
});

export default function NavigationCustomization() {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(false);
  const uiOptionsStore = useUiOptionsStore();
  const isPureClient = useIsPureClient();
  const isLocalNode =
    useRemoteAccessStore(
      (state) => state.initialized && (state.federationAvailable ?? state.isLocal),
    ) && !isPureClient;

  return (
    <>
      <Button
        size="sm"
        startContent={<MdOutlineDashboardCustomize className="text-base" />}
        variant="flat"
        onPress={() => setVisible(true)}
      >
        {t<string>("navigationEditor.open.label")}
      </Button>
      {visible && (
        <Suspense fallback={null}>
          <NavigationEditor
            isOpen
            capabilities={{ isPureClient, isLocalNode: Boolean(isLocalNode) }}
            savedLayout={uiOptionsStore.data.navigationLayout}
            onClose={() => setVisible(false)}
            onSave={async (navigationLayout) => {
              const payload = {
                ...navigationLayout,
                directoryAliases: navigationLayout.directoryAliases ?? {},
                items: navigationLayout.items.map(navigationApiNode),
              };
              const response = await BApi.options.patchUiOptions({ navigationLayout: payload });

              if (response.code)
                throw new Error(
                  response.message ?? t<string>("navigationEditor.saveFailed.description"),
                );
              // Apply locally only after saving; the hub updates other windows on this service.
              uiOptionsStore.update({ navigationLayout: payload });
            }}
          />
        </Suspense>
      )}
    </>
  );
}
