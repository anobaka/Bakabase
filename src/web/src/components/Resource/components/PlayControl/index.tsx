"use client";

import type { Resource as ResourceModel } from "@/core/models/Resource";
import type { DiscoverySource } from "@/hooks/useResourceDiscovery";

import { FolderOutlined, PlayCircleOutlined } from "@ant-design/icons";
import React, { forwardRef, useCallback, useEffect, useImperativeHandle, useState } from "react";
import { useTranslation } from "react-i18next";
import toast from "react-hot-toast";

import { Button, Chip, Modal } from "@/components/bakaui";
import { buildLogger, splitPathIntoSegments, standardizePath } from "@/components/utils";
import BApi from "@/sdk/BApi";
import BusinessConstants from "@/components/BusinessConstants";
import { useUiOptionsStore } from "@/stores/options";
import { usePlayableFilesDiscovery } from "@/hooks/useResourceDiscovery";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

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

export type PlayControlRef = {};

const PlayControl = forwardRef<PlayControlRef, Props>(function PlayControl(
  { resource, PortalComponent, afterPlaying },
  ref,
) {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const log = buildLogger(`PlayControl:${resource.id}|${resource.path}`);
  const uiOptionsStore = useUiOptionsStore();
  const resourceUiOptions = uiOptionsStore.data?.resource;

  // Get cache enabled status from UI options
  const cacheEnabled = !resourceUiOptions?.disableCache;

  // Use SSE-based discovery for playable files
  const discoveryState = usePlayableFilesDiscovery(resource, cacheEnabled);

  const [dirs, setDirs] = useState<Directory[]>();
  const [modalVisible, setModalVisible] = useState(false);

  // Compute playable files context from discovery state
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

    if (portalCtx?.files && portalCtx.files.length > 0) {
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
  }, [resource.id]);

  useImperativeHandle(ref, () => ({}), []);

  const play = (file: string) =>
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

    // If ready, play or show file selection modal
    if (portalCtx?.files && portalCtx.files.length > 0) {
      if (portalCtx.files.length === 1 && !portalCtx.hasMore) {
        play(portalCtx.files[0]!);
      } else if (
        resourceUiOptions?.autoSelectFirstPlayableFile &&
        portalCtx.files.length > 0
      ) {
        play(portalCtx.files[0]!);
      } else {
        if (dirs) {
          setModalVisible(true);
        }
      }
    }
  }, [status, portalCtx, dirs, resourceUiOptions?.autoSelectFirstPlayableFile]);

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
        title={t<string>("Please select a file to play")}
        visible={modalVisible}
        onClose={() => {
          setModalVisible(false);
        }}
      >
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
                                play(file.path);
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
                            {t<string>("Show all {{count}} files", {
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
                  t<string>(
                    "Auto-select first playable file has been enabled. You can change this in the resource page settings.",
                  ),
                );
              }}
            >
              {t<string>("Auto-select first file if multiple playable files exist")}
            </Button>
          </div>
        )}
      </Modal>
    </>
  );
});

PlayControl.displayName = "PlayControl";

export default PlayControl;
