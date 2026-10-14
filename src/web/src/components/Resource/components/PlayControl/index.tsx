"use client";

import type { PlayableItem, Resource as ResourceModel } from "@/core/models/Resource";

import { FolderOutlined, PlayCircleOutlined, VideoCameraOutlined } from "@ant-design/icons";
import React, {
  forwardRef,
  useCallback,
  useEffect,
  useImperativeHandle,
  useMemo,
  useState,
} from "react";
import { useTranslation } from "react-i18next";
import toast from "react-hot-toast";

import { isNativePlayerMediaType } from "../PlayOnThisDevice/playerSchemes";

import NoPlayableFilesModal from "./NoPlayableFilesModal";
import { DesktopPlaybackGuide, useDesktopPlaybackGuidanceAvailable } from "./DesktopPlaybackGuide";
import { usePlaybackPreferences } from "./usePlaybackPreferences";
import {
  pathMediaType,
  playbackKindForPath,
  summarizePlayablePaths,
  resolvePlaybackPreference,
  type PlaybackPreference,
} from "./playbackPreferences";
import { originalSourceActions, sourceActionTarget } from "./sourceActions";

import { Button, Chip, Modal } from "@/components/bakaui";
import { splitPathIntoSegments, standardizePath } from "@/components/utils";
import BApi from "@/sdk/BApi";
import BusinessConstants from "@/components/BusinessConstants";
import {
  ClientMode,
  DataOrigin,
  DataStatus,
  ResourceDataType,
  ResourceSource,
} from "@/sdk/constants";
import { useUiOptionsStore } from "@/stores/options";
import { usePlayableItemResolution } from "@/hooks/usePlayableItemResolution";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { useRemoteAccessStore, useUserSideActionsRunHere } from "@/stores/remoteAccess";
import { useSourceResourceProfile } from "@/features/playback/resourceProfiles";
import { useResourceBrowserPlayer } from "@/hooks/useResourceBrowserPlayer";
import PlayOnThisDevice from "@/components/Resource/components/PlayOnThisDevice";
import { preferredDevicePlatform } from "@/components/Resource/components/PlayOnThisDevice/playerSchemes";
import { useOpenResourceDirectory } from "@/hooks/useOpenResourceDirectory";
import { openExternalUrl } from "@/utils/openExternalUrl";
import { isDesktopAppWindow } from "@/utils/clientSurface";

// Play control status for UI rendering
export type PlayControlStatus = "idle" | "loading" | "ready" | "not-found" | "failed";

// FileSystem discovery status
export type FsDiscoveryStatus = "idle" | "loading" | "ready" | "failed";

export type SourceEntry = {
  source: DataOrigin;
  items: PlayableItem[];
};

export type PlayControlPortalProps = {
  openingRules?: { target: string; resourceId: number; label?: string };
  status: PlayControlStatus;
  /** Available sources, ordered for this device's preferred playback method. */
  sources: SourceEntry[];
  /** Whether the resource has a local file path */
  hasPath: boolean;
  /** Whether the resource is a file (affects open folder behavior) */
  isFile: boolean;
  /** FileSystem discovery status (for showing loading on FS button) */
  fsDiscoveryStatus: FsDiscoveryStatus;
  /** Play items from a specific source */
  onPlaySource: (source: DataOrigin) => void;
  sourceActionLabel?: (source: DataOrigin) => string;
  /** Original webpages remain separate from playback and discovery state. */
  originalSourceActions?: { key: string; label: string; onOpen: () => void }[];
  discoveryFailed?: boolean;
  onDiscoveryFailure?: () => void;
  /** Open the resource's folder */
  onOpenFolder: () => void;
  openFolderLabel: string;
  /** Handle click when no playable items found */
  onNotFound: () => void;
  /** Trigger FileSystem SSE discovery (call when FS button becomes visible) */
  triggerFsDiscovery: () => void;
  onPlayInBrowser?: () => void;
  onChoosePlayback?: () => void;
  onDesktopPlaybackGuide?: () => void;
  /** Select a concrete discovered video before choosing its playback method. */
  onPlayVideos?: () => void;
  browserPlaybackAvailable?: boolean;
  browserPlaybackIsDefault?: boolean;
  playbackCaption?: string;
  playbackReady?: boolean;
};

