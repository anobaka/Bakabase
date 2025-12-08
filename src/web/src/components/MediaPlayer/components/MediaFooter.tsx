"use client";

import React from "react";
import { MdInventory } from "react-icons/md";

import FileSystemEntryIcon from "@/components/FileSystemEntryIcon";
import { IconType, MediaType } from "@/sdk/constants";
import { Progress } from "@/components/bakaui";

import type { MediaPlayerEntry } from "../types";

interface MediaFooterProps {
  entry: MediaPlayerEntry;
  activeIndex: number;
  totalEntries: number;
  currentInitialized: boolean;
  autoPlay?: boolean;
  progress?: number;
  mediaType: MediaType;
  playing: boolean;
  renderOperations?: (
    filePath: string,
    mediaType: MediaType,
    playing: boolean,
    reactPlayer: any,
    image: HTMLImageElement | null,
  ) => any;
  reactPlayer?: any;
  image?: HTMLImageElement | null;
}

const MediaFooter: React.FC<MediaFooterProps> = ({
  entry,
  activeIndex,
  totalEntries,
  currentInitialized,
  autoPlay,
  progress,
  mediaType,
  playing,
  renderOperations,
  reactPlayer,
  image,
}) => {
  return (
    <>
      <div className="h-[40px] min-h-[40px] text-center flex gap-2.5 items-center justify-center text-white/90 bg-black/50 border-t border-white/10 px-5">
        <div className="flex items-center gap-1.5">
          <div className="max-w-4 max-h-4">
            <FileSystemEntryIcon path={entry.path} size={16} type={IconType.Dynamic} />
          </div>
          <div className="flex items-center flex-wrap gap-0.5">
            {entry.name}
          </div>
        </div>
        <span>
          ({activeIndex + 1} / {totalEntries})
        </span>
        {renderOperations &&
          currentInitialized &&
          renderOperations(
            entry.playPath || entry.path,
            mediaType,
            playing,
            reactPlayer,
            image,
          )}
      </div>
      {autoPlay && progress && (
        <Progress {...({ percent: progress, size: "sm", textRender: () => "" } as any)} />
      )}
    </>
  );
};

export default MediaFooter;
