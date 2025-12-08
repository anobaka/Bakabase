"use client";

import type { IResourceCoverRef } from "@/components/Resource/components/ResourceCover";
import type { Resource } from "@/core/models/Resource";
import type { BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry } from "@/sdk/Api";
import type { MediaType } from "@/sdk/constants";

import { useTranslation } from "react-i18next";
import React, { useMemo } from "react";
import {
  FireOutlined,
  FolderOpenOutlined,
  ProductOutlined,
  PushpinOutlined,
  VideoCameraAddOutlined,
} from "@ant-design/icons";
import { AiOutlinePicture } from "react-icons/ai";

import BApi from "@/sdk/BApi";
import ResourceEnhancementsDialog from "@/components/Resource/components/ResourceEnhancementsDialog";
import { EnhancementAdditionalItem, IwFsType } from "@/sdk/constants";
import { PlaylistCollection } from "@/components/Playlist";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { Button, Popover, Modal, toast } from "@/components/bakaui";
import { useUiOptionsStore } from "@/stores/options";
import MediaPlayer from "@/components/MediaPlayer";

interface IProps {
  resource: Resource;
  coverRef?: IResourceCoverRef;
  reload?: (ct?: AbortSignal) => Promise<any>;
}

const Operations = ({ resource, coverRef, reload }: IProps) => {
  const { t } = useTranslation();
  const { createPortal, createWindow } = useBakabaseContext();
  const uiOptions = useUiOptionsStore((state) => state.data);

  // Get displayOperations from options, default to ["aggregate"] for new users
  const displayOperations = useMemo(() => {
    const ops = uiOptions?.resource?.displayOperations;

    // If null/undefined or empty, default to aggregate button
    if (!ops || ops.length === 0) {
      return ["aggregate"];
    }

    return ops;
  }, [uiOptions?.resource?.displayOperations]);

  const showResourceMediaPlayer = () => {
    BApi.file
      .getAllFiles({
        path: resource.path,
      })
      .then((a) => {
        if (!a.code && a.data) {
          if (a.data.length == 0) {
            return toast.default(t<string>("No files to preview"));
          }
          const files = a.data;

          const entries: BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry[] = files.map(
            (path) => {
              const name = path.split(/[/\\]/).pop() || path;
              const ext = name.includes(".") ? name.split(".").pop() : undefined;

              return {
                path: path,
                name: name,
                meaningfulName: name,
                ext: ext,
                type: IwFsType.Unknown,
                passwordsForDecompressing: [],
              };
            },
          );

          createWindow(
            MediaPlayer,
            {
              entries: entries,
              defaultActiveIndex: 0,
              renderOperations: (
                filePath: string,
                mediaType: MediaType,
                playing: boolean,
                reactPlayer: any,
                image: HTMLImageElement | null,
              ): any => {},
            },
            {
              title: resource.displayName,
              persistent: true,
            },
          );
        }
      });
  };

  const showAggregate = displayOperations.includes("aggregate");
  const showPin = displayOperations.includes("pin");
  const showOpenFolder = displayOperations.includes("openFolder");
  const showEnhancements = displayOperations.includes("enhancements");
  const showPreview = displayOperations.includes("preview");
  const showAddToPlaylist = displayOperations.includes("addToPlaylist");

  // If aggregate is selected, show popover with all buttons (filtered by what's selected)
  // Otherwise, show individual buttons for selected operations
  if (showAggregate) {
    const buttons = [];

    if (showPin) {
      buttons.push(
        <Button
          key="pin"
          isIconOnly
          color={resource.pinned ? "warning" : "default"}
          size={"sm"}
          title={resource.pinned ? t<string>("Unpin") : t<string>("Pin")}
          onClick={() => {
            BApi.resource.pinResource(resource.id, { pin: !resource.pinned }).then((r) => {
              reload?.();
            });
          }}
        >
          <PushpinOutlined className={"text-lg"} />
        </Button>,
      );
    }
    if (showOpenFolder) {
      buttons.push(
        <Button
          key="openFolder"
          isIconOnly
          size={"sm"}
          title={t<string>("Open folder")}
          onClick={() =>
            BApi.tool.openFileOrDirectory({
              path: resource.path,
              openInDirectory: resource.isFile,
            })
          }
        >
          <FolderOpenOutlined className={"text-lg"} />
        </Button>,
      );
    }
    if (showEnhancements) {
      buttons.push(
        <Button
          key="enhancements"
          isIconOnly
          size={"sm"}
          title={t<string>("Enhancements")}
          onClick={() => {
            BApi.resource
              .getResourceEnhancements(resource.id, {
                additionalItem: EnhancementAdditionalItem.GeneratedPropertyValue,
              })
              .then((t) => {
                createPortal(ResourceEnhancementsDialog, {
                  resourceId: resource.id,
                  // @ts-ignore
                  enhancements: t.data || [],
                });
              });
          }}
        >
          <FireOutlined className={"text-lg"} />
        </Button>,
      );
    }
    if (showPreview) {
      buttons.push(
        <Button
          key="preview"
          isIconOnly
          size={"sm"}
          title={t<string>("Preview")}
          onClick={showResourceMediaPlayer}
        >
          <AiOutlinePicture className={"text-lg"} />
        </Button>,
      );
    }
    if (showAddToPlaylist) {
      buttons.push(
        <Button
          key="addToPlaylist"
          isIconOnly
          size={"sm"}
          title={t<string>("Add to playlist")}
          onClick={() => {
            createPortal(Modal, {
              defaultVisible: true,
              title: t<string>("Add to playlist"),
              children: <PlaylistCollection addingResourceId={resource.id} />,
              style: { minWidth: 600 },
              footer: {
                actions: ["cancel"],
              },
            });
          }}
        >
          <VideoCameraAddOutlined className={"text-lg"} />
        </Button>,
      );
    }

    // If no individual operations selected, show all buttons (default behavior)
    if (buttons.length === 0) {
      buttons.push(
        <Button
          key="pin"
          isIconOnly
          color={resource.pinned ? "warning" : "default"}
          size={"sm"}
          title={resource.pinned ? t<string>("Unpin") : t<string>("Pin")}
          onClick={() => {
            BApi.resource.pinResource(resource.id, { pin: !resource.pinned }).then((r) => {
              reload?.();
            });
          }}
        >
          <PushpinOutlined className={"text-lg"} />
        </Button>,
        <Button
          key="openFolder"
          isIconOnly
          size={"sm"}
          title={t<string>("Open folder")}
          onClick={() =>
            BApi.tool.openFileOrDirectory({
              path: resource.path,
              openInDirectory: resource.isFile,
            })
          }
        >
          <FolderOpenOutlined className={"text-lg"} />
        </Button>,
        <Button
          key="enhancements"
          isIconOnly
          size={"sm"}
          title={t<string>("Enhancements")}
          onClick={() => {
            BApi.resource
              .getResourceEnhancements(resource.id, {
                additionalItem: EnhancementAdditionalItem.GeneratedPropertyValue,
              })
              .then((t) => {
                createPortal(ResourceEnhancementsDialog, {
                  resourceId: resource.id,
                  // @ts-ignore
                  enhancements: t.data || [],
                });
              });
          }}
        >
          <FireOutlined className={"text-lg"} />
        </Button>,
        <Button
          key="preview"
          isIconOnly
          size={"sm"}
          title={t<string>("Preview")}
          onClick={showResourceMediaPlayer}
        >
          <AiOutlinePicture className={"text-lg"} />
        </Button>,
        <Button
          key="addToPlaylist"
          isIconOnly
          size={"sm"}
          title={t<string>("Add to playlist")}
          onClick={() => {
            createPortal(Modal, {
              defaultVisible: true,
              title: t<string>("Add to playlist"),
              children: <PlaylistCollection addingResourceId={resource.id} />,
              style: { minWidth: 600 },
              footer: {
                actions: ["cancel"],
              },
            });
          }}
        >
          <VideoCameraAddOutlined className={"text-lg"} />
        </Button>,
      );
    }

    return (
      <Popover
        style={{
          zIndex: 20,
        }}
        trigger={
          <Button
            isIconOnly
            className={"absolute top-1 right-1 z-10 opacity-0 group-hover/resource:opacity-100"}
            size={"sm"}
          >
            <ProductOutlined className={"text-lg"} />
          </Button>
        }
      >
        <div className={"grid grid-cols-3 gap-1 py-1 rounded"}>{buttons}</div>
      </Popover>
    );
  }

  // Show individual buttons for selected operations
  const individualButtons = [];
  let buttonIndex = 0;

  if (showPin) {
    individualButtons.push(
      <Button
        key="pin"
        isIconOnly
        className={"absolute top-1 right-1 z-10 opacity-0 group-hover/resource:opacity-100"}
        color={resource.pinned ? "warning" : "default"}
        size={"sm"}
        style={{ right: `${buttonIndex * 2.5}rem` }}
        title={resource.pinned ? t<string>("Unpin") : t<string>("Pin")}
        onClick={() => {
          BApi.resource.pinResource(resource.id, { pin: !resource.pinned }).then((r) => {
            reload?.();
          });
        }}
      >
        <PushpinOutlined className={"text-lg"} />
      </Button>,
    );
    buttonIndex++;
  }
  if (showOpenFolder) {
    individualButtons.push(
      <Button
        key="openFolder"
        isIconOnly
        className={"absolute top-1 right-1 z-10 opacity-0 group-hover/resource:opacity-100"}
        size={"sm"}
        style={{ right: `${buttonIndex * 2.5}rem` }}
        title={t<string>("Open folder")}
        onClick={() =>
          BApi.tool.openFileOrDirectory({
            path: resource.path,
            openInDirectory: resource.isFile,
          })
        }
      >
        <FolderOpenOutlined className={"text-lg"} />
      </Button>,
    );
    buttonIndex++;
  }
  if (showEnhancements) {
    individualButtons.push(
      <Button
        key="enhancements"
        isIconOnly
        className={"absolute top-1 right-1 z-10 opacity-0 group-hover/resource:opacity-100"}
        size={"sm"}
        style={{ right: `${buttonIndex * 2.5}rem` }}
        title={t<string>("Enhancements")}
        onClick={() => {
          BApi.resource
            .getResourceEnhancements(resource.id, {
              additionalItem: EnhancementAdditionalItem.GeneratedPropertyValue,
            })
            .then((t) => {
              createPortal(ResourceEnhancementsDialog, {
                resourceId: resource.id,
                // @ts-ignore
                enhancements: t.data || [],
              });
            });
        }}
      >
        <FireOutlined className={"text-lg"} />
      </Button>,
    );
    buttonIndex++;
  }
  if (showPreview) {
    individualButtons.push(
      <Button
        key="preview"
        isIconOnly
        className={"absolute top-1 right-1 z-10 opacity-0 group-hover/resource:opacity-100"}
        size={"sm"}
        style={{ right: `${buttonIndex * 2.5}rem` }}
        title={t<string>("Preview")}
        onClick={showResourceMediaPlayer}
      >
        <AiOutlinePicture className={"text-lg"} />
      </Button>,
    );
    buttonIndex++;
  }
  if (showAddToPlaylist) {
    individualButtons.push(
      <Button
        key="addToPlaylist"
        isIconOnly
        className={"absolute top-1 right-1 z-10 opacity-0 group-hover/resource:opacity-100"}
        size={"sm"}
        style={{ right: `${buttonIndex * 2.5}rem` }}
        title={t<string>("Add to playlist")}
        onClick={() => {
          createPortal(Modal, {
            defaultVisible: true,
            title: t<string>("Add to playlist"),
            children: <PlaylistCollection addingResourceId={resource.id} />,
            style: { minWidth: 600 },
            footer: {
              actions: ["cancel"],
            },
          });
        }}
      >
        <VideoCameraAddOutlined className={"text-lg"} />
      </Button>,
    );
  }

  return <>{individualButtons}</>;
};

Operations.displayName = "Operations";

export default Operations;