type Props = {
  resource: ResourceModel;
  PortalComponent: React.FC<PlayControlPortalProps>;
  afterPlaying?: () => any;
};

type Directory = {
  relativePath: string;
  groups: Group[];
};

type Group = {
  extension: string;
  files: File[];
  showAll?: boolean;
};

type File = {
  name: string;
  path: string;
};

const splitIntoDirs = (paths: string[], prefix?: string): Directory[] => {
  const groups: Directory[] = [];
  const stdPrefix = standardizePath(prefix) ?? "";

  for (const path of paths) {
    const stdPath = standardizePath(path)!;
    const relativePath = stdPath.replace(stdPrefix, "");
    const segments = splitPathIntoSegments(relativePath);
    const relativeDir = segments
      .slice(0, segments.length - 1)
      .join(BusinessConstants.pathSeparator);
    let dir = groups.find((g) => g.relativePath == relativeDir);

    if (!dir) {
      dir = {
        relativePath: relativeDir,
        groups: [],
      };
      groups.push(dir);
    }
    const extension = segments[segments.length - 1]!.split(".").pop()!;
    let group = dir.groups.find((g) => g.extension == extension);

    if (!group) {
      group = {
        extension,
        files: [],
      };
      dir.groups.push(group);
    }
    group.files.push({
      name: segments[segments.length - 1]!,
      path,
    });
  }

  return groups;
};

const DefaultVisibleFileCount = 5;
const VideoSelectionPageSize = 50;

/**
 * Ask whichever machine is meant to play this to play it.
 *
 * Through the SDK rather than a bare fetch, because the answer is no longer
 * always the server's: a managed server's relay intercepts this endpoint, and
 * its refusals — the library is not mapped on this machine, this build cannot
 * do it yet — arrive as ordinary error responses. A raw fetch swallowed
 * them into an opaque failure.
 */
const playItemApi = async (
  resourceId: number,
  origin: DataOrigin,
  key: string,
  explicitChoice = false,
) =>
  await BApi.resource.playResourceItem(resourceId, {
    origin,
    key,
    ...(explicitChoice ? { explicitChoice: true } : {}),
  });

export type PlayControlRef = {
  triggerDiscovery: () => void;
};

