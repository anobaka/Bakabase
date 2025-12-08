"use client";

import type { components } from "@/sdk/BApi2";
import type { ReactNode } from "react";

import { SortableElement, SortableElementProps, SortableHandle } from "react-sortable-hoc";
import React, { useCallback } from "react";
import {
  MdVideoLibrary,
  MdVideoFile,
  MdImage,
  MdAudiotrack,
} from "react-icons/md";
import { AiOutlineDelete, AiOutlineFolderOpen } from "react-icons/ai";
import { useTranslation } from "react-i18next";

import { Button } from "@/components/bakaui";
import { PlaylistItemType } from "@/sdk/constants";
import DragHandle from "@/components/DragHandle";
import BApi from "@/sdk/BApi";
import { GrResources } from "react-icons/gr";
import { IoFolderOpen } from "react-icons/io5";

type PlayListItem =
  components["schemas"]["Bakabase.InsideWorld.Business.Components.PlayList.Models.Domain.PlayListItem"];
type Resource =
  components["schemas"]["Bakabase.Abstractions.Models.Domain.Resource"];

interface SortablePlaylistItemProps extends SortableElementProps {
  item: PlayListItem;
  resource?: Resource;
  onRemove: (item: PlayListItem) => void;
}

const ItemTypeIcon = {
  [PlaylistItemType.Resource]: GrResources,
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

const SortableDragHandle = SortableHandle(() => <DragHandle />);

export default SortableElement<SortablePlaylistItemProps>(
  ({ item, resource, onRemove }: SortablePlaylistItemProps) => {
    const { t } = useTranslation();
    const renderResourceName = useCallback((): ReactNode => {
      if (resource?.displayName) {
        return resource.displayName;
      }
      return t("Unknown Resource");
    }, [resource, t]);

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

    return (
      <div 
        className="grid grid-cols-[auto_auto_1fr_auto_auto] gap-2 items-center px-2 border-b"
        style={{
          borderColor: 'var(--theme-border-color)',
        }}
      >
        <div className="flex items-center justify-center w-8">
          <SortableDragHandle />
        </div>
        <div className="flex items-center justify-center text-gray-400 w-6">
          {React.createElement(ItemTypeIcon[item.type], { className: "text-lg" })}
        </div>
        <div className="flex items-center min-w-0 gap-2 truncate">
          <span className="text-sm">{renderResourceName()}</span>
          <Button
            // color={"primary"}
            disabled={!resource && item.type === PlaylistItemType.Resource}
            variant={"light"}
            size="sm"
            onPress={handleOpenFile}
            isIconOnly
          >
            <AiOutlineFolderOpen className="text-lg" />
          </Button>
          {item.file && (
            <span className="text-sm text-gray-600 truncate">
              {item.file}
            </span>
          )}
        </div>
        <div className="flex items-center justify-end text-sm text-gray-500 min-w-[120px]">
          {renderDuration(item)}
        </div>
        <div className="flex items-center justify-center w-10">
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
