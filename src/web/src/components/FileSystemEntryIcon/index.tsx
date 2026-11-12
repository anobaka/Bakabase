"use client";

import React, { useEffect, useState } from "react";
import i18n from "i18next";
import _ from "lodash";
import { MdFolder, MdInsertDriveFile, MdImage, MdAudiotrack, MdMovie, MdDescription, MdFolderZip } from "react-icons/md";

import "./index.scss";

import { ExtensionMediaTypes, MediaType, IconType, RuntimeMode } from "@/sdk/constants";
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
  /** Use the filename and folder metadata alone, including for historical files. */
  metadataOnly?: boolean;
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

const FileSystemEntryIcon = ({ path, type, size = 14, disableCache, metadataOnly }: Props) => {
  const cacheKey = buildCacheKey(type, path);
  const iconCache = useIconsStore((state) =>
    cacheKey === undefined ? undefined : (state.icons[cacheKey] as string | null | undefined),
  );
  const add = useIconsStore((state) => state.add);
  const userSideActionsRunHere = useUserSideActionsRunHere();
  const isPureClient = useIsPureClient();
  // AppContext arrives asynchronously. Wait for it before assuming a local
  // connection has a desktop; a managed server's relay supplies its own icon handler.
  const hasDesktopRuntime = useAppContextStore(
    (state) => state.bApi2 !== null && state.runtimeMode !== RuntimeMode.Docker,
  );
  const canLoadIcon = !metadataOnly && userSideActionsRunHere && (isPureClient || hasDesktopRuntime);
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

  const suffix = path?.split(/[\\/]/).at(-1)?.match(/\.([^.]+)$/)?.[1].toLowerCase();
  const mediaType = suffix ? ExtensionMediaTypes["." + suffix] : undefined;
  const fallback = mediaType === MediaType.Image ? MdImage : mediaType === MediaType.Video ? MdMovie :
    mediaType === MediaType.Audio ? MdAudiotrack : mediaType === MediaType.Text ? MdDescription :
    suffix && ["zip", "7z", "rar", "gz", "tar", "bz2", "xz"].includes(suffix) ? MdFolderZip : MdInsertDriveFile;
  const FileIcon = metadataOnly ? fallback : MdInsertDriveFile;
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
        <FileIcon
          style={{
            color: "#ccc",
            fontSize: size,
          }}
          title={metadataOnly && suffix ? suffix.toUpperCase() : i18n.t<string>("Unknown file type")}
        />
      )}
    </div>
  );
};

FileSystemEntryIcon.displayName = "FileSystemEntryIcon";

export default FileSystemEntryIcon;
