"use client";

import React, { useEffect, useRef, useState } from "react";
import i18n from "i18next";
import { MdFolder, MdInsertDriveFile } from "react-icons/md";

import "./index.scss";
import _ from "lodash";

import { IconType } from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import { splitPathIntoSegments } from "@/components/utils";
import { useIconsStore } from "@/stores/icons";
import { useUpdate } from "react-use";

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
const FileSystemEntryIcon = ({
  path,
  type,
  size = 14,
  disableCache,
}: Props) => {
  const cacheKey = buildCacheKey(type, path);
  const { icons, add } = useIconsStore();
  const iconCache = cacheKey == undefined ? undefined : icons[cacheKey] as string;
  const iconImgDataRef = useRef<string | undefined>(disableCache ? undefined : (iconCache === undefined ? undefined : iconCache));
  const forceUpdate = useUpdate();

  // console.log(path, type, size, iconImgData);

  useEffect(() => {
    // console.log('cacheKey', cacheKey);
    // console.log('iconImgDataRef.current', iconImgDataRef.current);
    // console.log('disableCache', disableCache);
    // console.log('icons', icons);
    if (iconImgDataRef.current === undefined && cacheKey != undefined) {
      BApi.file
        .getIconData({
          type,
          path,
        })
        .then((r) => {
          add({ [cacheKey]: r.data });
          // console.log('add', { [cacheKey]: r.data });
          iconImgDataRef.current = (r.data);
          forceUpdate();
        });
    }
  }, []);

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
      {/* {type == 'directory' ? ( */}
      {/*   <svg aria-hidden="true"> */}
      {/*     <use xlinkHref="#icon-folder1" /> */}
      {/*   </svg> */}
      {/* ) : */}
      {iconImgDataRef.current ? (
        <img alt={""} src={iconImgDataRef.current} />
      ) : (
        type == IconType.Directory ? (
          <MdFolder style={{
            color: "#ccc",
            fontSize: size,
          }} />
        ) :
          (<MdInsertDriveFile
            style={{
              color: "#ccc",
              fontSize: size,
            }}
            title={i18n.t<string>("Unknown file type")}
          />
          ))}
    </div>
  );
};

FileSystemEntryIcon.displayName = "FileSystemEntryIcon";

export default FileSystemEntryIcon;
