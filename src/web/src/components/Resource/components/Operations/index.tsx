"use client";

import type { IResourceCoverRef } from "@/components/Resource/components/ResourceCover";
import type { Resource } from "@/core/models/Resource";

import { useTranslation } from "react-i18next";
import React from "react";
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
import ShowResourceMediaPlayer from "@/components/Resource/components/ShowResourceMediaPlayer";
import { EnhancementAdditionalItem, PlaylistItemType } from "@/sdk/constants";
import { PlaylistCollection } from "@/components/Playlist";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { Button, Popover, Modal } from "@/components/bakaui";

interface IProps {
  resource: Resource;
  coverRef?: IResourceCoverRef;
  reload?: (ct?: AbortSignal) => Promise<any>;
}

export default ({ resource, coverRef, reload }: IProps) => {
  const { t } = useTranslation();

  const { createPortal } = useBakabaseContext();
  // const searchEngines = store.useModelState('thirdPartyOptions').simpleSearchEngines || [];

  return (
    <Popover
      style={{
        zIndex: 20,
      }}
      trigger={
        <Button
          isIconOnly
          className={
            "absolute top-1 right-1 z-10 opacity-0 group-hover/resource:opacity-100"
          }
          size={"sm"}
        >
          <ProductOutlined className={"text-lg"} />
        </Button>
      }
    >
      <div className={"grid grid-cols-3 gap-1 py-1 rounded"}>
        <Button
          isIconOnly
          color={resource.pinned ? "warning" : "default"}
          size={"sm"}
          title={resource.pinned ? t<string>("Unpin") : t<string>("Pin")}
          onClick={() => {
            BApi.resource
              .pinResource(resource.id, { pin: !resource.pinned })
              .then((r) => {
                reload?.();
              });
          }}
        >
          <PushpinOutlined className={"text-lg"} />
        </Button>
        <Button
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
        </Button>
        <Button
          isIconOnly
          size={"sm"}
          title={t<string>("Enhancements")}
          onClick={() => {
            BApi.resource
              .getResourceEnhancements(resource.id, {
                additionalItem:
                  EnhancementAdditionalItem.GeneratedPropertyValue,
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
        </Button>
        <Button
          isIconOnly
          size={"sm"}
          title={t<string>("Preview")}
          onClick={() => {
            ShowResourceMediaPlayer(
              resource.id,
              resource.path,
              (base64String: string) => {
                coverRef?.save(base64String);
                // @ts-ignore
              },
              t,
              resource.isSingleFile,
            );
          }}
        >
          <AiOutlinePicture className={"text-lg"} />
        </Button>
        <Button
          isIconOnly
          size={"sm"}
          title={t<string>("Add to playlist")}
          onClick={() => {
            createPortal(Modal, {
              defaultVisible: true,
              title: t<string>("Add to playlist"),
              children: (
                <PlaylistCollection
                  defaultNewItem={{
                    resourceId: resource.id,
                    type: PlaylistItemType.Resource,
                  }}
                />
              ),
              style: { minWidth: 600 },
              closeMode: ["close", "mask", "esc"],
            });
          }}
        >
          <VideoCameraAddOutlined className={"text-lg"} />
        </Button>
      </div>
    </Popover>
  );
};
