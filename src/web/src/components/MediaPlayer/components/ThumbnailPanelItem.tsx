"use client";

import React, { useState } from "react";
import { MdInventory } from "react-icons/md";

import FileSystemEntryIcon from "@/components/FileSystemEntryIcon";
import { IconType, IwFsType, MediaType } from "@/sdk/constants";
import { Spinner } from "@/components/bakaui";
import type { BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry } from "@/sdk/Api";
import type { MediaPlayerEntry } from "../types";

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

const ThumbnailPanelItem: React.FC<ThumbnailPanelItemProps> = ({
  entry,
  index,
  activeIndex,
  isActive,
  getThumbnailUrl,
  getMediaType,
  onEntryClick,
  activeThumbnailRef,
}) => {
  const [imageLoading, setImageLoading] = useState(true);
  const [imageError, setImageError] = useState(false);

  const thumbnailUrl = getThumbnailUrl(entry);
  const mediaType = getMediaType(entry);
  const isDirectory = entry.type === IwFsType.Directory;
  const isCompressedFile = entry.type === IwFsType.CompressedFileEntry;

  return (
    <div
      ref={isActive ? activeThumbnailRef : null}
      className={`group cursor-pointer transition-all duration-200 border-transparent relative ${
        isActive ? "bg-blue-500/20 border-l-blue-500/80" : "hover:bg-white/5"
      }`}
      title={entry.name}
      onClick={() => onEntryClick(entry)}
    >
      <div className="flex flex-col items-center gap-1.5 relative w-full">
        <div className="relative w-[120px] h-[120px] flex items-center justify-center bg-black/30 rounded overflow-hidden thumbnail-image-container">
          {thumbnailUrl ? (
            <>
              {imageLoading && !imageError && (
                <div className="absolute inset-0 flex items-center justify-center z-[2] bg-black/30">
                  <Spinner size="sm" />
                </div>
              )}
              <img
                alt={entry.name}
                className="w-full h-full object-contain thumbnail-image"
                src={thumbnailUrl}
                onLoad={() => setImageLoading(false)}
                onError={(e) => {
                  setImageLoading(false);
                  setImageError(true);
                  // Fallback to icon if thumbnail fails
                  const target = e.target as HTMLImageElement;
                  target.style.display = "none";
                  if (target.parentElement) {
                    target.parentElement.classList.add("icon-fallback");
                  }
                }}
              />
              {imageError && (
                <div className="flex items-center justify-center w-full h-full text-white/70 thumbnail-icon">
                  <FileSystemEntryIcon
                    path={entry.path}
                    size={40}
                    type={isDirectory ? IconType.Directory : IconType.Dynamic}
                  />
                </div>
              )}
            </>
          ) : (
            <div className="flex items-center justify-center w-full h-full text-white/70 thumbnail-icon">
              <FileSystemEntryIcon
                path={entry.path}
                size={40}
                type={isDirectory ? IconType.Directory : IconType.Dynamic}
              />
            </div>
          )}
          {/* Name overlay - appears on hover */}
          <div className="absolute top-0 left-0 right-0 px-2 py-1 opacity-0 group-hover:opacity-100 transition-opacity duration-200 max-h-full overflow-hidden">
            <div className="text-white/90 text-xs text-center break-words line-clamp-6" title={entry.name}>
              {entry.name}
            </div>
          </div>
          {mediaType === MediaType.Video && (
            <div className="absolute bottom-1 right-1 px-1.5 py-0.5 rounded text-[10px] font-semibold uppercase bg-red-500/80 text-white/90">
              VIDEO
            </div>
          )}
          {mediaType === MediaType.Audio && (
            <div className="absolute bottom-1 right-1 px-1.5 py-0.5 rounded text-[10px] font-semibold uppercase bg-green-500/80 text-white/90">
              AUDIO
            </div>
          )}
          {mediaType === MediaType.Text && (
            <div className="absolute bottom-1 right-1 px-1.5 py-0.5 rounded text-[10px] font-semibold uppercase bg-purple-500/80 text-white/90">
              TEXT
            </div>
          )}
          {(isDirectory || isCompressedFile) && (
            <div className="absolute top-1 left-1 px-1.5 py-0.5 rounded text-[10px] font-semibold uppercase bg-blue-500/80 text-white/90">
              {isDirectory ? "DIR" : "ZIP"}
            </div>
          )}
        </div>
      </div>
    </div>
  );
};

export default ThumbnailPanelItem;


