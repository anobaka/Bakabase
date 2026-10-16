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
  renderFooterExtra?: (filePath: string) => React.ReactNode;
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
  renderFooterExtra,
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
      <nav aria-label={t("mediaPlayer.navigation")} className="media-player-navigation">
        <button
          aria-label={t("mediaPlayer.previous")}
          className="media-player-icon-button"
          disabled={activeIndex <= 0}
          title={t("mediaPlayer.previous")}
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
          aria-label={t("mediaPlayer.next")}
          className="media-player-icon-button"
          disabled={activeIndex >= totalEntries - 1}
          title={t("mediaPlayer.next")}
          onClick={onNextEntry}
        >
          <TbChevronRight size={20} />
        </button>
      </nav>
      {renderFooterExtra && (
        <div className="media-player-footer-extra">
          {renderFooterExtra(entry.playPath || entry.path)}
        </div>
      )}
    </footer>
  );
};

export default MediaFooter;
