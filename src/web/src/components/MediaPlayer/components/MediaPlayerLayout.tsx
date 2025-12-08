"use client";

import React, { useCallback, useEffect, useRef, useState } from "react";

import ThumbnailPanel from "./ThumbnailPanel";
import MediaContent from "./MediaContent";
import { MediaType, IwFsType } from "@/sdk/constants";

import type { BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry } from "@/sdk/Api";
import type { MediaPlayerEntry } from "../types";

interface MediaPlayerLayoutProps {
  entries: MediaPlayerEntry[];
  playableEntries: MediaPlayerEntry[];
  activeIndex: number;
  activeEntry: MediaPlayerEntry;
  leftPanelCollapsed: boolean;
  playing: boolean;
  currentInitialized: boolean;
  autoPlay?: boolean;
  progress?: number;
  getMediaType: (entry: BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry) => MediaType;
  renderOperations?: (
    filePath: string,
    mediaType: MediaType,
    playing: boolean,
    reactPlayer: any,
    image: HTMLImageElement | null,
  ) => any;
  onEntryClick: (entry: BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry) => void;
  onToggleCollapse: () => void;
  onPrevEntry: () => void;
  onNextEntry: () => void;
  onLoad: () => void;
  onVideoReady?: (width: number, height: number) => void;
  onVideoPlay?: () => void;
  onVideoPause?: () => void;
  onVideoEnded?: () => void;
  onVideoSeek?: () => void;
  onVideoStart?: () => void;
  onVideoProgress?: (state: {
    played: number;
    playedSeconds: number;
    loaded: number;
    loadedSeconds: number;
  }) => void;
  onWheel?: (e: React.WheelEvent) => void;
}

const MediaPlayerLayout: React.FC<MediaPlayerLayoutProps> = ({
  entries,
  playableEntries,
  activeIndex,
  activeEntry,
  leftPanelCollapsed,
  playing,
  currentInitialized,
  autoPlay,
  progress,
  getMediaType,
  renderOperations,
  onEntryClick,
  onToggleCollapse,
  onPrevEntry,
  onNextEntry,
  onLoad,
  onVideoReady,
  onVideoPlay,
  onVideoPause,
  onVideoEnded,
  onVideoSeek,
  onVideoStart,
  onVideoProgress,
  onWheel,
}) => {
  const mediaType = getMediaType(activeEntry);

  return (
    <div
      className="w-full h-full bg-black/90 flex flex-row"
      tabIndex={-1}
      onWheel={onWheel}
    >
      <ThumbnailPanel
        activeIndex={activeIndex}
        collapsed={leftPanelCollapsed}
        entries={entries}
        getMediaType={getMediaType}
        playableEntries={playableEntries}
        onEntryClick={onEntryClick}
        onToggleCollapse={onToggleCollapse}
      />

      <MediaContent
        activeEntry={activeEntry}
        activeIndex={activeIndex}
        autoPlay={autoPlay}
        currentInitialized={currentInitialized}
        mediaType={mediaType}
        playableEntries={playableEntries}
        playing={playing}
        progress={progress}
        renderOperations={renderOperations}
        onLoad={onLoad}
        onNextEntry={onNextEntry}
        onPrevEntry={onPrevEntry}
        onVideoEnded={onVideoEnded}
        onVideoPause={onVideoPause}
        onVideoPlay={onVideoPlay}
        onVideoProgress={onVideoProgress}
        onVideoReady={onVideoReady}
        onVideoSeek={onVideoSeek}
        onVideoStart={onVideoStart}
      />
    </div>
  );
};

export default MediaPlayerLayout;
