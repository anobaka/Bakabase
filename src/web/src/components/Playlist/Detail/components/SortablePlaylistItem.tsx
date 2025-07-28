"use client";

import type { components } from "@/sdk/BApi2";
import type { ReactNode } from "react";

import { SortableElement } from "react-sortable-hoc";
import React, { useCallback } from "react";
import {
  MdVideoLibrary,
  MdVideoFile,
  MdImage,
  MdAudiotrack,
} from "react-icons/md";
import { AiOutlineDelete } from "react-icons/ai";
import { useTranslation } from "react-i18next";

import { Button } from "@/components/bakaui";
import { PlaylistItemType } from "@/sdk/constants";
import DragHandle from "@/components/DragHandle";
import BApi from "@/sdk/BApi";

type PlayListItem =
  components["schemas"]["Bakabase.InsideWorld.Business.Components.PlayList.Models.Domain.PlayListItem"];
type Resource =
  components["schemas"]["Bakabase.Abstractions.Models.Domain.Resource"];

interface SortablePlaylistItemProps {
  item: PlayListItem;
  resource?: Resource;
  onRemove: (item: PlayListItem) => void;
}

const ItemTypeIcon = {
  [PlaylistItemType.Resource]: MdVideoLibrary,
  [PlaylistItemType.Video]: MdVideoFile,
  [PlaylistItemType.Image]: MdImage,
  [PlaylistItemType.Audio]: MdAudiotrack,
};

const renderDuration = (item: PlayListItem): string => {
  if (
    item.type === PlaylistItemType.Audio ||
    item.type === PlaylistItemType.Video
  ) {
    let duration = item.startTime ?? "";

    if (item.endTime) {
      duration += ` ~ ${item.endTime}`;
    }

    return duration;
  }

  return "";
};

export default SortableElement<SortablePlaylistItemProps>(
  ({ item, resource, onRemove }: SortablePlaylistItemProps) => {
    const { t } = useTranslation();
    const renderName = useCallback((): ReactNode => {
      switch (item.type as PlaylistItemType) {
        case PlaylistItemType.Resource:
          return (
            <Button size={"sm"} variant={"light"}>
              {resource?.displayName ?? t("Unknown Resource")}
            </Button>
          );
        case PlaylistItemType.Video:
        case PlaylistItemType.Image:
        case PlaylistItemType.Audio:
          return item.file;
      }
    }, [item.file, resource]);

    const handleOpenFile = useCallback(() => {
      if (resource) {
        BApi.tool.openFileOrDirectory({
          path: item.file ?? resource.path,
          openInDirectory: item.file ? true : resource.isFile,
        });
      }
    }, [item.file, resource]);

    const handleRemove = useCallback(() => {
      onRemove(item);
    }, [item, onRemove]);

    const displayFilename = getDisplayFilename();

    return (
      <div className="flex items-center gap-1">
        <DragHandle />
        <div className="flex items-center text-gray-400">
          {React.createElement(ItemTypeIcon[item.type], { size: "small" })}
        </div>
        <div className="flex items-center">
          <Button
            color={"primary"}
            disabled={!resource}
            variant={"light"}
            onPress={handleOpenFile}
          >
            {resource?.displayName || "Unknown Resource"}
          </Button>
        </div>
        {renderName()}
        <div className="flex items-center text-sm text-gray-500">
          {renderDuration(item)}
        </div>
        <div className="flex items-center">
          <Button
            isIconOnly
            color={"danger"}
            size={"sm"}
            variant={"light"}
            onPress={handleRemove}
          >
            <AiOutlineDelete className={"text-lg"} />
          </Button>
        </div>
      </div>
    );
  },
);