const PlayControl = forwardRef<PlayControlRef, Props>(function PlayControl(
  { resource, PortalComponent, afterPlaying },
  ref,
) {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const uiOptionsStore = useUiOptionsStore();
  const userSideActionsRunHere = useUserSideActionsRunHere();
  const appWindow = isDesktopAppWindow();
  const desktopGuidanceAvailable = useDesktopPlaybackGuidanceAvailable();
  const [desktopGuidanceVisible, setDesktopGuidanceVisible] = useState(false);
  const openInBrowserPlayer = useResourceBrowserPlayer();
  const folderAction = useOpenResourceDirectory();
  const resourceUiOptions = uiOptionsStore.data?.resource;
  const playback = usePlaybackPreferences();
  const currentServerId = useRemoteAccessStore((state) => state.serverId);
  const currentServerName = useRemoteAccessStore((state) => state.serverName);
  const managing = useRemoteAccessStore((state) => state.clientMode === ClientMode.PureClient);
  const openingTarget =
    currentServerId && (managing || !userSideActionsRunHere)
      ? `server:${currentServerId}`
      : undefined;
  const opening = useSourceResourceProfile(openingTarget, resource.id);

  // Use the playable item resolution hook which handles SSE discovery internally
  const resolution = usePlayableItemResolution(resource);

  const [dirs, setDirs] = useState<Directory[]>();
  const [modalVisible, setModalVisible] = useState(false);
  const [discoveryFailureVisible, setDiscoveryFailureVisible] = useState(false);
  const [openingFailureVisible, setOpeningFailureVisible] = useState(false);
  const [retryingOpening, setRetryingOpening] = useState(false);
  const [modalSource, setModalSource] = useState<DataOrigin | null>(null);
  const [modalPlaybackOverride, setModalPlaybackOverride] = useState<PlaybackPreference>();
  const [videoSelectionVisible, setVideoSelectionVisible] = useState(false);
  const [visibleVideoCount, setVisibleVideoCount] = useState(VideoSelectionPageSize);

  const effectiveGroups = useMemo(
    () =>
      resolution.groups
        .filter((group) => [DataOrigin.FileSystem, DataOrigin.Steam].includes(group.origin))
        .map((group) => ({
          ...group,
          items:
            group.origin === DataOrigin.FileSystem
              ? group.status === DataStatus.Ready
                ? group.items.filter(
                    (item) => typeof item.key === "string" && item.key.trim().length > 0,
                  )
                : []
              : group.items,
        })),
    [resolution.groups],
  );

  // Map resolution groups to SourceEntry[] for the PortalComponent
  const sources: SourceEntry[] = useMemo(
    () =>
      effectiveGroups
        .filter((g) => g.items.length > 0)
        .map((g) => ({ source: g.origin, items: g.items }))
        .sort((a, b) =>
          !userSideActionsRunHere
            ? Number(b.source === DataOrigin.FileSystem) -
              Number(a.source === DataOrigin.FileSystem)
            : 0,
        ),
    [effectiveGroups, userSideActionsRunHere],
  );

  // Build sourceGroups map for handlePlaySource
  const sourceGroups = useMemo(() => {
    const groups = new Map<DataOrigin, PlayableItem[]>();

    for (const g of effectiveGroups) {
      if (g.items.length > 0) {
        groups.set(g.origin, g.items);
      }
    }

    return groups;
  }, [effectiveGroups]);

  // All playable items from all groups
  const playableItems = useMemo(() => effectiveGroups.flatMap((g) => g.items), [effectiveGroups]);
  const fsItems = useMemo(
    () => playableItems.filter((item) => item.origin === DataOrigin.FileSystem),
    [playableItems],
  );
  const collection = useMemo(
    () =>
      summarizePlayablePaths(
        fsItems.length > 0
          ? fsItems.map((item) => item.key)
          : resource.isFile && resource.path
            ? [resource.path]
            : [],
      ),
    [fsItems, resource.isFile, resource.path],
  );
  const resourceKind = collection.kind;
  const representativePath = fsItems[0]?.key ?? resource.path;
  const preferredPlayback = resolvePlaybackPreference(
    opening.preference(resourceKind, playback.preferences[resourceKind]),
    userSideActionsRunHere,
    collection.mediaType,
    preferredDevicePlatform(),
    collection.isMixed,
    appWindow,
  );
  const playbackCaption = opening.unavailable
    ? t("resource.playback.chooseMethod")
    : preferredPlayback.mode === "browser"
      ? t(
          resourceKind === "audio" || resourceKind === "video"
            ? "resource.playOnThisDevice.playInBrowser"
            : "resource.playOnThisDevice.viewInBrowser",
        )
      : preferredPlayback.mode === "ask"
        ? t("resource.playback.chooseMethod")
        : preferredPlayback.scheme
          ? t("resource.playback.usePlayer", { player: preferredPlayback.scheme.name })
          : t("resource.playOnThisDevice.desktopPlayer");
  const orderedSources = [...sources].sort((a, b) =>
    preferredPlayback.mode === "browser" || preferredPlayback.mode === "ask"
      ? Number(b.source === DataOrigin.FileSystem) - Number(a.source === DataOrigin.FileSystem)
      : 0,
  );

  // FileSystem discovery status for the portal
  const fsDiscoveryStatus: FsDiscoveryStatus = useMemo(() => {
    const fsGroup = resolution.groups.find((g) => g.origin === DataOrigin.FileSystem);

    if (!fsGroup) {
      // Check if there's a FS dataState at all
      const hasFsState = resource.dataStates?.some(
        (s) => s.dataType === ResourceDataType.PlayableItem && s.origin === DataOrigin.FileSystem,
      );

      if (!hasFsState) return "ready";

      return "idle";
    }
    if (fsGroup.status === DataStatus.Failed) return "failed";
    if (fsGroup.status === DataStatus.Ready) return "ready";
    if (fsGroup.status === DataStatus.NotStarted) return "loading";

    return "ready";
  }, [resolution.groups, resource.dataStates]);

  // Map overall status
  const status: PlayControlStatus = useMemo(() => {
    if (effectiveGroups.some((group) => group.items.length > 0)) return "ready";
    if (effectiveGroups.some((group) => group.status === DataStatus.NotStarted)) return "loading";
    if (effectiveGroups.some((group) => group.status === DataStatus.Failed)) return "failed";

    return fsDiscoveryStatus === "idle" ? "idle" : "not-found";
  }, [effectiveGroups, fsDiscoveryStatus]);

  // Update dirs from filesystem playable files
  useEffect(() => {
    const fsPaths = playableItems
      .filter((i) => i.origin === DataOrigin.FileSystem)
      .map((i) => i.key);

    if (fsPaths.length > 0 && !resource.isFile) {
      setDirs(splitIntoDirs(fsPaths, resource.path));
    }
  }, [playableItems, resource.isFile, resource.path]);

  // Reset dirs when resource changes
  useEffect(() => {
    setDirs(undefined);
    setModalSource(null);
    setModalPlaybackOverride(undefined);
    setVideoSelectionVisible(false);
    setDiscoveryFailureVisible(false);
    setOpeningFailureVisible(false);
  }, [resource.id]);

  useImperativeHandle(
    ref,
    () => ({
      triggerDiscovery: () => resolution.triggerDiscovery(DataOrigin.FileSystem),
    }),
    [resolution.triggerDiscovery],
  );

  // Play a PlayableItem via unified PlayItem API (all sources go through resolvers)
  const playNativeItem = async (item: PlayableItem, explicitChoice = false) => {
    try {
      const rsp = await playItemApi(resource.id, item.origin, item.key, explicitChoice);

      if (!rsp.code) {
        if (item.origin === DataOrigin.Steam)
          toast(t<string>("resource.playbackActions.steamRequested"));
        else toast.success(t<string>("Opened"));
        afterPlaying?.();
      } else if (item.origin === DataOrigin.FileSystem) {
        choosePlayback(item, t("resource.playbackPolicy.nativeFailed"));
      }
    } catch (e) {
      if (item.origin === DataOrigin.FileSystem)
        choosePlayback(item, t("resource.playbackPolicy.nativeFailed"));
      else toast.error(String(e));
    }
  };

  const playInBrowser = (initialPath?: string) => {
    void openInBrowserPlayer(resource, initialPath, {
      onOpenAlternative: (path) => choosePlayback({ origin: DataOrigin.FileSystem, key: path }),
      canOpenAlternative: (path) =>
        userSideActionsRunHere || isNativePlayerMediaType(pathMediaType(path)),
    })
      .then(() => afterPlaying?.())
      .catch((e) => toast.error(String(e)));
  };

  const choosePlayback = (item?: PlayableItem, unavailableReason?: string) => {
    const selectedPath = item?.key ?? representativePath;

    if (!selectedPath) return;
    createPortal(PlayOnThisDevice, {
      resource,
      filePath: selectedPath,
      kind: item ? playbackKindForPath(selectedPath) : resourceKind,
      isMixed: !item && collection.isMixed,
      unavailableReason,
      offerDesktopGuidance: fsDiscoveryStatus === "ready" && (!!item || fsItems.length > 0),
      preferenceScope: appWindow && userSideActionsRunHere ? "device" : "browser",
      onPlayInBrowser: () => playInBrowser(item?.key),
      onPlayVideos:
        !item && videoItems.length > 0 && fsDiscoveryStatus === "ready"
          ? handlePlayVideos
          : undefined,
      onPlayNative: userSideActionsRunHere
        ? () =>
            item
              ? void playNativeItem(item, true)
              : handlePlaySource(DataOrigin.FileSystem, { mode: "native" })
        : undefined,
      onRememberChoice: playback.save,
    });
  };

  const videoItems = useMemo(() => {
    const fsItemsByPath = new Map(fsItems.map((item) => [item.key, item]));

    return collection.videoPaths.map((path) => fsItemsByPath.get(path)!).filter(Boolean);
  }, [fsItems, collection.videoPaths]);
  const handlePlayVideos = () => {
    if (!playback.ready || fsDiscoveryStatus !== "ready") return;
    if (videoItems.length === 1) choosePlayback(videoItems[0]);
    else if (videoItems.length > 1) {
      setVisibleVideoCount(VideoSelectionPageSize);
      setVideoSelectionVisible(true);
    }
  };

  const playItem = async (item: PlayableItem, override?: PlaybackPreference) => {
    if (item.origin === DataOrigin.Steam) {
      const action = sourceActionTarget(ResourceSource.Steam, item.key);

      if (!action) {
        toast.error(t<string>("resource.playbackActions.invalidSource"));

        return;
      }
      if (!appWindow || !userSideActionsRunHere) {
        window.open(action.href, "_self");
        toast(t<string>("resource.playbackActions.steamRequested"));

        return;
      }
    }
    if (item.origin === DataOrigin.FileSystem) {
      const kind = playbackKindForPath(item.key);
      const preference =
        override && override.mode !== "automatic"
          ? override
          : opening.preference(kind, override ?? playback.preferences[kind]);
      const method = resolvePlaybackPreference(
        preference,
        userSideActionsRunHere,
        pathMediaType(item.key),
        preferredDevicePlatform(),
        false,
        appWindow,
      );

      if (method.mode === "browser") {
        playInBrowser(item.key);

        return;
      }
      if (method.mode === "ask") {
        choosePlayback(
          item,
          preference.mode === "native"
            ? t("resource.playbackPolicy.preferredUnavailable")
            : undefined,
        );

        return;
      }
      if (method.scheme) {
        const streamUrl = new URL(
          BApi.file.getRawFileUrl({ fullname: item.key }),
          window.location.origin,
        ).href;

        BApi.resource.markResourceAsPlayed(resource.id, { item: item.key }).catch(() => {});
        window.location.href = method.scheme.build(
          streamUrl,
          resource.displayName ?? item.key,
          pathMediaType(item.key),
        );

        return;
      }
    }
    // Files execute on the viewing device only when its native bridge is available.
    if (!userSideActionsRunHere) {
      if (item.origin === DataOrigin.FileSystem) {
        // Browser playback is one option among several: for anything the browser
        // cannot demux, handing the file to a native player on this device is
        // both better quality and free for the server.
        choosePlayback(item);
      } else {
        toast.error(t<string>("resource.play.hostOnly"));
      }

      return;
    }

    await playNativeItem(
      item,
      item.origin === DataOrigin.FileSystem &&
        (override && override.mode !== "automatic"
          ? override
          : opening.preference(
              playbackKindForPath(item.key),
              override ?? playback.preferences[playbackKindForPath(item.key)],
            )
        ).mode === "native",
    );
  };

  /** Play items from a specific source, handling single/multiple items */
  const handlePlaySource = useCallback(
    (source: DataOrigin, override?: PlaybackPreference) => {
      if (source === DataOrigin.FileSystem) {
        if (!override && opening.unavailable) {
          setOpeningFailureVisible(true);

          return;
        }
        if (!playback.ready || (!override && !opening.ready)) return;
      }
      const items = sourceGroups.get(source) ?? [];

      if (items.length === 0) return;
      if (source === DataOrigin.FileSystem && !override && preferredPlayback.mode === "browser") {
        playInBrowser(items.length === 1 ? items[0]?.key : undefined);

        return;
      }
      if (source === DataOrigin.FileSystem && !override && preferredPlayback.mode === "ask") {
        choosePlayback(
          undefined,
          opening.preference(resourceKind, playback.preferences[resourceKind]).mode === "native"
            ? t("resource.playbackPolicy.preferredUnavailable")
            : undefined,
        );

        return;
      }
      const selectedPreference =
        source === DataOrigin.FileSystem
          ? (override ?? opening.preference(resourceKind, playback.preferences[resourceKind]))
          : undefined;

      if (items.length === 1) {
        playItem(items[0]!, selectedPreference);

        return;
      }

      // Multiple items - check auto-select preference
      if (resourceUiOptions?.autoSelectFirstPlayableFile) {
        playItem(items[0]!, selectedPreference);

        return;
      }

      // Show modal filtered to this source
      if (source === DataOrigin.FileSystem && !resource.isFile) {
        const fsItems = sourceGroups.get(DataOrigin.FileSystem) ?? [];

        if (fsItems.length > 0) {
          const fsPaths = fsItems.map((i) => i.key);
          const computedDirs = splitIntoDirs(fsPaths, resource.path);

          setDirs(computedDirs);
        }
      }
      setModalSource(source);
      setModalPlaybackOverride(selectedPreference);
      setModalVisible(true);
    },
    [
      sourceGroups,
      resourceUiOptions?.autoSelectFirstPlayableFile,
      resource.path,
      resource.isFile,
      playback.ready,
      preferredPlayback,
      playback.preferences,
      resourceKind,
      userSideActionsRunHere,
      collection.isMixed,
      appWindow,
      opening.snapshot,
      opening.ready,
      opening.unavailable,
    ],
  );

  /** Open the resource's folder */
  const handleOpenFolder = useCallback(() => {
    folderAction.open(resource);
  }, [resource, folderAction.open]);

  /** Handle click when no playable files found */
  const handleNotFound = useCallback(() => {
    if (effectiveGroups.some((group) => group.status === DataStatus.Failed)) {
      setDiscoveryFailureVisible(true);

      return;
    }
    createPortal(NoPlayableFilesModal, {
      resourceId: resource.id,
      hasPath: !!resource.path,
    });
  }, [resource.id, resource.path, effectiveGroups]);

  const failedGroups = effectiveGroups.filter((group) => group.status === DataStatus.Failed);
  const sourceActionLabel = (source: DataOrigin) =>
    source === DataOrigin.Steam ? t("resource.playbackActions.openSteam") : playbackCaption;
  const originalActions = originalSourceActions(resource.sourceLinks).map(({ source, href }) => ({
    key: href,
    label: t("resource.playbackActions.openOriginal", { source: ResourceSource[source] }),
    onOpen: () => openExternalUrl(href, !appWindow),
  }));

  // Get items to display in modal (filtered by selected source or all)
  const modalItems = modalSource ? (sourceGroups.get(modalSource) ?? []) : playableItems;
  const isFileSystemModal =
    modalSource === DataOrigin.FileSystem ||
    (!modalSource && sourceGroups.has(DataOrigin.FileSystem));

  return (
    <>
      {desktopGuidanceVisible && (
        <DesktopPlaybackGuide onClose={() => setDesktopGuidanceVisible(false)} />
      )}
      <PortalComponent
        browserPlaybackAvailable={!!resource.path}
        browserPlaybackIsDefault={!opening.unavailable && preferredPlayback.mode === "browser"}
        discoveryFailed={failedGroups.length > 0}
        fsDiscoveryStatus={fsDiscoveryStatus}
        hasPath={!!resource.path}
        isFile={resource.isFile}
        openFolderLabel={folderAction.label}
        openingRules={
          openingTarget
            ? { target: openingTarget, resourceId: resource.id, label: currentServerName }
            : undefined
        }
        originalSourceActions={originalActions}
        playbackCaption={playbackCaption}
        playbackReady={playback.ready}
        sourceActionLabel={sourceActionLabel}
        sources={orderedSources}
        status={status}
        triggerFsDiscovery={() => resolution.triggerDiscovery(DataOrigin.FileSystem)}
        onChoosePlayback={() => choosePlayback()}
        onDesktopPlaybackGuide={
          desktopGuidanceAvailable && fsItems.length > 0 && fsDiscoveryStatus === "ready"
            ? () => setDesktopGuidanceVisible(true)
            : undefined
        }
        onDiscoveryFailure={() => setDiscoveryFailureVisible(true)}
        onNotFound={handleNotFound}
        onOpenFolder={handleOpenFolder}
        onPlayInBrowser={() => playInBrowser()}
        onPlaySource={handlePlaySource}
        onPlayVideos={
          videoItems.length > 0 && fsDiscoveryStatus === "ready" ? handlePlayVideos : undefined
        }
      />
      <Modal
        footer={false}
        size="sm"
        title={t("resource.playback.chooseMethod")}
        visible={openingFailureVisible}
        onClose={() => setOpeningFailureVisible(false)}
      >
        <p className="mb-3 text-sm text-warning" role="alert">
          {t("resource.playbackPolicy.rulesUnavailable")}
        </p>
        <div className="flex flex-wrap gap-2">
          <Button
            isLoading={retryingOpening}
            onPress={async () => {
              setRetryingOpening(true);
              await opening.reload();
              setRetryingOpening(false);
              setOpeningFailureVisible(false);
            }}
          >
            {t("resource.playbackPolicy.retryRules")}
          </Button>
          <Button
            onPress={() => {
              setOpeningFailureVisible(false);
              playInBrowser();
            }}
          >
            {t("resource.openingRules.browser")}
          </Button>
          <Button
            onPress={() => {
              setOpeningFailureVisible(false);
              choosePlayback(undefined, t("resource.playbackPolicy.rulesUnavailable"));
            }}
          >
            {t("resource.playback.chooseMethod")}
          </Button>
        </div>
      </Modal>
      <Modal
        footer={false}
        size="md"
        title={t("resource.playbackActions.discoveryFailed")}
        visible={discoveryFailureVisible}
        onClose={() => setDiscoveryFailureVisible(false)}
      >
        <p className="text-sm text-foreground-500">
          {t("resource.playbackActions.discoveryFailureHint")}
        </p>
        <div className="my-3 flex max-h-52 flex-col gap-2 overflow-y-auto">
          {failedGroups.map((group) => (
            <div key={group.origin} className="rounded-lg bg-warning-50 p-3 text-sm">
              <div className="font-medium">{t(`resource.source.${DataOrigin[group.origin]}`)}</div>
              <p className="mt-1 whitespace-pre-wrap break-words text-foreground-500">
                {group.error || t("resource.playbackActions.discoveryUnknownError")}
              </p>
            </div>
          ))}
        </div>
        <Button
          color="primary"
          onPress={() => {
            failedGroups.forEach((group) => resolution.triggerDiscovery(group.origin));
            setDiscoveryFailureVisible(false);
          }}
        >
          {t("resource.playbackActions.retryDiscovery")}
        </Button>
      </Modal>
      <Modal
        footer={false}
        size="md"
        title={t("resource.playback.selectVideo")}
        visible={videoSelectionVisible}
        onClose={() => setVideoSelectionVisible(false)}
      >
        <p className="pb-2 text-xs text-foreground-500">
          {t("resource.playback.videoCount", { count: videoItems.length })}
        </p>
        <div className="flex max-h-[50vh] flex-col gap-1 overflow-y-auto">
          {videoItems.slice(0, visibleVideoCount).map((item) => (
            <Button
              key={item.key}
              fullWidth
              className="h-auto justify-start gap-2 px-2 py-2 text-left"
              size="sm"
              variant="flat"
              onPress={() => {
                setVideoSelectionVisible(false);
                choosePlayback(item);
              }}
            >
              <VideoCameraOutlined className="shrink-0 text-base" />
              <span className="min-w-0 break-all">
                {item.displayName ?? item.key.split(/[/\\]/).pop()}
              </span>
            </Button>
          ))}
          {visibleVideoCount < videoItems.length && (
            <Button
              size="sm"
              variant="light"
              onPress={() => setVisibleVideoCount((count) => count + VideoSelectionPageSize)}
            >
              {t("resource.playback.loadMoreVideos")}
            </Button>
          )}
        </div>
      </Modal>
      <Modal
        footer={false}
        size={"lg"}
        title={t<string>(
          isFileSystemModal
            ? "resource.playControl.modal.selectFileTitle"
            : "resource.playbackActions.selectSource",
        )}
        visible={modalVisible}
        onClose={() => {
          setModalVisible(false);
          setModalSource(null);
          setModalPlaybackOverride(undefined);
        }}
      >
        {/* FileSystem source: directory-grouped file list */}
        {isFileSystemModal && (
          <div className={"flex flex-col gap-2 pb-2 overflow-y-auto max-h-[60vh]"}>
            {dirs?.map((d) => {
              return (
                <div key={d.relativePath}>
                  {dirs.length > 1 && (
                    <div className={"flex items-center"}>
                      <FolderOutlined className={"text-base"} />
                      <Chip radius={"sm"} size={"sm"} variant={"light"}>
                        {d.relativePath}
                      </Chip>
                    </div>
                  )}
                  <div className={"flex gap-1 flex-col"}>
                    {d.groups.map((g) => {
                      const showCount = g.showAll
                        ? g.files.length
                        : Math.min(DefaultVisibleFileCount, g.files.length);

                      return (
                        <div key={g.extension} className={"flex flex-wrap items-center gap-1"}>
                          {d.groups.length > 1 && (
                            <Chip radius={"sm"} size={"sm"} variant={"flat"}>
                              {g.extension}
                            </Chip>
                          )}
                          {g.files.slice(0, showCount).map((file) => {
                            return (
                              <Button
                                key={file.path}
                                className={"whitespace-break-spaces py-2 h-auto text-left"}
                                radius={"sm"}
                                size={"sm"}
                                onPress={() => {
                                  playItem(
                                    { origin: DataOrigin.FileSystem, key: file.path },
                                    modalPlaybackOverride,
                                  );
                                  setModalVisible(false);
                                }}
                              >
                                <PlayCircleOutlined className={"text-base"} />
                                <span className={"break-all overflow-hidden text-ellipsis"}>
                                  {file.name}
                                </span>
                              </Button>
                            );
                          })}
                          {g.files.length > DefaultVisibleFileCount && !g.showAll && (
                            <Button
                              color={"primary"}
                              size={"sm"}
                              variant={"light"}
                              onPress={() => {
                                g.showAll = true;
                                setDirs([...dirs]);
                              }}
                            >
                              {t<string>("resource.playControl.modal.showAllFiles", {
                                count: g.files.length,
                              })}
                            </Button>
                          )}
                        </div>
                      );
                    })}
                  </div>
                </div>
              );
            })}
          </div>
        )}

        {/* Non-FileSystem source items */}
        {!isFileSystemModal && (
          <div className={"flex flex-wrap gap-1 pb-2 overflow-y-auto max-h-[60vh]"}>
            {modalItems.map((item) => (
              <Button
                key={`${item.origin}-${item.key}`}
                className={"whitespace-break-spaces py-2 h-auto text-left"}
                radius={"sm"}
                size={"sm"}
                onPress={() => playItem(item)}
              >
                <PlayCircleOutlined className={"text-base"} />
                <span className={"break-all overflow-hidden text-ellipsis"}>
                  {item.displayName ?? item.key}
                </span>
              </Button>
            ))}
          </div>
        )}

        {!resourceUiOptions?.autoSelectFirstPlayableFile && modalItems.length > 1 && (
          <div className={"pt-3 border-t mt-3"}>
            <Button
              color={"primary"}
              size={"sm"}
              variant={"flat"}
              onPress={async () => {
                await uiOptionsStore.patch({
                  resource: {
                    ...resourceUiOptions,
                    autoSelectFirstPlayableFile: true,
                  },
                });
                setModalVisible(false);
                toast.success(t<string>("resource.playControl.toast.autoSelectEnabled"));
              }}
            >
              {t<string>("resource.playControl.modal.autoSelectFirstFile")}
            </Button>
          </div>
        )}
      </Modal>
    </>
  );
});

PlayControl.displayName = "PlayControl";

export default PlayControl;
