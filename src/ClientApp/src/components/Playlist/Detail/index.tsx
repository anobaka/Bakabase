import React, { useEffect, useState } from 'react';
import { GetAllPlaylists, GetPlaylist, GetResourcesByKeys, OpenFileOrDirectory, SortCategories } from '@/sdk/apis';
import './index.scss';
import { Button, Input, NumberPicker, TimePicker2 } from '@alifd/next';
import i18n from 'i18next';
import CustomIcon from '@/components/CustomIcon';
import { PlaylistItemType } from '@/sdk/constants';
import SortablePlaylistItemList from '@/components/Playlist/Detail/components/SortablePlaylistItemList';
import BApi from '@/sdk/BApi';

export default ({ id, onChange }) => {
  const [playlist, setPlaylist] = useState({});
  const [resources, setResources] = useState({});

  useEffect(() => {
    BApi.playlist.getPlaylist(id).then((a) => {
      const pl = a.data;
      setPlaylist(pl);
      const resourceIds = (pl.items?.map((b) => b.resourceId) ?? []).filter(a => a)!;
      const distinctResourceIds = resourceIds.filter((t, i) => resourceIds.indexOf(t) == i);
      if (distinctResourceIds.length > 0) {
        BApi.resource.getResourcesByKeys({ ids: distinctResourceIds }).then((c) => {
          setResources((c.data || []).reduce((s, t) => {
            s[t.id] = t;
            return s;
          }, {}));
        });
      }
    });
  }, []);

  const patchPlaylist = (patches: any = undefined) => {
    const pl = {
      ...playlist,
      ...(patches || {}),
    };
    setPlaylist(pl);
    onChange(pl);
  };

  const renderPlaylistItems = () => {
    const items = playlist.items || [];
    return (
      <SortablePlaylistItemList
        useDragHandle
        items={items}
        resources={resources}
        onRemove={(item) => {
          patchPlaylist({
            items: items.filter((a) => a != item),
          });
        }}
        onSortEnd={({ newIndex, oldIndex }) => {
          const oi = items[oldIndex];
          items.splice(oldIndex, 1);
          items.splice(newIndex, 0, oi);
          patchPlaylist();
        }}
      />
    );
  };

  return (
    <div className={'play-list-detail'}>
      <div className="info">
        <div className="name">
          <div className="label">
            {i18n.t('Name')}
          </div>
          <div className="value">
            <Input
              value={playlist.name}
              onChange={(v) => {
                patchPlaylist({
                  name: v,
                });
              }}
            />
          </div>
        </div>
        <div className="interval">
          <div className="label">
            {i18n.t('Interval')}
          </div>
          <div className="value">
            <NumberPicker
              min={0}
              value={playlist.interval}
              onChange={(v) => {
                patchPlaylist({
                  interval: v,
                });
              }}
              innerAfter={i18n.t('ms')}
            />
          </div>
        </div>
      </div>
      <div className="items">
        {renderPlaylistItems()}
      </div>
    </div>
  );
};
