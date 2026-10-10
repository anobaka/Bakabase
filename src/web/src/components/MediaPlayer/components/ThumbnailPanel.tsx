"use client";
import type { ListRowProps } from "react-virtualized";
import type { BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry } from "@/sdk/Api";
import type { MediaPlayerEntry } from "../types";
import React, { useMemo, useRef, useState } from "react";
import { TbLayoutSidebarLeftCollapse, TbLayoutSidebarLeftExpand, TbSearch } from "react-icons/tb";
import { AutoSizer, List } from "react-virtualized";
import { useTranslation } from "react-i18next";
import ThumbnailPanelItem from "./ThumbnailPanelItem";
import { MediaType } from "@/sdk/constants";
import envConfig from "@/config/env";

interface ThumbnailPanelProps {
  entries: MediaPlayerEntry[];
  playableEntries: MediaPlayerEntry[];
  activeIndex: number;
  collapsed: boolean;
  onToggleCollapse: () => void;
  onEntryClick: (entry: BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry) => void;
  getMediaType: (entry: BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry) => MediaType;
}
const ThumbnailPanel = ({
  entries,
  playableEntries,
  activeIndex,
  collapsed,
  onToggleCollapse,
  onEntryClick,
  getMediaType,
}: ThumbnailPanelProps) => {
  const { t } = useTranslation();
  const [search, setSearch] = useState("");
  const [filter, setFilter] = useState<MediaType | undefined>();
  const list = useRef<List>(null);
  const activePath = playableEntries[activeIndex]?.path;
  const visible = useMemo(
    () =>
      entries.filter(
        (entry) =>
          (filter === undefined || getMediaType(entry) === filter) &&
          (entry.name || entry.path).toLocaleLowerCase().includes(search.toLocaleLowerCase()),
      ),
    [entries, filter, search, getMediaType],
  );
  const activeVisibleIndex = visible.findIndex((entry) => entry.path === activePath);
  const types = [MediaType.Video, MediaType.Audio, MediaType.Image, MediaType.Text].filter((type) =>
    entries.some((entry) => getMediaType(entry) === type),
  );
  const getThumbnailUrl = (entry: MediaPlayerEntry) =>
    getMediaType(entry) === MediaType.Image
      ? `${envConfig.apiEndpoint}/tool/thumbnail?path=${encodeURIComponent(entry.playPath || entry.path)}&w=96&h=96`
      : null;
  const renderRow = ({ index, key, style }: ListRowProps) => {
    const entry = visible[index];
    return (
      <div key={key} style={style}>
        <ThumbnailPanelItem
          entry={entry}
          index={index}
          activeIndex={activeIndex}
          isActive={entry.path === activePath}
          getMediaType={getMediaType}
          getThumbnailUrl={getThumbnailUrl}
          onEntryClick={onEntryClick}
          activeThumbnailRef={null}
        />
      </div>
    );
  };
  return (
    <aside
      className={`media-player-sidebar ${collapsed ? "is-collapsed" : ""}`}
      aria-label={t("mediaPlayer.files")}
    >
      <div className="media-player-sidebar-heading">
        {!collapsed && (
          <strong>
            {t("mediaPlayer.files")} <small>{entries.length}</small>
          </strong>
        )}
        <button
          className="media-player-icon-button"
          title={t(collapsed ? "mediaPlayer.expandFiles" : "mediaPlayer.collapseFiles")}
          aria-label={t(collapsed ? "mediaPlayer.expandFiles" : "mediaPlayer.collapseFiles")}
          onClick={onToggleCollapse}
        >
          {collapsed ? (
            <TbLayoutSidebarLeftExpand size={20} />
          ) : (
            <TbLayoutSidebarLeftCollapse size={20} />
          )}
        </button>
      </div>
      {!collapsed && (
        <>
          <label className="media-player-search">
            <TbSearch size={16} />
            <input
              placeholder={t("mediaPlayer.searchFiles")}
              aria-label={t("mediaPlayer.searchFiles")}
              value={search}
              onChange={(event) => setSearch(event.target.value)}
            />
          </label>
          {types.length > 1 && (
            <div className="media-player-filters">
              <button
                className={filter === undefined ? "is-active" : ""}
                onClick={() => setFilter(undefined)}
              >
                {t("mediaPlayer.type.all")}
              </button>
              {types.map((type) => (
                <button
                  key={type}
                  className={filter === type ? "is-active" : ""}
                  onClick={() => setFilter(type)}
                >
                  {t(
                    `mediaPlayer.type.${type === MediaType.Video ? "video" : type === MediaType.Audio ? "audio" : type === MediaType.Image ? "image" : "text"}`,
                  )}
                </button>
              ))}
            </div>
          )}
          <div
            className="media-player-file-list"
            role="listbox"
            aria-label={t("mediaPlayer.files")}
          >
            {visible.length ? (
              <AutoSizer>
                {({ height, width }) => (
                  <List
                    ref={list}
                    height={height}
                    width={width}
                    rowCount={visible.length}
                    rowHeight={72}
                    rowRenderer={renderRow}
                    overscanRowCount={5}
                    scrollToIndex={activeVisibleIndex >= 0 ? activeVisibleIndex : undefined}
                  />
                )}
              </AutoSizer>
            ) : (
              <div className="media-player-empty media-player-list-empty">
                {t("mediaPlayer.noMatchingFiles")}
              </div>
            )}
          </div>
        </>
      )}
    </aside>
  );
};
export default ThumbnailPanel;
