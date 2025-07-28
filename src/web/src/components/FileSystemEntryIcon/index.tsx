"use client";

import React, { useEffect, useState } from "react";
import i18n from "i18next";
import { MdInsertDriveFile } from "react-icons/md";

import "./index.scss";
import _ from "lodash";

import { IconType } from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import { splitPathIntoSegments } from "@/components/utils";

type Props = {
  type: IconType;
  path?: string;
  size: number | string;
  disableCache?: boolean;
};

const buildCacheKey = (type: IconType, path?: string): string => {
  let suffix = "";

  switch (type) {
    case IconType.UnknownFile:
    case IconType.Directory:
      break;
    case IconType.Dynamic: {
      if (!path) {
        throw new Error("Path is required for dynamic icon");
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

  const [icons, iconsDispatchers] = useState<{ [key: string]: string }>({});
  const [iconImgData, setIconImgData] = useState(
    disableCache ? undefined : icons[cacheKey],
  );

  // console.log(path, type, size, iconImgData);

  useEffect(() => {
    if (!iconImgData) {
      BApi.file
        .getIconData({
          type,
          path,
        })
        .then((r) => {
          iconsDispatchers({ ...icons, [cacheKey]: r.data });
          setIconImgData(r.data);
        });
    }
    // console.log(ext, icons);
    // if (!iconImgData && !type) {
    //   if (ext in icons) {
    //     setIconImgData(icons[ext]);
    //   } else {
    //     const isCompressedFileContent = path!.includes('!');
    //     if (!isCompressedFileContent) {
    //       GetIconData({
    //         path,
    //       })
    //         .invoke((t) => {
    //           if (!t.code) {
    //             iconsDispatchers.add({ [ext]: t.data });
    //             setIconImgData(t.data);
    //           }
    //         });
    //     }
    //   }
    // }
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
      {iconImgData ? (
        <img alt={""} src={iconImgData} />
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
