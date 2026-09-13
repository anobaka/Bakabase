"use client";

import React, { useEffect, useState } from "react";
import i18n from "i18next";
import _ from "lodash";
import { MdFolder, MdInsertDriveFile } from "react-icons/md";

import "./index.scss";

import { IconType, RuntimeMode } from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import { splitPathIntoSegments } from "@/components/utils";
import { useIconsStore } from "@/stores/icons";
import { useAppContextStore } from "@/stores/appContext";
import { useIsPureClient, useUserSideActionsRunHere } from "@/stores/remoteAccess";

type Props = {
  type: IconType;
  path?: string;
  size: number | string;
  disableCache?: boolean;
};

const buildCacheKey = (type: IconType, path?: string): string | undefined => {
  let suffix = "";

  switch (type) {
    case IconType.UnknownFile:
    case IconType.Directory:
      break;
    case IconType.Dynamic: {
      if (!path) {
        // console.error("Path is required for dynamic icon");
        return undefined;
      }

      if (path.endsWith(".exe") || path.endsWith(".app")) {
        suffix = path;
        break;
      }

      const filename = _.last(splitPathIntoSegments(path))!;
      const filenameSegments = filename.split(".");
      const ext = filenameSegments[filenameSegments.length - 1];

      if (ext == undefined) {
        type = IconType.UnknownFile;
      } else {
        suffix = `.${ext}`;
      }
      break;
    }
  }

  return `${type}-${suffix}`;
};

const pendingIcons = new Map<string, Promise<string | null>>();

const loadIcon = (cacheKey: string, type: IconType, path?: string): Promise<string | null> => {
  const pending = pendingIcons.get(cacheKey);

  if (pending) return pending;

  const request = BApi.file
    .getIconData({ type, path })
    .then((response) => response.data ?? null)
    // An unavailable native icon has the same fallback as an empty response.
    .catch(() => null)
    .finally(() => pendingIcons.delete(cacheKey));

  pendingIcons.set(cacheKey, request);

  return request;
};

const FileSystemEntryIcon = ({ path, type, size = 14, disableCache }: Props) => {
  const cacheKey = buildCacheKey(type, path);
  const iconCache = useIconsStore((state) =>
    cacheKey === undefined ? undefined : (state.icons[cacheKey] as string | null | undefined),
  );
  const add = useIconsStore((state) => state.add);
  const userSideActionsRunHere = useUserSideActionsRunHere();
  const isPureClient = useIsPureClient();
  // AppContext arrives asynchronously. Wait for it before assuming a local
  // connection has a desktop; a thin client supplies its own icon handler.
  const hasDesktopRuntime = useAppContextStore(
    (state) => state.bApi2 !== null && state.runtimeMode !== RuntimeMode.Docker,
  );
  const canLoadIcon = userSideActionsRunHere && (isPureClient || hasDesktopRuntime);
  const [loadedIcon, setLoadedIcon] = useState<{ cacheKey: string; data: string | null }>();
  const iconImgData =
    !disableCache && iconCache !== undefined
      ? iconCache
      : loadedIcon?.cacheKey === cacheKey
        ? loadedIcon?.data
        : undefined;

  useEffect(() => {
    if (!canLoadIcon || cacheKey === undefined || iconImgData !== undefined) return;

    let active = true;

    loadIcon(cacheKey, type, path).then((data) => {
      // Cache null too, so unavailable icons are not requested on every mount.
      if (!disableCache) add({ [cacheKey]: data });
      if (active) setLoadedIcon({ cacheKey, data });
    });

    return () => {
      active = false;
    };
  }, [add, cacheKey, canLoadIcon, disableCache, iconImgData, path, type]);

  return (
    <div
      className={"file-system-entry-icon"}
      style={{
        width: size,
        height: size,
        minWidth: size,
        minHeight: size,
      }}
    >
      {canLoadIcon && iconImgData ? (
        <img alt={""} src={iconImgData} />
      ) : type == IconType.Directory ? (
        <MdFolder
          style={{
            color: "#ccc",
            fontSize: size,
          }}
        />
      ) : (
        <MdInsertDriveFile
          style={{
            color: "#ccc",
            fontSize: size,
          }}
          title={i18n.t<string>("Unknown file type")}
        />
      )}
    </div>
  );
};

FileSystemEntryIcon.displayName = "FileSystemEntryIcon";

export default FileSystemEntryIcon;
