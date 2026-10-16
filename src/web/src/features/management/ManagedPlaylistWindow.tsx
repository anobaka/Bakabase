import { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import type { ManagementSource } from "./api";
import { ownerClient } from "./api";
import { managedResourceAssetUrl } from "./resourceApi";
import { managedMediaUrl } from "./playback";
import type { ResourceBrowserFilesPage } from "./ResourceBrowserWindow";
import type { Resource } from "@/core/models/Resource";
import type { MediaPlayerEntry } from "@/components/MediaPlayer/types";
import MediaPlayer from "@/components/MediaPlayer";
import { Button, Spinner } from "@/components/bakaui";
import { IwFsType } from "@/sdk/constants";

type Cursor = { item: number; offset: number };

/** The normal browser player reads only one bounded resource page at a time. */
export default function ManagedPlaylistWindow({
  source,
  items,
  interval,
}: {
  source: ManagementSource;
  items: { resourceId?: number; file?: string }[];
  interval?: number;
}) {
  const { t } = useTranslation();
  const [cursor, setCursor] = useState<Cursor>({ item: 0, offset: 0 });
  const [history, setHistory] = useState<Cursor[]>([]);
  const [page, setPage] = useState<ResourceBrowserFilesPage>();
  const [error, setError] = useState<string>();
  const [loading, setLoading] = useState(false);
  const [hasLocalPath, setHasLocalPath] = useState(false);
  const resourceId = items[cursor.item]?.resourceId;
  useEffect(() => {
    let cancelled = false;
    setPage(undefined);
    setError(undefined);
    if (!resourceId || items[cursor.item]?.file) {
      setLoading(false);
      setError(
        t(items.length ? "management.playlist.fileItems" : "playlist.label.noPlayableContents"),
      );
      return;
    }
    setLoading(true);
    const request = ownerClient(source);
    Promise.all([
      request<ResourceBrowserFilesPage>("resourceBrowserFiles", {
        id: resourceId,
        query: { offset: [String(cursor.offset)], pageSize: ["100"], scope: ["all"] },
      }),
      request<Resource[]>("resourceGet", { ids: [resourceId] }),
    ])
      .then(([response, metadata]) => {
        if (cancelled) return;
        if (response.code || !response.data)
          throw new Error(response.message || t("mediaPlayer.filesFailed"));
        if (metadata.code || !metadata.data?.length)
          throw new Error(metadata.message || t("mediaPlayer.filesFailed"));
        setHasLocalPath(!!metadata.data[0].hasLocalPath);
        setPage(response.data);
      })
      .catch((failure) => {
        if (!cancelled) setError(String(failure));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [source, resourceId, cursor, items, t]);
  const entries = useMemo<MediaPlayerEntry[]>(
    () =>
      (page?.items ?? []).map((item) => ({
        path: item.key,
        name: item.displayName || item.key.split(/[/\\]/).pop() || item.key,
        meaningfulName: item.displayName || item.key.split(/[/\\]/).pop() || item.key,
        type: IwFsType.Unknown,
        passwordsForDecompressing: [],
        mediaUrl: hasLocalPath
          ? managedResourceAssetUrl(source, resourceId!, item.key)
          : managedMediaUrl(source, resourceId!, item),
        thumbnailUrl: null,
      })),
    [page, source, resourceId, hasLocalPath],
  );
  const next = page?.hasMore
    ? { item: cursor.item, offset: page.nextOffset! }
    : cursor.item + 1 < items.length
      ? { item: cursor.item + 1, offset: 0 }
      : undefined;
  return (
    <div className="flex h-full min-h-0 flex-col">
      <div className="min-h-0 flex-1">
        {loading ? (
          <Spinner />
        ) : error ? (
          <p role="alert" className="p-4 text-danger">
            {error}
          </p>
        ) : (
          <MediaPlayer
            key={`${cursor.item}:${cursor.offset}`}
            entries={entries}
            interval={interval}
            autoPlay
          />
        )}
      </div>
      <div className="flex items-center justify-end gap-2 border-t border-default-200 px-3 py-1">
        <span className="text-xs text-default-500">
          {items.length ? cursor.item + 1 : 0} / {items.length}
        </span>
        <Button
          size="sm"
          variant="light"
          isDisabled={loading || !history.length}
          onPress={() => {
            setCursor(history[history.length - 1]);
            setHistory(history.slice(0, -1));
          }}
        >
          {t("management.previous")}
        </Button>
        <Button
          size="sm"
          variant="light"
          isDisabled={loading || !next}
          onPress={() => {
            if (next) {
              setHistory([...history, cursor]);
              setCursor(next);
            }
          }}
        >
          {t("management.next")}
        </Button>
      </div>
    </div>
  );
}
