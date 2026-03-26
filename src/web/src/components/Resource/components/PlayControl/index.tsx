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
import { ResourceSource, ResourceSourceLabel } from "@/sdk/constants";
import { useUiOptionsStore } from "@/stores/options";
import { usePlayableFilesDiscovery } from "@/hooks/useResourceDiscovery";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import envConfig from "@/config/env";

import NoPlayableFilesModal from "./NoPlayableFilesModal";

// Play control status for UI rendering
export type PlayControlStatus = "idle" | "loading" | "ready" | "not-found";

export type SourceEntry = {
  source: ResourceSource;
  items: PlayableItem[];
};

export type PlayControlPortalProps = {
  status: PlayControlStatus;
  /** Ordered list of sources that have playable items */
  sources: SourceEntry[];
  /** Whether the resource has a local file path */
  hasPath: boolean;
  /** Whether the resource is a file (affects open folder behavior) */
  isFile: boolean;
  /** Play items from a specific source */
  onPlaySource: (source: ResourceSource) => void;
  /** Open the resource's folder */
  onOpenFolder: () => void;
  /** Handle click when no playable items found */
  onNotFound: () => void;
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
const playItemApi = async (resourceId: number, source: ResourceSource, key: string) => {
  const endpoint = envConfig.apiEndpoint || "";
  const url = `${endpoint}/resource/${resourceId}/play-item?source=${source}&key=${encodeURIComponent(key)}`;
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

  // Get playable file cache enabled status from UI options
  const cacheEnabled = !resourceUiOptions?.disablePlayableFileCache;

  // Backend resolves playable items with priority:
  // 1. External source items (from SourceLinks)  2. FileSystem cache
  // Only need SSE discovery when backend hasn't resolved (playableItemsReady=false, no items)
  const needsDiscovery = !resource.playableItemsReady && !resource.playableItems?.length;
  const discoveryState = usePlayableFilesDiscovery(resource, cacheEnabled, needsDiscovery);

  const [dirs, setDirs] = useState<Directory[]>();
  const [modalVisible, setModalVisible] = useState(false);
  const [modalSource, setModalSource] = useState<ResourceSource | null>(null);
  const [loadingAllFiles, setLoadingAllFiles] = useState(false);
  const [allFilesLoaded, setAllFilesLoaded] = useState(false);

  // Use backend-resolved items first, fall back to filesystem discovery
  const playableItems = useMemo(() => {
    if (resource.playableItems?.length) return resource.playableItems;
    if (discoveryState.playableFilePaths?.length) {
      return discoveryState.playableFilePaths.map((p) => ({
        source: ResourceSource.FileSystem as ResourceSource,
        key: p,
      }));
    }
    return [];
  }, [resource.playableItems, discoveryState.playableFilePaths]);

  // Group items by source
  const sourceGroups = useMemo(() => {
    const groups = new Map<ResourceSource, PlayableItem[]>();
    for (const item of playableItems) {
      const list = groups.get(item.source) ?? [];
      list.push(item);
      groups.set(item.source, list);
    }
    return groups;
  }, [playableItems]);

  // Ordered source entries (sources with playable items)
  const sources: SourceEntry[] = useMemo(() => {
    return Array.from(sourceGroups.entries()).map(([source, items]) => ({ source, items }));
  }, [sourceGroups]);

  // Compute status for PortalComponent
  const computeStatus = (): PlayControlStatus => {
    // 1. Has data?
    if (playableItems.length > 0) return "ready";
    // 2. Can still discover?
    const hasBackendItems = resource.playableItems?.length;
    if (!hasBackendItems && (discoveryState.status === "loading")) return "loading";
    if (!hasBackendItems && discoveryState.status === "idle") return "idle";
    // 3. Nothing found
    return "not-found";
  };

  const status = computeStatus();

  // Update dirs from filesystem playable files
  useEffect(() => {
    const fsPaths = playableItems
      .filter((i) => i.source === ResourceSource.FileSystem)
      .map((i) => i.key);
    if (fsPaths.length > 0 && !resource.isFile) {
      setDirs(splitIntoDirs(fsPaths, resource.path));
    }
  }, [playableItems, resource.isFile, resource.path]);

  // Reset dirs when resource changes
  useEffect(() => {
    setDirs(undefined);
    setAllFilesLoaded(false);
    setModalSource(null);
  }, [resource.id]);

  useImperativeHandle(ref, () => ({
    triggerDiscovery: discoveryState.trigger,
  }), [discoveryState.trigger]);

  // Play a file via legacy API (FileSystem source)
  const playFile = (file: string) =>
    BApi.resource
      .playResourceFile(resource.id, {
        file,
      })
      .then((a) => {
        if (!a.code) {
          toast.success(t<string>("Opened"));
          afterPlaying?.();
        }
      });

  // Play a PlayableItem via the new multi-source API
  const playItem = async (item: PlayableItem) => {
    if (item.source === ResourceSource.FileSystem) {
      // For FileSystem items, use the existing API for backward compatibility
      return playFile(item.key);
    }

    try {
      const rsp = await playItemApi(resource.id, item.source, item.key);
      if (!rsp.code) {
        toast.success(t<string>("Opened"));
        afterPlaying?.();
      }
    } catch (e) {
      toast.error(String(e));
    }
  };

  /** Play items from a specific source, handling single/multiple items */
  const handlePlaySource = useCallback((source: ResourceSource) => {
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
    if (source === ResourceSource.FileSystem && discoveryState.playableFilePaths?.length && !resource.isFile) {
      const computedDirs = splitIntoDirs(discoveryState.playableFilePaths, resource.path);
      setDirs(computedDirs);
    }
    setModalSource(source);
    setModalVisible(true);
  }, [sourceGroups, resourceUiOptions?.autoSelectFirstPlayableFile, discoveryState.playableFilePaths, resource.path, resource.isFile]);

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
  const isFileSystemModal = modalSource === ResourceSource.FileSystem || (!modalSource && sourceGroups.has(ResourceSource.FileSystem));

  return (
    <>
      <PortalComponent
        status={status}
        sources={sources}
        hasPath={!!resource.path}
        isFile={resource.isFile}
        onPlaySource={handlePlaySource}
        onOpenFolder={handleOpenFolder}
        onNotFound={handleNotFound}
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
          <div className={"flex flex-col gap-2 pb-2"}>
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
                                  playFile(file.path);
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
          <div className={"flex flex-wrap gap-1 pb-2"}>
            {modalItems.map((item) => (
              <Button
                key={`${item.source}-${item.key}`}
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

        {resource.hasMorePlayableFiles && !allFilesLoaded && isFileSystemModal && (
          <div className={"pt-2"}>
            <Button
              color={"primary"}
              size={"sm"}
              variant={"light"}
              isLoading={loadingAllFiles}
              onPress={async () => {
                setLoadingAllFiles(true);
                try {
                  const rsp = await BApi.resource.getResourcePlayableFiles(resource.id);
                  const allFiles = rsp.data ?? [];
                  if (allFiles.length > 0 && !resource.isFile) {
                    setDirs(splitIntoDirs(allFiles, resource.path));
                  }
                  setAllFilesLoaded(true);
                } finally {
                  setLoadingAllFiles(false);
                }
              }}
            >
              {t<string>("resource.playControl.modal.loadAllFiles")}
            </Button>
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
