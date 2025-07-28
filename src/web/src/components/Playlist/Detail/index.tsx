"use client";

import type { components } from "@/sdk/BApi2";

import { useCallback, useEffect, useState } from "react";
import i18n from "i18next";
import { useTranslation } from "react-i18next";

import { Input, NumberInput } from "@/components/bakaui";
import SortablePlaylistItemList from "@/components/Playlist/Detail/components/SortablePlaylistItemList";
import BApi from "@/sdk/BApi";

type PlayList =
  components["schemas"]["Bakabase.InsideWorld.Business.Components.PlayList.Models.Domain.PlayList"];

interface PlaylistDetailProps {
  id: number;
  onChange: (playlist: PlayList) => void;
}

interface ResourceMap {
  [key: number]: any;
}
const Detail = ({ id, onChange }: PlaylistDetailProps) => {
  const { t } = useTranslation();
  const [playlist, setPlaylist] = useState<PlayList | null>(null);
  const [resourceMap, setResourceMap] = useState<ResourceMap>({});
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let isMounted = true;

    const loadPlaylistData = async () => {
      try {
        setLoading(true);
        setError(null);

        const playlistResponse = await BApi.playlist.getPlaylist(id);
        const pl = playlistResponse.data;

        if (!isMounted) return;
        if (!pl) {
          setError("Playlist data is empty");

          return;
        }

        setPlaylist(pl);

        const resourceIds = (
          pl.items?.map((item) => item.resourceId) ?? []
        ).filter((resourceId): resourceId is number => resourceId != null);

        const distinctResourceIds = [...new Set(resourceIds)];

        if (distinctResourceIds.length > 0) {
          const resourcesResponse = await BApi.resource.getResourcesByKeys({
            ids: distinctResourceIds,
          });

          if (!isMounted) return;

          const resourceMap = (
            resourcesResponse.data || []
          ).reduce<ResourceMap>((acc, resource) => {
            acc[resource.id!] = resource;

            return acc;
          }, {});

          setResourceMap(resourceMap);
        }
      } catch (err) {
        if (!isMounted) return;
        setError(
          err instanceof Error ? err.message : "Failed to load playlist",
        );
      } finally {
        if (isMounted) {
          setLoading(false);
        }
      }
    };

    loadPlaylistData();

    return () => {
      isMounted = false;
    };
  }, [id]);

  const patchPlaylist = useCallback(
    (patches: Partial<PlayList> = {}) => {
      if (!playlist) return;

      const updatedPlaylist = {
        ...playlist,
        ...patches,
      };

      setPlaylist(updatedPlaylist);
      onChange(updatedPlaylist);
    },
    [playlist, onChange],
  );

  const handleRemoveItem = useCallback(
    (itemToRemove: any) => {
      if (!playlist?.items) return;

      patchPlaylist({
        items: playlist.items.filter((item) => item !== itemToRemove),
      });
    },
    [playlist?.items, patchPlaylist],
  );

  const handleSortEnd = useCallback(
    ({ newIndex, oldIndex }: { newIndex: number; oldIndex: number }) => {
      if (!playlist?.items) return;

      const items = [...playlist.items];
      const [movedItem] = items.splice(oldIndex, 1);

      items.splice(newIndex, 0, movedItem);

      patchPlaylist({ items });
    },
    [playlist?.items, patchPlaylist],
  );

  const handleNameChange = useCallback(
    (name: string) => {
      patchPlaylist({ name });
    },
    [patchPlaylist],
  );

  const handleIntervalChange = useCallback(
    (interval: number) => {
      patchPlaylist({ interval });
    },
    [patchPlaylist],
  );

  if (loading) {
    return (
      <div className="p-4">
        <div className="flex justify-center items-center py-5 text-gray-500">
          Loading playlist...
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="p-4">
        <div className="flex justify-center items-center py-5 px-4 text-red-600 bg-red-50 rounded border border-red-200">
          Error: {error}
        </div>
      </div>
    );
  }

  if (!playlist) {
    return (
      <div className="p-4">
        <div className="flex justify-center items-center py-5 px-4 text-red-600 bg-red-50 rounded border border-red-200">
          Playlist not found
        </div>
      </div>
    );
  }

  return (
    <div className="p-4 space-y-4">
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-2">
        <Input
          fullWidth={false}
          label={t<string>("Name")}
          size="sm"
          value={playlist.name || ""}
          onValueChange={handleNameChange}
        />
        <NumberInput
          endContent={i18n.t<string>("ms")}
          fullWidth={false}
          label={t<string>("Interval")}
          min={0}
          size="sm"
          value={playlist.interval || 0}
          onValueChange={handleIntervalChange}
        />
      </div>
      <div className="mt-4">
        <SortablePlaylistItemList
          useDragHandle
          items={playlist?.items ?? []}
          resources={resourceMap}
          onRemove={handleRemoveItem}
          onSortEnd={handleSortEnd}
        />
      </div>
    </div>
  );
};

Detail.displayName = "Detail";

export default Detail;
