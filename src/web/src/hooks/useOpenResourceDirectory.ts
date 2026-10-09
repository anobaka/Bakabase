import { useCallback } from "react";
import { useTranslation } from "react-i18next";

import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import FolderLocationModal from "@/components/Resource/components/FolderLocationModal";
import { resourceFolderPath } from "@/components/Resource/resourceFolderPath";
import BApi from "@/sdk/BApi";
import { useUserSideActionsRunHere } from "@/stores/remoteAccess";

type FolderResource = { id: number; path?: string; directory?: string; isFile?: boolean };

/** One action for every resource surface, with the same desktop/relay/browser behavior. */
export const useOpenResourceDirectory = () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const userSideActionsRunHere = useUserSideActionsRunHere();

  const open = useCallback(
    (resource: FolderResource) => {
      if (!resource.path) return;
      if (userSideActionsRunHere) {
        // The desktop's relay intercepts this route and applies its path mappings.
        void BApi.resource.openResourceDirectory({ id: resource.id });

        return;
      }

      createPortal(FolderLocationModal, {
        path:
          (resource.isFile && resource.directory) ||
          resourceFolderPath(resource.path, resource.isFile),
      });
    },
    [createPortal, userSideActionsRunHere],
  );

  return {
    open,
    label: t<string>(
      userSideActionsRunHere ? "common.action.openFolder" : "resource.folderLocation.title",
    ),
  };
};
