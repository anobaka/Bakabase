import type { BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry } from "@/sdk/Api";
import type { Resource as ResourceModel } from "@/core/models/Resource";

import { useCallback } from "react";
import { useTranslation } from "react-i18next";
import toast from "react-hot-toast";

import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import MediaPlayer from "@/components/MediaPlayer";
import BApi from "@/sdk/BApi";
import { IwFsType } from "@/sdk/constants";

const toEntry = (path: string): BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry => {
  const name = path.split(/[/\\]/).pop() || path;
  const ext = name.includes(".") ? name.split(".").pop() : undefined;

  return {
    path,
    name,
    meaningfulName: name,
    ext,
    type: IwFsType.Unknown,
    passwordsForDecompressing: [],
  };
};

/**
 * Opens a resource in the built-in browser player.
 *
 * This is what the Play button does on a device that is not the host: launching
 * a player through the API would start it on the host's desktop, where nobody is
 * watching. Streaming into the page is the only playback the requesting device
 * can actually see.
 */
export const useResourceBrowserPlayer = () => {
  const { t } = useTranslation();
  const { createWindow } = useBakabaseContext();

  return useCallback(
    async (resource: ResourceModel, initialPath?: string) => {
      const rsp = await BApi.file.getAllFiles({ path: resource.path });

      if (rsp.code || !rsp.data) {
        return;
      }

      if (rsp.data.length === 0) {
        toast(t<string>("resource.play.noFilesToPreview"));

        return;
      }

      const entries = rsp.data.map(toEntry);
      const defaultActiveIndex = initialPath
        ? Math.max(
            0,
            entries.findIndex((e) => e.path === initialPath),
          )
        : 0;

      createWindow(
        MediaPlayer,
        { entries, defaultActiveIndex, renderOperations: (): any => {} },
        { title: resource.displayName, persistent: true },
      );

      // Playing through the API records history as a side effect; playing in the
      // browser never did, so "last played" would stay empty for everything
      // watched from another device. Failing to record must not break playback.
      try {
        await BApi.resource.markResourceAsPlayed(resource.id, {
          item: initialPath ?? entries[defaultActiveIndex]?.path,
        });
      } catch {
        // ignored
      }
    },
    [createWindow, t],
  );
};
