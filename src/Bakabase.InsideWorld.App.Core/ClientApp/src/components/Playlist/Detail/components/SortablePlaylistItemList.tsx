import { SortableContainer } from 'react-sortable-hoc';
import React from 'react';
import SortablePlaylistItem from '@/components/Playlist/Detail/components/SortablePlaylistItem';

export default SortableContainer(({ items, resources, onRemove }) => {
  return (
    <div className="categories">
      {items.map((item, index) => {
        return (
          <SortablePlaylistItem
            index={index}
            key={index}
            item={item}
            resource={resources[item.resourceId]}
            onRemove={onRemove}
          />
        );
      })}
    </div>
  );
});
