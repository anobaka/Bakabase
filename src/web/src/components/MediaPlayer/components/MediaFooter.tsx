"use client";
import type { MediaPlayerEntry } from "../types";
import type { MediaType } from "@/sdk/constants";
import React from "react";
import { useTranslation } from "react-i18next";
import { TbChevronLeft, TbChevronRight } from "react-icons/tb";
import { mediaTypeKey } from "../media";

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
  onPrevEntry: () => void;
  onNextEntry: () => void;
}
const MediaFooter = ({
  entry,
  activeIndex,
  totalEntries,
  currentInitialized,
  mediaType,
  playing,
  renderOperations,
  reactPlayer,
  image,
  onPrevEntry,
  onNextEntry,
}: MediaFooterProps) => {
  const { t } = useTranslation();
  return (
    <footer className="media-player-footer">
      <div className="media-player-current">
        <span className="media-player-current-type">{t(mediaTypeKey(mediaType))}</span>
        <span title={entry.path}>{entry.name || entry.path}</span>
      </div>
      {renderOperations &&
        currentInitialized &&
        renderOperations(entry.playPath || entry.path, mediaType, playing, reactPlayer, image)}
      <nav className="media-player-navigation" aria-label={t("mediaPlayer.navigation")}>
        <button
          className="media-player-icon-button"
          aria-label={t("mediaPlayer.previous")}
          title={t("mediaPlayer.previous")}
          disabled={activeIndex <= 0}
          onClick={onPrevEntry}
        >
          <TbChevronLeft size={20} />
        </button>
        <span>
          {activeIndex + 1}
          <span className="media-player-count-divider">/</span>
          {totalEntries}
        </span>
        <button
          className="media-player-icon-button"
          aria-label={t("mediaPlayer.next")}
          title={t("mediaPlayer.next")}
          disabled={activeIndex >= totalEntries - 1}
          onClick={onNextEntry}
        >
          <TbChevronRight size={20} />
        </button>
      </nav>
    </footer>
  );
};
export default MediaFooter;
