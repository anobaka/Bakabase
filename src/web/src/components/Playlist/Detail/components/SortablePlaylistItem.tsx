"use client";

import { SortableElement } from "react-sortable-hoc";
import { Button } from "@/components/bakaui";
import React from "react";

import { MdVideoLibrary, MdVideoFile, MdImage, MdAudiotrack, MdDragIndicator } from "react-icons/md";
import { PlaylistItemType } from "@/sdk/constants";
import DragHandle from "@/components/DragHandle";
import { MdDelete } from "react-icons/md";
import BApi from "@/sdk/BApi";

const ItemTypeIcon = {
  [PlaylistItemType.Resource]: MdVideoLibrary,
  [PlaylistItemType.Video]: MdVideoFile,
  [PlaylistItemType.Image]: MdImage,
  [PlaylistItemType.Audio]: MdAudiotrack,
};

const renderDuration = (item) => {
  if (
    item.type == PlaylistItemType.Audio ||
    item.type == PlaylistItemType.Video
  ) {
    let sb = item.startTime ?? "";

    if (item.endTime) {
      sb += ` ~ ${item.endTime}`;
    }

    return sb;
  }
};

export default SortableElement(({ item, resource, onRemove }) => {
  let displayFilename = item.file;

  if (resource && displayFilename) {
    if (resource.isSingleFile) {
      displayFilename = item.file
        ?.replace(resource?.path, "")
        .trim("/")
        .trim("\\");
    } else {
      const segments = item.file?.replaceAll("\\", "/").split("/");

      console.log(segments);
      displayFilename = segments[segments.length - 1];
    }
  }
  // console.log(resource, displayFilename);

  return (
    <div className={"sortable-playlist-item"}>
      <div className="resource-name">
        <DragHandle />
        <div className="type">
          {React.createElement(ItemTypeIcon[item.type], { size: "small" })}
        </div>
        <div className="name">
          <Button
            text
            type={"primary"}
            onClick={() => {
              if (resource) {
                BApi.tool.openFileOrDirectory({
                  path: item.file ?? resource.rawFullname,
                  openInDirectory: item.file ? true : resource.isSingleFile,
                });
              }
            }}
          >
            {resource?.displayName}
          </Button>
        </div>
      </div>
      <div className="file">{displayFilename}</div>

      <div className="duration">{renderDuration(item)}</div>
      <div className="opt">
        <Button
          isIconOnly
          color={"danger"}
          size={"sm"}
          variant={"light"}
          onPress={() => {
            onRemove(item);
          }}
        >
          <MdDelete className={"text-base"} />
        </Button>
      </div>
    </div>
  );
});
