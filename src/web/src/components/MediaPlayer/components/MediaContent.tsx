"use client";
import type { MediaPlayerEntry, MediaPreviewState } from "../types";
import type { MediaType } from "@/sdk/constants";

import React, { useRef } from "react";

import MediaRenderer, { type MediaRendererRef } from "./MediaRenderer";
import MediaFooter from "./MediaFooter";

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
  onPreviewStateChange?: (path: string, state: MediaPreviewState) => void;
  onOpenAlternative?: (path: string) => void | Promise<void>;
}
const MediaContent = (props: MediaContentProps) => {
  const renderer = useRef<MediaRendererRef>(null);
  const {
    activeEntry,
    activeIndex,
    playableEntries,
    mediaType,
    playing,
    currentInitialized,
    autoPlay,
    progress,
    renderOperations,
    onPrevEntry,
    onNextEntry,
    ...events
  } = props;

  return (
    <main className="media-player-main">
      <div className="media-player-stage">
        <MediaRenderer
          key={activeEntry.playPath || activeEntry.path}
          ref={renderer}
          currentInitialized={currentInitialized}
          entry={activeEntry}
          mediaType={mediaType}
          playing={playing}
          {...events}
        />
      </div>
      <MediaFooter
        activeIndex={activeIndex}
        autoPlay={autoPlay}
        currentInitialized={currentInitialized}
        entry={activeEntry}
        image={renderer.current?.getImageRef() || null}
        mediaType={mediaType}
        playing={playing}
        progress={progress}
        reactPlayer={renderer.current?.getPlayerRef()}
        renderOperations={renderOperations}
        totalEntries={playableEntries.length}
        onNextEntry={onNextEntry}
        onPrevEntry={onPrevEntry}
      />
    </main>
  );
};

export default MediaContent;
