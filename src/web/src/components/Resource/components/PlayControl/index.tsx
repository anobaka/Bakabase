"use client";

import type { PlayableItem, Resource as ResourceModel } from "@/core/models/Resource";

import { FolderOutlined, PlayCircleOutlined } from "@ant-design/icons";
import React, { forwardRef, useCallback, useEffect, useImperativeHandle, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import toast from "react-hot-toast";

import { Button, Chip, Modal } from "@/components/bakaui";
import { splitPathIntoSegments, standardizePath } from "@/components/utils";
import BApi from "@/sdk/BApi";
import BusinessConstants from "@/components/BusinessConstants";
import { DataOrigin, DataStatus, ResourceDataType } from "@/sdk/constants";
import { useUiOptionsStore } from "@/stores/options";
import { usePlayableItemResolution } from "@/hooks/usePlayableItemResolution";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import envConfig from "@/config/env";

import NoPlayableFilesModal from "./NoPlayableFilesModal";

// Play control status for UI rendering
export type PlayControlStatus = "idle" | "loading" | "ready" | "not-found";

// FileSystem discovery status
export type FsDiscoveryStatus = "idle" | "loading" | "ready";

export type SourceEntry = {
  source: DataOrigin;
  items: PlayableItem[];
};

export type PlayControlPortalProps = {
  status: PlayControlStatus;
  /** Ordered list of sources that have playable items (non-FileSystem first) */
  sources: SourceEntry[];
  /** Whether the resource has a local file path */
  hasPath: boolean;
  /** Whether the resource is a file (affects open folder behavior) */
  isFile: boolean;
  /** FileSystem discovery status (for showing loading on FS button) */
  fsDiscoveryStatus: FsDiscoveryStatus;
  /** Play items from a specific source */
  onPlaySource: (source: DataOrigin) => void;
  /** Open the resource's folder */
  onOpenFolder: () => void;
  /** Handle click when no playable items found */
  onNotFound: () => void;
  /** Trigger FileSystem SSE discovery (call when FS button becomes visible) */
  triggerFsDiscovery: () => void;
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

const splitIntoDirs = (paths: string[], prefix: string): Directory[] => {
  const groups: Directory[] = [];
  const stdPrefix = standardizePath(prefix)!;

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

/** Call the PlayItem API endpoint. */
const playItemApi = async (resourceId: number, origin: DataOrigin, key: string) => {
  const endpoint = envConfig.apiEndpoint || "";
  const url = `${endpoint}/resource/${resourceId}/play-item?origin=${origin}&key=${encodeURIComponent(key)}`;
  const rsp = await fetch(url);
  return rsp.json();
};

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
  const resourceUiOptions = uiOptionsStore.data?.resource;

  // Use the playable item resolution hook which handles SSE discovery internally
  const resolution = usePlayableItemResolution(resource);

  const [dirs, setDirs] = useState<Directory[]>();
  const [modalVisible, setModalVisible] = useState(false);
  const [modalSource, setModalSource] = useState<DataOrigin | null>(null);

  // Map resolution groups to SourceEntry[] for the PortalComponent
  const sources: SourceEntry[] = useMemo(() =>
    resolution.groups
      .filter(g => g.items.length > 0)
      .map(g => ({ source: g.origin, items: g.items })),
    [resolution.groups]
  );

  // Build sourceGroups map for handlePlaySource
  const sourceGroups = useMemo(() => {
    const groups = new Map<DataOrigin, PlayableItem[]>();
    for (const g of resolution.groups) {
      if (g.items.length > 0) {
        groups.set(g.origin, g.items);
      }
    }
    return groups;
  }, [resolution.groups]);

  // All playable items from all groups
  const playableItems = useMemo(() =>
    resolution.groups.flatMap(g => g.items),
    [resolution.groups]
  );

  // FileSystem discovery status for the portal
  const fsDiscoveryStatus: FsDiscoveryStatus = useMemo(() => {
    const fsGroup = resolution.groups.find(g => g.origin === DataOrigin.FileSystem);
    if (!fsGroup) {
      // Check if there's a FS dataState at all
      const hasFsState = resource.dataStates?.some(
        s => s.dataType === ResourceDataType.PlayableItem && s.origin === DataOrigin.FileSystem
      );
      if (!hasFsState) return "ready";
      return "idle";
    }
    if (fsGroup.status === DataStatus.Ready) return "ready";
    if (fsGroup.status === DataStatus.NotStarted) return "loading";
    return "ready";
  }, [resolution.groups, resource.dataStates]);

  // Map overall status
  const status: PlayControlStatus = useMemo(() => {
    switch (resolution.overallStatus) {
      case "ready": return "ready";
      case "loading": return "loading";
      case "not-found": return fsDiscoveryStatus === "idle" ? "idle" : "not-found";
    }
  }, [resolution.overallStatus, fsDiscoveryStatus]);

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
  }, [resource.id]);

  useImperativeHandle(ref, () => ({
    triggerDiscovery: () => resolution.triggerDiscovery(DataOrigin.FileSystem),
  }), [resolution.triggerDiscovery]);

  // Play a PlayableItem via unified PlayItem API (all sources go through resolvers)
  const playItem = async (item: PlayableItem) => {
    try {
      const rsp = await playItemApi(resource.id, item.origin, item.key);
      if (!rsp.code) {
        toast.success(t<string>("Opened"));
        afterPlaying?.();
      }
    } catch (e) {
      toast.error(String(e));
    }
  };

  /** Play items from a specific source, handling single/multiple items */
  const handlePlaySource = useCallback((source: DataOrigin) => {
    const items = sourceGroups.get(source) ?? [];
    if (items.length === 0) return;

    if (items.length === 1) {
      playItem(items[0]!);
      return;
    }

    // Multiple items - check auto-select preference
    if (resourceUiOptions?.autoSelectFirstPlayableFile) {
      playItem(items[0]!);
      return;
    }

    // Show modal filtered to this source
    if (source === DataOrigin.FileSystem && !resource.isFile) {
      const fsItems = sourceGroups.get(DataOrigin.FileSystem) ?? [];
      if (fsItems.length > 0) {
        const fsPaths = fsItems.map(i => i.key);
        const computedDirs = splitIntoDirs(fsPaths, resource.path);
        setDirs(computedDirs);
      }
    }
    setModalSource(source);
    setModalVisible(true);
  }, [sourceGroups, resourceUiOptions?.autoSelectFirstPlayableFile, resource.path, resource.isFile]);

  /** Open the resource's folder */
  const handleOpenFolder = useCallback(() => {
    BApi.tool.openFileOrDirectory({
      path: resource.path,
      openInDirectory: resource.isFile,
    });
  }, [resource.path, resource.isFile]);

  /** Handle click when no playable files found */
  const handleNotFound = useCallback(() => {
    createPortal(NoPlayableFilesModal, {
      resourceId: resource.id,
    });
  }, [resource.id]);

  // Get items to display in modal (filtered by selected source or all)
  const modalItems = modalSource ? (sourceGroups.get(modalSource) ?? []) : playableItems;
  const isFileSystemModal = modalSource === DataOrigin.FileSystem || (!modalSource && sourceGroups.has(DataOrigin.FileSystem));

  return (
    <>
      <PortalComponent
        status={status}
        sources={sources}
        hasPath={!!resource.path}
        isFile={resource.isFile}
        fsDiscoveryStatus={fsDiscoveryStatus}
        onPlaySource={handlePlaySource}
        onOpenFolder={handleOpenFolder}
        onNotFound={handleNotFound}
        triggerFsDiscovery={() => resolution.triggerDiscovery(DataOrigin.FileSystem)}
      />
      <Modal
        footer={false}
        size={"lg"}
        title={t<string>("resource.playControl.modal.selectFileTitle")}
        visible={modalVisible}
        onClose={() => {
          setModalVisible(false);
          setModalSource(null);
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
                                  playItem({ origin: DataOrigin.FileSystem, key: file.path });
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
                toast.success(
                  t<string>("resource.playControl.toast.autoSelectEnabled"),
                );
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
