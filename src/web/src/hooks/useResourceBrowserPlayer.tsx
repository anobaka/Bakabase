import type { BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry } from "@/sdk/Api";
import type { Resource as ResourceModel } from "@/core/models/Resource";
import type { MediaBrowseScope } from "@/components/MediaPlayer/types";

import { useCallback } from "react";
import { useTranslation } from "react-i18next";

import { toast } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import MediaPlayer from "@/components/MediaPlayer";
import BApi from "@/sdk/BApi";
import { DataOrigin, IwFsType } from "@/sdk/constants";
import { mediaPathKey, selectInitialMediaIndex } from "@/components/MediaPlayer/media";
import { initialWindowBounds } from "@/components/Window/bounds";
import { MEDIA_PLAYER_WINDOW_PERSISTENCE_KEY } from "@/components/Window/persistence";
import { useResourceOwner } from "@/features/management/resourceContext";
import { ownerClient } from "@/features/management/api";
import ResourceBrowserWindow, {
  type ResourceBrowserFilesPage,
} from "@/features/management/ResourceBrowserWindow";

export interface ResourceBrowserPlayerOptions {
  scope?: MediaBrowseScope;
  onOpenAlternative?: (filePath: string) => void | Promise<void>;
  canOpenAlternative?: (filePath: string) => boolean;
}

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
  const owner = useResourceOwner();

  return useCallback(
    async (
      resource: ResourceModel,
      initialPath?: string,
      options: ResourceBrowserPlayerOptions = {},
    ) => {
      if (owner && !owner.isLocal) {
        const request = ownerClient(owner);
        const response = await request<ResourceBrowserFilesPage>("resourceBrowserFiles", {
          id: resource.id,
          query: {
            offset: ["0"],
            pageSize: ["100"],
            scope: [options.scope === "configured" ? "configured" : "all"],
            ...(initialPath ? { initialPath: [initialPath] } : {}),
          },
        });

        if (!response.data || (!response.data.items.length && options.scope !== "configured")) {
          toast.default(t<string>("resource.play.noFilesToPreview"));

          return;
        }
        const bounds = initialWindowBounds(
          { initialSize: { width: 1120, height: 760 } },
          window.innerWidth,
          window.innerHeight,
        );

        createWindow(
          ResourceBrowserWindow,
          {
            source: owner,
            resource,
            initialPage: response.data,
            initialPath,
            options,
          },
          {
            title: `${resource.displayName || resource.path} · ${owner.name}`,
            persistenceKey: MEDIA_PLAYER_WINDOW_PERSISTENCE_KEY,
            persistent: true,
            initialSize: { width: bounds.width, height: bounds.height },
            initialPosition: { x: bounds.x, y: bounds.y },
          },
        );
        const selected =
          response.data.items.find((item) => item.key === initialPath) ?? response.data.items[0];

        if (selected)
          void request("resourceMarkPlayed", {
            id: resource.id,
            query: { item: [selected.key] },
          }).catch(() => {});

        return;
      }
      let rsp;

      try {
        rsp = resource.isFile
          ? { code: 0, data: [resource.path] }
          : await BApi.file.getAllFiles(
              { path: resource.path },
              { signal: AbortSignal.timeout(15_000) },
            );
      } catch {
        toast.danger(t("mediaPlayer.filesFailed"));

        return;
      }

      if (rsp.code || !rsp.data) {
        toast.danger(t("mediaPlayer.filesFailed"));

        return;
      }

      if (rsp.data.length === 0) {
        toast.default(t<string>("resource.play.noFilesToPreview"));

        return;
      }

      const entries = rsp.data.flatMap((path) => (path ? [toEntry(path)] : []));
      let locatedPaths =
        resource.playableItems
          ?.filter((item) => item.origin === DataOrigin.FileSystem)
          .map((item) => item.key) ?? [];
      let candidatesKnown = resource.playableItems !== undefined;

      if (!initialPath || options.scope === "configured") {
        try {
          const located = await BApi.resource.getResourcePlayableItems(resource.id, {
            signal: AbortSignal.timeout(10_000),
          });

          if (!located.code && located.data) {
            candidatesKnown = true;
            locatedPaths = located.data
              .filter((item) => item.origin === DataOrigin.FileSystem)
              .map((item) => item.key)
              .filter((path): path is string => !!path);
          }
        } catch {
          // Cached located files or the first useful media file still open if discovery fails.
        }
      }
      if (options.scope === "configured" && !candidatesKnown) {
        toast.danger(t("mediaPlayer.scope.failed"));

        return;
      }
      const candidateKeys = new Set(locatedPaths.map(mediaPathKey));
      const initialEntries =
        options.scope === "configured"
          ? entries.filter((entry) => candidateKeys.has(mediaPathKey(entry.path)))
          : entries;
      const selectedEntry =
        initialEntries[
          selectInitialMediaIndex(initialEntries, initialPath ? [initialPath] : locatedPaths)
        ];
      const defaultActiveIndex = selectedEntry ? entries.indexOf(selectedEntry) : 0;
      const { x, y, width, height } = initialWindowBounds(
        { initialSize: { width: 1120, height: 760 } },
        window.innerWidth,
        window.innerHeight,
      );

      createWindow(
        MediaPlayer,
        {
          entries,
          defaultActiveIndex,
          resourceScope: !resource.isFile,
          initialScope: options.scope,
          configuredPaths: candidatesKnown ? locatedPaths : undefined,
          onOpenAlternative: options.onOpenAlternative,
          canOpenAlternative: options.canOpenAlternative,
          renderOperations: (): any => {},
        },
        {
          title: resource.displayName,
          persistenceKey: MEDIA_PLAYER_WINDOW_PERSISTENCE_KEY,
          persistent: true,
          initialSize: { width, height },
          initialPosition: { x, y },
        },
      );

      // Playing through the API records history as a side effect; playing in the
      // browser never did, so "last played" would stay empty for everything
      // watched from another device. Failing to record must not break playback.
      if (!selectedEntry) return;
      try {
        await BApi.resource.markResourceAsPlayed(resource.id, {
          item: selectedEntry.path,
        });
      } catch {
        // ignored
      }
    },
    [createWindow, t, owner],
  );
};
