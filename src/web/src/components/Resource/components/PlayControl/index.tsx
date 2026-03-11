"use client";

import type { PlayableItem, Resource as ResourceModel } from "@/core/models/Resource";
import type { DiscoverySource } from "@/hooks/useResourceDiscovery";

import { FolderOutlined, PlayCircleOutlined } from "@ant-design/icons";
import React, { forwardRef, useCallback, useEffect, useImperativeHandle, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import toast from "react-hot-toast";

import { Button, Chip, Dropdown, DropdownItem, DropdownMenu, DropdownTrigger, Modal } from "@/components/bakaui";
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
export type PlayControlStatus = "loading" | "ready" | "not-found";

export type PlayControlPortalProps = {
  status: PlayControlStatus;
  source: DiscoverySource;
  cacheEnabled: boolean;
  onClick: () => void;
  tooltipContent?: string;
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

export type PlayControlRef = {};

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

  // Use SSE-based discovery for playable files
  const discoveryState = usePlayableFilesDiscovery(resource, cacheEnabled);

  const [dirs, setDirs] = useState<Directory[]>();
  const [modalVisible, setModalVisible] = useState(false);
  const [loadingAllFiles, setLoadingAllFiles] = useState(false);
  const [allFilesLoaded, setAllFilesLoaded] = useState(false);

  // Get playable items from discovery state
  const playableItems = discoveryState.playableItems ?? [];

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

  const hasMultipleSources = sourceGroups.size > 1;

  // Compute playable files context from discovery state (legacy compat for FileSystem)
  const portalCtx = discoveryState.status === "ready"
    ? {
        files: discoveryState.playableFilePaths ?? [],
        hasMore: discoveryState.hasMorePlayableFiles ?? false,
      }
    : null;

  // Compute status for PortalComponent
  const computeStatus = (): PlayControlStatus => {
    if (discoveryState.status === "loading") {
      return "loading";
    }

    if (discoveryState.status === "error") {
      return "not-found";
    }

    // Check if we have any playable items (multi-source) or legacy files
    if (playableItems.length > 0 || (portalCtx?.files && portalCtx.files.length > 0)) {
      return "ready";
    }
    return "not-found";
  };

  // Compute tooltip content based on status and source
  const computeTooltipContent = (): string | undefined => {
    if (discoveryState.status === "loading") {
      if (discoveryState.cacheEnabled) {
        return t("resource.playControl.tooltip.cachePreparing");
      }
      return t("resource.playControl.tooltip.loading");
    }

    if (computeStatus() === "not-found") {
      return t("resource.playControl.tooltip.notFound");
    }

    return undefined;
  };

  const status = computeStatus();
  const tooltipContent = computeTooltipContent();

  // Update dirs when discovery completes
  useEffect(() => {
    if (discoveryState.status === "ready" && discoveryState.playableFilePaths?.length && !resource.isFile) {
      setDirs(splitIntoDirs(discoveryState.playableFilePaths, resource.path));
    }
  }, [discoveryState.status, discoveryState.playableFilePaths, resource.isFile, resource.path]);

  // Reset dirs when resource changes
  useEffect(() => {
    setDirs(undefined);
    setAllFilesLoaded(false);
  }, [resource.id]);

  useImperativeHandle(ref, () => ({}), []);

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

  const handleClick = useCallback(() => {
    // If not found, show the NoPlayableFilesModal
    if (status === "not-found") {
      createPortal(NoPlayableFilesModal, {
        resourceId: resource.id,
      });
      return;
    }

    // If loading, do nothing
    if (status === "loading") {
      return;
    }

    // If there are items from multiple sources and more than 1 non-FileSystem source items,
    // we need to show source selection - but this is handled by the dropdown in render.
    // If only one source or auto-select, play directly.

    if (playableItems.length === 1) {
      playItem(playableItems[0]!);
      return;
    }

    // For single-source FileSystem items, use existing logic
    if (!hasMultipleSources && portalCtx?.files && portalCtx.files.length > 0) {
      if (portalCtx.files.length === 1 && !portalCtx.hasMore) {
        playFile(portalCtx.files[0]!);
      } else if (
        resourceUiOptions?.autoSelectFirstPlayableFile &&
        portalCtx.files.length > 0
      ) {
        playFile(portalCtx.files[0]!);
      } else {
        // Compute dirs synchronously to avoid race condition with useEffect
        if (!resource.isFile && portalCtx.files.length > 0) {
          const computedDirs = splitIntoDirs(portalCtx.files, resource.path);
          setDirs(computedDirs);
        }
        setModalVisible(true);
      }
      return;
    }

    // For multiple sources or non-FileSystem items, show the selection modal
    setModalVisible(true);
  }, [status, portalCtx, playableItems, hasMultipleSources, resource.isFile, resource.path, resourceUiOptions?.autoSelectFirstPlayableFile]);

  return (
    <>
      <PortalComponent
        status={status}
        source={discoveryState.source}
        cacheEnabled={discoveryState.cacheEnabled}
        onClick={handleClick}
        tooltipContent={tooltipContent}
      />
      <Modal
        footer={false}
        size={"lg"}
        title={t<string>("resource.playControl.modal.selectFileTitle")}
        visible={modalVisible}
        onClose={() => {
          setModalVisible(false);
        }}
      >
        {/* Multi-source: show source tabs/sections when multiple sources exist */}
        {hasMultipleSources && (
          <div className={"flex flex-col gap-4 pb-2"}>
            {Array.from(sourceGroups.entries()).map(([source, items]) => (
              <div key={source} className={"flex flex-col gap-2"}>
                <div className={"flex items-center gap-2"}>
                  <Chip radius={"sm"} size={"sm"} color={"primary"} variant={"flat"}>
                    {ResourceSourceLabel[source] ?? `Source ${source}`}
                  </Chip>
                </div>
                <div className={"flex flex-wrap gap-1"}>
                  {source === ResourceSource.FileSystem ? (
                    // For FileSystem items, show directory-grouped file list
                    dirs?.map((d) => (
                      <div key={d.relativePath} className={"w-full"}>
                        {dirs.length > 1 && (
                          <div className={"flex items-center"}>
                            <FolderOutlined className={"text-base"} />
                            <Chip radius={"sm"} size={"sm"} variant={"light"}>
                              {d.relativePath}
                            </Chip>
                          </div>
                        )}
                        <div className={"flex gap-1 flex-wrap"}>
                          {d.groups.map((g) => {
                            const showCount = g.showAll
                              ? g.files.length
                              : Math.min(DefaultVisibleFileCount, g.files.length);
                            return g.files.slice(0, showCount).map((file) => (
                              <Button
                                key={file.path}
                                className={"whitespace-break-spaces py-2 h-auto text-left"}
                                radius={"sm"}
                                size={"sm"}
                                onPress={() => playFile(file.path)}
                              >
                                <PlayCircleOutlined className={"text-base"} />
                                <span className={"break-all overflow-hidden text-ellipsis"}>
                                  {file.name}
                                </span>
                              </Button>
                            ));
                          })}
                        </div>
                      </div>
                    ))
                  ) : (
                    // For non-FileSystem items, show each item as a button
                    items.map((item) => (
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
                    ))
                  )}
                </div>
              </div>
            ))}
          </div>
        )}

        {/* Single source: existing file browser */}
        {!hasMultipleSources && (
          <>
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

              {/* Non-FileSystem single source items */}
              {sourceGroups.size === 1 && !sourceGroups.has(ResourceSource.FileSystem) && (
                <div className={"flex flex-wrap gap-1"}>
                  {playableItems.map((item) => (
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
            </div>
          </>
        )}

        {portalCtx?.hasMore && !allFilesLoaded && (
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
        {!resourceUiOptions?.autoSelectFirstPlayableFile && portalCtx && portalCtx.files.length > 1 && (
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
