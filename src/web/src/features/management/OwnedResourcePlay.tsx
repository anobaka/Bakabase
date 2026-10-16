import type { PlayableItem, Resource } from "@/core/models/Resource";
import type { MediaPlayerEntry } from "@/components/MediaPlayer/types";
import type { ManagementSource } from "./api";

import { useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { PlayCircleOutlined } from "@ant-design/icons";

import { ownerClient, ownedResourceKey } from "./api";
import { managedMediaUrl, playManagedResource } from "./playback";

import { Button, Modal } from "@/components/bakaui";
import PlayControl from "@/components/Resource/components/PlayControl";
import { ResourceDetailPlaybackButton } from "@/components/Resource/components/PlayControl/PlaybackButtons";
import { resolvePlaybackMethod } from "@/components/Resource/components/PlayControl/playbackPolicy";
import { sourceActionTarget } from "@/components/Resource/components/PlayControl/sourceActions";
import { DesktopPlaybackGuideButton } from "@/components/Resource/components/PlayControl/DesktopPlaybackGuide";
import { useSourceResourceProfile } from "@/features/playback/resourceProfiles";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import MediaPlayer from "@/components/MediaPlayer";
import { selectInitialMediaIndex } from "@/components/MediaPlayer/media";
import { initialWindowBounds } from "@/components/Window/bounds";
import { MEDIA_PLAYER_WINDOW_PERSISTENCE_KEY } from "@/components/Window/persistence";
import { useUiOptionsStore } from "@/stores/options";
import { useUserSideActionsRunHere } from "@/stores/remoteAccess";
import { isDesktopAppWindow } from "@/utils/clientSurface";
import { DataOrigin, IwFsType, ResourceSource } from "@/sdk/constants";

const pageSize = 50;

type BrowserFilesPage = {
  items: PlayableItem[];
  offset: number;
  nextOffset?: number;
  hasMore: boolean;
  playableFilesConfigured?: boolean;
};
const isFileItem = (item: PlayableItem) =>
  item.origin === DataOrigin.FileSystem || item.origin === DataOrigin.Manual;

/** Local resources retain the original discovery, profile, directory and history flows. */
export default function OwnedResourcePlay(props: { source: ManagementSource; resource: Resource }) {
  return props.source.isLocal ? (
    <PlayControl PortalComponent={ResourceDetailPlaybackButton} resource={props.resource} />
  ) : (
    <ManagedResourcePlay key={ownedResourceKey(props.source, props.resource.id)} {...props} />
  );
}

function ManagedResourcePlay({
  source,
  resource,
}: {
  source: ManagementSource;
  resource: Resource;
}) {
  const { t } = useTranslation();
  const { createWindow } = useBakabaseContext();
  const nativeAvailable = useUserSideActionsRunHere();
  const appWindow = isDesktopAppWindow();
  const opening = useSourceResourceProfile(
    `node:${source.serverId}:${source.libraryEpoch}`,
    resource.id,
  );
  const request = useMemo(() => ownerClient(source), [source]);
  const autoSelectFirst = useUiOptionsStore(
    (state) => state.data?.resource?.autoSelectFirstPlayableFile,
  );
  const generation = useRef(0);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string>();
  const [items, setItems] = useState<PlayableItem[]>([]);
  const [dialog, setDialog] = useState<"choose" | "files" | "empty" | "browserPage">();
  const [browserPage, setBrowserPage] = useState<BrowserFilesPage>();
  const [selected, setSelected] = useState<PlayableItem>();
  const [visibleCount, setVisibleCount] = useState(pageSize);
  const [explicitChoice, setExplicitChoice] = useState(false);
  const canLaunch = appWindow && nativeAvailable;

  useEffect(
    () => () => {
      generation.current++;
    },
    [],
  );

  const preview = (candidates: PlayableItem[], initial?: PlayableItem) => {
    const files = candidates.filter(isFileItem);

    if (!files.length) {
      setDialog("empty");

      return;
    }
    const entries: MediaPlayerEntry[] = files.map((item) => ({
      path: item.key,
      name: item.displayName || item.key.split(/[/\\]/).pop() || item.key,
      meaningfulName: item.displayName || item.key.split(/[/\\]/).pop() || item.key,
      type: IwFsType.Unknown,
      passwordsForDecompressing: [],
      mediaUrl: managedMediaUrl(source, resource.id, item),
      // Do not query the manager's filesystem for an owner's thumbnail.
      thumbnailUrl: null,
    }));
    const bounds = initialWindowBounds(
      { initialSize: { width: 1120, height: 760 } },
      window.innerWidth,
      window.innerHeight,
    );
    const initialIndex = selectInitialMediaIndex(
      entries,
      initial ? [initial.key] : files.map((item) => item.key),
    );

    createWindow(
      MediaPlayer,
      {
        entries,
        defaultActiveIndex: initialIndex,
        onOpenAlternative: canLaunch
          ? (path: string) => {
              const candidate = files.find((item) => item.key === path);

              if (!candidate) throw new Error(t("management.play.fileChanged"));

              return playManagedResource(source, resource.id, candidate, true);
            }
          : undefined,
      },
      {
        title: `${resource.displayName || resource.path || resource.id} · ${source.name}`,
        persistenceKey: MEDIA_PLAYER_WINDOW_PERSISTENCE_KEY,
        persistent: true,
        initialSize: { width: bounds.width, height: bounds.height },
        initialPosition: { x: bounds.x, y: bounds.y },
      },
    );
    setDialog(undefined);
    const selectedEntry = entries[initialIndex];

    if (selectedEntry)
      void request("resourceMarkPlayed", {
        id: resource.id,
        query: { item: [selectedEntry.path] },
      }).catch(() => {});
  };

  const previewBrowserPage = (page: BrowserFilesPage) => {
    if (page.hasMore || page.offset > 0) setDialog("browserPage");
    else preview(page.items);
  };
  const loadBrowserPage = async (offset: number) => {
    const current = generation.current;

    setLoading(true);
    setError(undefined);
    try {
      const response = await request<BrowserFilesPage>("resourceBrowserFiles", {
        id: resource.id,
        query: { offset: [String(offset)], pageSize: ["100"] },
      });

      if (current !== generation.current) return;
      if (!response.data) throw new Error(t("management.play.noFiles"));
      setBrowserPage(response.data);
      setDialog("browserPage");
    } catch (failure) {
      if (current === generation.current) setError(String(failure));
    } finally {
      if (current === generation.current) setLoading(false);
    }
  };

  const launch = async (item: PlayableItem, explicit: boolean) => {
    const current = generation.current;

    if (!canLaunch) {
      if (item.origin === DataOrigin.Steam) {
        const action = sourceActionTarget(ResourceSource.Steam, item.key);

        if (action) window.open(action.href, "_self");
      } else {
        setSelected(item);
        setDialog("choose");
      }

      return;
    }
    setLoading(true);
    setError(undefined);
    try {
      await playManagedResource(source, resource.id, item, explicit);
      if (current === generation.current) setDialog(undefined);
    } catch (failure) {
      if (current === generation.current) {
        setError(String(failure));
        setSelected(item);
        setDialog("choose");
      }
    } finally {
      if (current === generation.current) setLoading(false);
    }
  };

  const launchCandidates = async (candidates: PlayableItem[], explicit: boolean) => {
    setExplicitChoice(explicit);
    if (candidates.length === 1 || autoSelectFirst) await launch(candidates[0]!, explicit);
    else {
      setVisibleCount(pageSize);
      setDialog("files");
    }
  };

  const play = async () => {
    const current = generation.current;

    setLoading(true);
    setError(undefined);
    setSelected(undefined);
    setBrowserPage(undefined);
    try {
      const choice = opening.choice();
      const method = resolvePlaybackMethod(choice, nativeAvailable, appWindow);
      let browserFiles: BrowserFilesPage | undefined;

      if (method === "browser" && !opening.unavailable) {
        // Browse a bounded page before requesting the potentially huge native-candidate list.
        const response = await request<BrowserFilesPage>("resourceBrowserFiles", {
          id: resource.id,
          query: { offset: ["0"], pageSize: ["100"] },
        });

        if (current !== generation.current) return;
        browserFiles = response.data;
        if (browserFiles?.items?.length) {
          setItems([]);
          setBrowserPage(browserFiles);
          previewBrowserPage(browserFiles);

          return;
        }
      }
      const response = await request<PlayableItem[]>("resourcePlayableItems", { id: resource.id });

      if (current !== generation.current) return;
      const seen = new Set<string>();
      const candidates = (response.data ?? []).filter((item) => {
        if (
          ![DataOrigin.FileSystem, DataOrigin.Manual, DataOrigin.Steam].includes(item.origin) ||
          !item.key?.trim()
        )
          return false;
        const key = JSON.stringify([item.origin, item.key]);

        if (seen.has(key)) return false;
        seen.add(key);

        return true;
      });

      setItems(candidates);
      if (!candidates.length) {
        // Native discovery intentionally returns no items without a playable-file rule.
        // Browser browsing still has an owner-resolved, bounded list of supported files.
        const response = browserFiles
          ? { data: browserFiles }
          : await request<BrowserFilesPage>("resourceBrowserFiles", {
              id: resource.id,
              query: { offset: ["0"], pageSize: ["100"] },
            });

        if (current !== generation.current) return;
        const page = response.data;

        if (page?.items?.length) {
          setBrowserPage(page);
          if (!opening.unavailable && method === "browser") previewBrowserPage(page);
          else setDialog("choose");
        } else setDialog("empty");

        return;
      }

      if (opening.unavailable || method === "ask") {
        if (opening.unavailable) setError(t("resource.playbackPolicy.rulesUnavailable"));
        setDialog("choose");
      } else if (method === "browser") {
        if (candidates.some(isFileItem)) preview(candidates);
        else await launchCandidates(candidates, false);
      } else await launchCandidates(candidates, choice?.mode === "native");
    } catch (failure) {
      if (current === generation.current) {
        setError(String(failure));
        setDialog("empty");
      }
    } finally {
      if (current === generation.current) setLoading(false);
    }
  };

  return (
    <>
      <Button
        isIconOnly
        aria-label={t("common.action.play")}
        isDisabled={!source.available || (!opening.ready && !opening.unavailable)}
        isLoading={loading}
        size="sm"
        variant="light"
        onPress={() => void play()}
      >
        <PlayCircleOutlined />
      </Button>
      <Modal
        footer={false}
        size="md"
        title={
          dialog === "files" ? t("management.play.selectFile") : t("resource.playback.chooseMethod")
        }
        visible={!!dialog}
        onClose={() => {
          setDialog(undefined);
          setError(undefined);
        }}
      >
        {error && (
          <p className="mb-3 whitespace-pre-wrap break-words text-sm text-danger" role="alert">
            {error}
          </p>
        )}
        {dialog === "empty" ? (
          <div className="space-y-3">
            {!error && <p className="text-sm text-default-500">{t("management.play.noFiles")}</p>}
            <Button isLoading={loading} size="sm" onPress={() => void play()}>
              {t("management.retry")}
            </Button>
          </div>
        ) : dialog === "browserPage" && browserPage ? (
          <div className="space-y-3">
            <p className="text-sm text-default-500">{t("management.play.browserPageHelp")}</p>
            <div className="max-h-[40vh] overflow-y-auto text-sm">
              {browserPage.items.map((item) => (
                <p key={item.key} className="truncate py-1" title={item.key}>
                  {item.displayName || item.key}
                </p>
              ))}
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <Button
                isDisabled={loading || browserPage.offset === 0}
                onPress={() => void loadBrowserPage(Math.max(0, browserPage.offset - 100))}
              >
                {t("management.previous")}
              </Button>
              <Button
                isDisabled={loading || !browserPage.hasMore}
                onPress={() => void loadBrowserPage(browserPage.nextOffset!)}
              >
                {t("management.next")}
              </Button>
              <Button
                color="primary"
                isDisabled={loading || !browserPage.items.length}
                onPress={() => preview(browserPage.items)}
              >
                {t("management.play.openPage")}
              </Button>
            </div>
          </div>
        ) : dialog === "files" ? (
          <div className="max-h-[50vh] space-y-1 overflow-y-auto">
            {items.slice(0, visibleCount).map((item) => (
              <Button
                key={JSON.stringify([item.origin, item.key])}
                fullWidth
                className="h-auto justify-start py-2 text-left"
                isDisabled={loading}
                size="sm"
                variant="light"
                onPress={() => void launch(item, explicitChoice)}
              >
                <span className="min-w-0 break-all">{item.displayName || item.key}</span>
              </Button>
            ))}
            {visibleCount < items.length && (
              <Button size="sm" onPress={() => setVisibleCount((value) => value + pageSize)}>
                {t("management.play.moreFiles")}
              </Button>
            )}
          </div>
        ) : (
          dialog === "choose" && (
            <div className="flex flex-col gap-2">
              {opening.unavailable && (
                <Button
                  isLoading={loading}
                  onPress={async () => {
                    await opening.reload();
                    setDialog(undefined);
                  }}
                >
                  {t("resource.playbackPolicy.retryRules")}
                </Button>
              )}
              {(items.some(isFileItem) || !!browserPage?.items.length) && (
                <Button
                  color="primary"
                  onPress={() =>
                    browserPage ? previewBrowserPage(browserPage) : preview(items, selected)
                  }
                >
                  {t("resource.openingRules.browser")}
                </Button>
              )}
              {canLaunch && items.length > 0 ? (
                <Button
                  isDisabled={loading}
                  onPress={() =>
                    selected ? void launch(selected, true) : launchCandidates(items, true)
                  }
                >
                  {t("resource.playOnThisDevice.desktopPlayer")}
                </Button>
              ) : !canLaunch ? (
                <DesktopPlaybackGuideButton />
              ) : null}
            </div>
          )
        )}
      </Modal>
    </>
  );
}
