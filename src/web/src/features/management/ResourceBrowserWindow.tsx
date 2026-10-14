import type { PlayableItem, Resource } from "@/core/models/Resource";
import type { ResourceBrowserPlayerOptions } from "@/hooks/useResourceBrowserPlayer";
import type { ManagementSource } from "./api";
import type { MediaBrowseScope } from "@/components/MediaPlayer/types";

import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";

import { ownerClient } from "./api";
import { managedMediaUrl } from "./playback";
import { managedResourceAssetUrl } from "./resourceApi";

import MediaPlayer from "@/components/MediaPlayer";
import { Button } from "@/components/bakaui";
import { IwFsType } from "@/sdk/constants";
import { selectInitialMediaIndex } from "@/components/MediaPlayer/media";

export type ResourceBrowserFilesPage = {
  items: PlayableItem[];
  offset: number;
  nextOffset?: number;
  hasMore: boolean;
  playableFilesConfigured?: boolean;
};

/** The ordinary player renders owner-delivered files; large folders stay paged. */
export default function ResourceBrowserWindow({
  source,
  resource,
  initialPage,
  initialPath,
  options,
}: {
  source: ManagementSource;
  resource: Resource;
  initialPage: ResourceBrowserFilesPage;
  initialPath?: string;
  options: ResourceBrowserPlayerOptions;
}) {
  const { t } = useTranslation();
  const [page, setPage] = useState(initialPage);
  const [scope, setScope] = useState<MediaBrowseScope>(options.scope ?? "resource");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string>();
  const request = useMemo(() => ownerClient(source), [source]);
  const entries = useMemo(
    () =>
      page.items.map((item) => ({
        path: item.key,
        name: item.displayName || item.key.split(/[/\\]/).pop() || item.key,
        meaningfulName: item.displayName || item.key.split(/[/\\]/).pop() || item.key,
        type: IwFsType.Unknown,
        passwordsForDecompressing: [],
        mediaUrl:
          scope === "resource" && resource.hasLocalPath
            ? managedResourceAssetUrl(source, resource.id, item.key)
            : managedMediaUrl(source, resource.id, item),
        thumbnailUrl: null,
      })),
    [page, source, resource.id, resource.hasLocalPath, scope],
  );
  const activeIndex = selectInitialMediaIndex(entries, initialPath ? [initialPath] : []);
  const changePage = async (offset: number, nextScope = scope) => {
    setLoading(true);
    setError(undefined);
    try {
      const response = await request<ResourceBrowserFilesPage>("resourceBrowserFiles", {
        id: resource.id,
        query: {
          offset: [String(offset)],
          pageSize: ["100"],
          scope: [nextScope === "configured" ? "configured" : "all"],
        },
      });

      if (!response.data) throw new Error(t("mediaPlayer.filesFailed"));
      setPage(response.data);
      setScope(nextScope);
    } catch (failure) {
      setError(String(failure));
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="flex h-full min-h-0 flex-col">
      {!resource.isFile && (
        <div className="media-player-scope" title={t("mediaPlayer.scope.hint")}>
          <label>
            <span>{t("mediaPlayer.scope.label")}</span>
            <select
              aria-label={t("mediaPlayer.scope.label")}
              disabled={loading}
              value={scope}
              onChange={(event) => void changePage(0, event.target.value as MediaBrowseScope)}
            >
              <option value="resource">{t("mediaPlayer.scope.resource")}</option>
              <option value="configured">{t("mediaPlayer.scope.configured")}</option>
            </select>
          </label>
        </div>
      )}
      <div className="min-h-0 flex-1">
        <MediaPlayer
          key={`${scope}-${page.offset}`}
          entries={entries}
          defaultActiveIndex={activeIndex}
          onOpenAlternative={options.onOpenAlternative}
          canOpenAlternative={options.canOpenAlternative}
        />
      </div>
      {(page.hasMore || page.offset > 0 || error) && (
        <div className="flex shrink-0 items-center justify-end gap-2 border-t border-default-200 px-3 py-1">
          {error && (
            <p className="mr-auto text-xs text-danger" role="alert">
              {error}
            </p>
          )}
          <span className="text-xs text-default-500">
            {page.offset + 1}–{page.offset + page.items.length}
          </span>
          <Button
            size="sm"
            variant="light"
            isDisabled={loading || page.offset === 0}
            onPress={() => void changePage(Math.max(0, page.offset - 100))}
          >
            {t("management.previous")}
          </Button>
          <Button
            size="sm"
            variant="light"
            isLoading={loading}
            isDisabled={loading || !page.hasMore}
            onPress={() => void changePage(page.nextOffset!)}
          >
            {t("management.next")}
          </Button>
        </div>
      )}
    </div>
  );
}
