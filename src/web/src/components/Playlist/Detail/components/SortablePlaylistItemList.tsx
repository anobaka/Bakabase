"use client";

import type { components } from "@/sdk/BApi2";

import { SortableContainer, SortableContainerProps } from "react-sortable-hoc";
import { useTranslation } from "react-i18next";

import SortablePlaylistItem from "@/components/Playlist/Detail/components/SortablePlaylistItem";

type PlayListItem =
  components["schemas"]["Bakabase.InsideWorld.Business.Components.PlayList.Models.Domain.PlayListItem"];

interface SortablePlaylistItemListProps {
  items: PlayListItem[];
  resources: { [key: number]: any };
  onRemove: (item: PlayListItem) => void;
  onSortEnd: SortableContainerProps["onSortEnd"];
  useDragHandle?: boolean;
}

export default SortableContainer<SortablePlaylistItemListProps>(
  ({ items, resources, onRemove }: SortablePlaylistItemListProps) => {
    const { t } = useTranslation();
    
    return (
      <div className="w-full">
        {/* Header Row */}
        <div 
          className="grid grid-cols-[auto_auto_1fr_auto_auto] gap-2 items-center px-2 py-2 border-b text-sm font-medium"
          style={{
            backgroundColor: 'var(--theme-table-header-background)',
            color: 'var(--theme-text)',
            borderColor: 'var(--theme-border-color)',
          }}
        >
          <div className="w-8"></div>
          <div className="w-6"></div>
          <div>{t<string>("Resource / File")}</div>
          <div className="text-right">{t<string>("Duration")}</div>
          <div className="w-10"></div>
        </div>
        {/* Sortable Items */}
        <div className="max-h-[800px] overflow-y-auto">
          {items.map((item, index) => {
            return (
              <SortablePlaylistItem
                key={item.id ?? index}
                index={index}
                item={item}
                resource={resources[item.resourceId!]}
                onRemove={onRemove}
              />
            );
          })}
        </div>
      </div>
    );
  },
  { 
    withRef: false,
    helperClass: 'sortable-playlist-item-helper',
  },
);
