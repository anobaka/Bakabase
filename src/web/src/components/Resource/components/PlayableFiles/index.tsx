"use client";

import type { Resource as ResourceModel } from "@/core/models/Resource";

import { FolderOutlined, PlayCircleOutlined } from "@ant-design/icons";
import React, { forwardRef, useCallback, useEffect, useImperativeHandle, useState } from "react";
import { useTranslation } from "react-i18next";
import toast from "react-hot-toast";

import { Button, Chip, Modal } from "@/components/bakaui";
import { buildLogger, splitPathIntoSegments, standardizePath } from "@/components/utils";
import BApi from "@/sdk/BApi";
import BusinessConstants from "@/components/BusinessConstants";
import { useUiOptionsStore } from "@/stores/options";
import { ResourceCacheType } from "@/sdk/constants.ts";

type Props = {
  resource: ResourceModel;
  PortalComponent: React.FC<{ onClick: () => any }>;
  autoInitialize?: boolean;
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

type PlayableFilesCtx = {
  files: string[];
  hasMore: boolean;
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
    console.log(paths, path, stdPrefix, relativePath, segments);
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

export type PlayableFilesRef = {
  initialize: () => Promise<void>;
};

const PlayableFiles = forwardRef<PlayableFilesRef, Props>(function PlayableFiles(
  { autoInitialize, resource, PortalComponent, afterPlaying },
  ref,
) {
  const { t } = useTranslation();
  const log = buildLogger(`ResourcePlayableFiles:${resource.id}|${resource.path}`);
  const uiOptionsStore = useUiOptionsStore();
  const resourceUiOptions = uiOptionsStore.data?.resource;
  const useCache = !resourceUiOptions?.disableCache;

  const [portalCtx, setPortalCtx] = useState<PlayableFilesCtx>();
  const [dirs, setDirs] = useState<Directory[]>();
  const [modalVisible, setModalVisible] = useState(false);

  const initialize = useCallback(async () => {
    if (useCache) {
      if (resource.cache?.cachedTypes?.includes(ResourceCacheType.PlayableFiles)) {
        log("Set playable files via cache");
        setPortalCtx({
          files: resource.cache?.playableFilePaths ?? [],
          hasMore: resource.cache?.hasMorePlayableFiles ?? false,
        });

        return;
      }
    }
    log("Set playable files via api");
    await BApi.resource.getResourcePlayableFiles(resource.id).then((a) => {
      setPortalCtx({
        files: a.data || [],
        hasMore: false,
      });
      if (!resource.isFile) {
        setDirs(splitIntoDirs(a.data || [], resource.path));
      }
    });
  }, [useCache]);

  useImperativeHandle(
    ref,
    () => ({
      initialize,
    }),
    [initialize],
  );

  useEffect(() => {
    if (autoInitialize) {
      initialize();
    }
  }, []);

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

  if (portalCtx?.files && portalCtx.files.length > 0) {
    return (
      <>
        <PortalComponent
          onClick={() => {
            if (portalCtx.files.length == 1 && !portalCtx.hasMore) {
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
          }}
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
          {!resourceUiOptions?.autoSelectFirstPlayableFile && portalCtx?.files.length > 1 && (
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
  }

  return null;
});

PlayableFiles.displayName = "PlayableFiles";

export default PlayableFiles;
