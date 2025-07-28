"use client";

import type { components } from "@/sdk/BApi2";

import { SortableContainer } from "react-sortable-hoc";

import SortablePlaylistItem from "@/components/Playlist/Detail/components/SortablePlaylistItem";

type PlayListItem =
  components["schemas"]["Bakabase.InsideWorld.Business.Components.PlayList.Models.Domain.PlayListItem"];

interface SortablePlaylistItemListProps {
  items: PlayListItem[];
  resources: { [key: number]: any };
  onRemove: (item: PlayListItem) => void;
}

export default SortableContainer<SortablePlaylistItemListProps>(
  ({ items, resources, onRemove }: SortablePlaylistItemListProps) => {
    return (
      <div className="space-y-0">
        {items.map((item, index) => {
          return (
            <SortablePlaylistItem
              key={index}
              index={index}
              item={item}
              resource={resources[item.resourceId!]}
              onRemove={onRemove}
            />
          );
        })}
      </div>
    );
  },
);
