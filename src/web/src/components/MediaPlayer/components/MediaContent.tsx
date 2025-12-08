"use client";

import React, { useRef } from "react";

import MediaRenderer, { type MediaRendererRef } from "./MediaRenderer";
import MediaFooter from "./MediaFooter";
import { IwFsType, MediaType } from "@/sdk/constants";

import type { MediaPlayerEntry } from "../types";

interface MediaContentProps {
  activeEntry: MediaPlayerEntry;
  activeIndex: number;
  playableEntries: MediaPlayerEntry[];
  mediaType: MediaType;
  playing: boolean;
  currentInitialized: boolean;
  autoPlay?: boolean;
  progress?: number;
  renderOperations?: (
    filePath: string,
    mediaType: MediaType,
    playing: boolean,
    reactPlayer: any,
    image: HTMLImageElement | null,
  ) => any;
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
  onPrevEntry: () => void;
  onNextEntry: () => void;
}

const MediaContent: React.FC<MediaContentProps> = ({
  activeEntry,
  activeIndex,
  playableEntries,
  mediaType,
  playing,
  currentInitialized,
  autoPlay,
  progress,
  renderOperations,
  onLoad,
  onVideoReady,
  onVideoPlay,
  onVideoPause,
  onVideoEnded,
  onVideoSeek,
  onVideoStart,
  onVideoProgress,
  onPrevEntry,
  onNextEntry,
}) => {
  const mediaRendererRef = useRef<MediaRendererRef>(null);
  const mediaContainerRef = useRef<HTMLDivElement | null>(null);

  const handlePrevClick = (e: React.MouseEvent) => {
    e.stopPropagation();
    // Find previous playable entry
    let prevIndex = activeIndex - 1;

    while (prevIndex >= 0) {
      const entry = playableEntries[prevIndex];

      if (
        entry &&
        entry.type !== IwFsType.Directory &&
        entry.type !== IwFsType.CompressedFileEntry
      ) {
        onPrevEntry();
        break;
      }
      prevIndex--;
    }
  };

  const handleNextClick = (e: React.MouseEvent) => {
    e.stopPropagation();
    // Find next playable entry
    let nextIndex = activeIndex + 1;

    while (nextIndex < playableEntries.length) {
      const entry = playableEntries[nextIndex];

      if (
        entry &&
        entry.type !== IwFsType.Directory &&
        entry.type !== IwFsType.CompressedFileEntry
      ) {
        onNextEntry();
        break;
      }
      nextIndex++;
    }
  };

  return (
    <div className="flex-1 flex flex-col min-w-0 relative">
      <div
        ref={mediaContainerRef}
        className="flex-1 flex items-center justify-center relative min-h-0 overflow-y-auto overflow-x-hidden media-container"
        tabIndex={0}
      >
        {/* Left clickable area for previous */}
        {activeIndex > 0 && (
          <div
            className="absolute left-0 top-0 bottom-0 w-1/3 z-[1] cursor-pointer"
            onClick={handlePrevClick}
          />
        )}
        {/* Right clickable area for next */}
        {activeIndex < playableEntries.length - 1 && (
          <div
            className="absolute right-0 top-0 bottom-0 w-1/3 z-[1] cursor-pointer"
            onClick={handleNextClick}
          />
        )}
        <MediaRenderer
          ref={mediaRendererRef}
          currentInitialized={currentInitialized}
          entry={activeEntry}
          mediaType={mediaType}
          playing={playing}
          onLoad={onLoad}
          onVideoEnded={onVideoEnded}
          onVideoPause={onVideoPause}
          onVideoPlay={onVideoPlay}
          onVideoProgress={onVideoProgress}
          onVideoReady={onVideoReady}
          onVideoSeek={onVideoSeek}
          onVideoStart={onVideoStart}
        />
      </div>
      {activeEntry && (
        <MediaFooter
          activeIndex={activeIndex}
          autoPlay={autoPlay}
          currentInitialized={currentInitialized}
          entry={activeEntry}
          image={mediaRendererRef.current?.getImageRef() || null}
          mediaType={mediaType}
          playing={playing}
          progress={progress}
          reactPlayer={mediaRendererRef.current?.getPlayerRef()}
          renderOperations={renderOperations}
          totalEntries={playableEntries.length}
        />
      )}
    </div>
  );
};

export default MediaContent;
