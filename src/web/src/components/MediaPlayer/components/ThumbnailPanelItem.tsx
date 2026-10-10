"use client";
import type { BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry } from "@/sdk/Api";
import type { MediaPlayerEntry } from "../types";
import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { TbFileText, TbMovie, TbMusic, TbPhoto, TbFile } from "react-icons/tb";
import { MediaType } from "@/sdk/constants";
import { mediaTypeKey } from "../media";

export interface ThumbnailPanelItemProps {
  entry: MediaPlayerEntry;
  index: number;
  activeIndex: number;
  isActive: boolean;
  getThumbnailUrl: (entry: MediaPlayerEntry) => string | null;
  getMediaType: (entry: BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry) => MediaType;
  onEntryClick: (entry: BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry) => void;
  activeThumbnailRef: React.RefObject<HTMLDivElement> | null;
}
const ThumbnailPanelItem = ({
  entry,
  isActive,
  getThumbnailUrl,
  getMediaType,
  onEntryClick,
}: ThumbnailPanelItemProps) => {
  const { t } = useTranslation();
  const [failed, setFailed] = useState(false);
  useEffect(() => setFailed(false), [entry.path]);
  const type = getMediaType(entry);
  const thumbnail = getThumbnailUrl(entry);
  const Icon =
    type === MediaType.Video
      ? TbMovie
      : type === MediaType.Audio
        ? TbMusic
        : type === MediaType.Image
          ? TbPhoto
          : type === MediaType.Text
            ? TbFileText
            : TbFile;
  return (
    <button
      className={`media-player-file ${isActive ? "is-active" : ""}`}
      role="option"
      aria-selected={isActive}
      title={entry.path}
      onClick={() => onEntryClick(entry)}
    >
      <span className={`media-player-file-icon media-type-${type}`}>
        {thumbnail && !failed ? (
          <img src={thumbnail} alt="" loading="lazy" onError={() => setFailed(true)} />
        ) : (
          <Icon size={22} />
        )}
      </span>
      <span className="media-player-file-description">
        <span className="media-player-file-name">{entry.name || entry.path}</span>
        <span className="media-player-file-kind">
          {t(mediaTypeKey(type))}
          {entry.ext ? ` · ${entry.ext.toUpperCase()}` : ""}
        </span>
      </span>
      {isActive && <span className="media-player-active-dot" />}
    </button>
  );
};
export default ThumbnailPanelItem;
