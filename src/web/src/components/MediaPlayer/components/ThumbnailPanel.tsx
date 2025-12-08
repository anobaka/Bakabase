"use client";

import React, { useCallback, useEffect, useRef, useState } from "react";
import {
  TbChevronLeft,
  TbChevronRight,
} from "react-icons/tb";
import { AutoSizer, List } from "react-virtualized";
import type { ListRowProps } from "react-virtualized";

import { MediaType } from "@/sdk/constants";
import envConfig from "@/config/env";
import ThumbnailPanelItem from "./ThumbnailPanelItem";
import type { BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry } from "@/sdk/Api";
import type { MediaPlayerEntry } from "../types";

interface ThumbnailPanelProps {
  entries: MediaPlayerEntry[];
  playableEntries: MediaPlayerEntry[];
  activeIndex: number;
  collapsed: boolean;
  onToggleCollapse: () => void;
  onEntryClick: (entry: BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry) => void;
  getMediaType: (entry: BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry) => MediaType;
}

// Estimated row height: image (120px) + gap (6px) = ~126px
const ROW_HEIGHT = 126;

const ThumbnailPanel: React.FC<ThumbnailPanelProps> = ({
  entries,
  playableEntries,
  activeIndex,
  collapsed,
  onToggleCollapse,
  onEntryClick,
  getMediaType,
}) => {
  const thumbnailPanelRef = useRef<HTMLDivElement>(null);
  const activeThumbnailRef = useRef<HTMLDivElement>(null);
  const virtualListRef = useRef<List>(null);
  const isInitialMount = useRef(true);

  const getThumbnailUrl = useCallback(
    (entry: MediaPlayerEntry): string | null => {
      const mediaType = getMediaType(entry);

      if (mediaType === MediaType.Image) {
        // Use playPath for compressed file entries, otherwise use path
        const pathToUse = entry.playPath || entry.path;
        return `${envConfig.apiEndpoint}/tool/thumbnail?path=${encodeURIComponent(pathToUse)}&w=150&h=150`;
      }

      return null;
    },
    [getMediaType],
  );

  // Scroll active thumbnail into view (skip on initial mount and when entries change)
  const prevEntriesLengthRef = useRef(entries.length);
  useEffect(() => {
    if (isInitialMount.current) {
      isInitialMount.current = false;
      prevEntriesLengthRef.current = entries.length;
      return;
    }

    // Don't scroll if entries array changed (likely initial load)
    if (entries.length !== prevEntriesLengthRef.current) {
      prevEntriesLengthRef.current = entries.length;
      return;
    }

    if (!collapsed && virtualListRef.current && entries.length > 0) {
      // Find the index of the active entry in the entries array
      const activeEntry = playableEntries[activeIndex];
      if (activeEntry) {
        const entryIndex = entries.findIndex((e) => e.path === activeEntry.path);
        if (entryIndex >= 0) {
          virtualListRef.current.scrollToRow(entryIndex);
        }
      }
    }
  }, [activeIndex, collapsed, entries, playableEntries]);

  const rowRenderer = useCallback(
    ({ index, key, style }: ListRowProps) => {
      const entry = entries[index];
      if (!entry) return null;

      const playableIndex = playableEntries.findIndex((e) => e.path === entry.path);
      const isActive = playableIndex === activeIndex && playableIndex >= 0;

      return (
        <div key={key} style={{ ...style, padding: 0 }}>
          <ThumbnailPanelItem
            activeIndex={activeIndex}
            activeThumbnailRef={isActive ? activeThumbnailRef : null}
            entry={entry}
            getMediaType={getMediaType}
            getThumbnailUrl={getThumbnailUrl}
            index={playableIndex}
            isActive={isActive}
            onEntryClick={onEntryClick}
          />
        </div>
      );
    },
    [
      entries,
      playableEntries,
      activeIndex,
      getMediaType,
      getThumbnailUrl,
      onEntryClick,
    ],
  );

  if (collapsed) {
    return (
      <TbChevronRight
        className="absolute left-1 top-1/2 -translate-y-1/2 text-white/70 hover:text-white text-xl cursor-pointer hover:scale-110 transition-all duration-200 z-10"
        onClick={onToggleCollapse}
        title="Expand thumbnail panel"
      />
    );
  }

  return (
    <div className="relative bg-[rgba(30,30,30,0.95)] border-r border-white/10 flex flex-col transition-all duration-300 ease-in-out w-[120px] min-w-[120px] max-w-[120px]">
      <TbChevronLeft
        className="absolute right-1 top-1/2 -translate-y-1/2 text-white/70 hover:text-white text-xl cursor-pointer hover:scale-110 transition-all duration-200 z-10"
        onClick={onToggleCollapse}
        title="Collapse thumbnail panel"
      />
      <div
        ref={thumbnailPanelRef}
        className="flex-1 overflow-hidden thumbnail-list"
        onMouseDown={(e) => {
          // Prevent text selection when clicking and dragging
          if (e.button === 0) {
            // Left mouse button
            e.preventDefault();
          }
        }}
        style={{ userSelect: "none" }}
      >
        {entries.length > 0 ? (
          <AutoSizer>
            {({ width, height }) => (
              <List
                ref={virtualListRef}
                className="thumbnail-list-virtualized"
                height={height}
                width={width}
                rowCount={entries.length}
                rowHeight={ROW_HEIGHT}
                rowRenderer={rowRenderer}
                overscanRowCount={15}
              />
            )}
          </AutoSizer>
        ) : (
          <div className="flex items-center justify-center h-full text-white/50 text-sm">
            No entries
          </div>
        )}
      </div>
    </div>
  );
};

export default ThumbnailPanel;
