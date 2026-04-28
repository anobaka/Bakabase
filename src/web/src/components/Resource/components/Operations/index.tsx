"use client";

import type { IResourceCoverRef } from "@/components/Resource/components/ResourceCover";
import type { Resource } from "@/core/models/Resource";
import type { BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry } from "@/sdk/Api";
import type { MediaType } from "@/sdk/constants";

import { useTranslation } from "react-i18next";
import React, { useMemo, useState } from "react";
import {
  DeleteOutlined,
  FireOutlined,
  FolderOpenOutlined,
  LoadingOutlined,
  ProductOutlined,
  PushpinOutlined,
  ReloadOutlined,
  VideoCameraAddOutlined,
} from "@ant-design/icons";
import { AiOutlinePicture } from "react-icons/ai";

import BApi from "@/sdk/BApi";
import ResourceEnhancementsModal from "@/components/Resource/components/ResourceEnhancementsModal.tsx";
import DeleteResourceConfirmContent from "@/components/Resource/components/DeleteResourceConfirmContent";
import { EnhancementAdditionalItem, IwFsType } from "@/sdk/constants";
import { PlaylistCollection } from "@/components/Playlist";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { Button, Dropdown, DropdownTrigger, DropdownMenu, DropdownItem, Modal, toast } from "@/components/bakaui";
import { useUiOptionsStore } from "@/stores/options";
import MediaPlayer from "@/components/MediaPlayer";

interface IProps {
  resource: Resource;
  coverRef?: IResourceCoverRef;
  reload?: (ct?: AbortSignal) => Promise<any>;
}

const Operations = ({ resource, coverRef, reload }: IProps) => {
  const { t } = useTranslation();
  const { createPortal, createWindow } = useBakabaseContext();
  const uiOptions = useUiOptionsStore((state) => state.data);
  const [refreshingCache, setRefreshingCache] = useState(false);

  const hasCacheData = (resource.dataStates?.length ?? 0) > 0;

  const handleRefreshCache = async () => {
    if (refreshingCache) return;
    setRefreshingCache(true);
    try {
      const rsp = await BApi.cache.refreshResourceCache(resource.id);
      if (!rsp.code) {
        toast.success(t<string>("resource.action.refreshCache.success"));
        await reload?.();
      }
    } finally {
      setRefreshingCache(false);
    }
  };

  // Get displayOperations from options, default to ["aggregate"] for new users
  const displayOperations = useMemo(() => {
    const ops = uiOptions?.resource?.displayOperations;

    // If null/undefined or empty, default to aggregate button
    if (!ops || ops.length === 0) {
      return ["aggregate"];
    }

    return ops;
  }, [uiOptions?.resource?.displayOperations]);

  const showResourceMediaPlayer = () => {
    BApi.file
      .getAllFiles({
        path: resource.path,
      })
      .then((a) => {
        if (!a.code && a.data) {
          if (a.data.length == 0) {
            return toast.default(t<string>("No files to preview"));
          }
          const files = a.data;

          const entries: BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry[] = files.map(
            (path) => {
              const name = path.split(/[/\\]/).pop() || path;
              const ext = name.includes(".") ? name.split(".").pop() : undefined;

              return {
                path: path,
                name: name,
                meaningfulName: name,
                ext: ext,
                type: IwFsType.Unknown,
                passwordsForDecompressing: [],
              };
            },
          );

          createWindow(
            MediaPlayer,
            {
              entries: entries,
              defaultActiveIndex: 0,
              renderOperations: (
                filePath: string,
                mediaType: MediaType,
                playing: boolean,
                reactPlayer: any,
                image: HTMLImageElement | null,
              ): any => {},
            },
            {
              title: resource.displayName,
              persistent: true,
            },
          );
        }
      });
  };

  const handleDelete = () => {
    let deleteFiles = false;

    createPortal(Modal, {
      defaultVisible: true,
      title: t<string>("resource.contextMenu.deleteResource"),
      children: (
        <DeleteResourceConfirmContent
          count={1}
          onDeleteFilesChange={(v) => {
            deleteFiles = v;
          }}
        />
      ),
      footer: {
        actions: ["cancel", "ok"],
        okProps: {
          color: "danger",
          children: t<string>("common.action.delete"),
        },
      },
      onOk: async () => {
        await BApi.resource.deleteResourcesByKeys({
          ids: [resource.id],
          deleteFiles,
        });
        reload?.();
      },
    });
  };

  const showAggregate = displayOperations.includes("aggregate");
  const showPin = displayOperations.includes("pin");
  // Mirrors the right-click "open folder" menu entry — only shown when the
  // resource has a backing local path (see ContextMenuItems/index.tsx).
  const showOpenFolder = displayOperations.includes("openFolder") && !!resource.path;
  const showEnhancements = displayOperations.includes("enhancements");
  const showPreview = displayOperations.includes("preview");
  const showAddToPlaylist = displayOperations.includes("addToPlaylist");
  const showDelete = displayOperations.includes("delete");

  const openFolder = () => {
    BApi.resource.openResourceDirectory({ id: resource.id });
  };

  // Aggregate mode: dropdown with icon + label items
  if (showAggregate) {
    type MenuItem = { key: string; icon: React.ReactNode; label: string; className?: string; onAction: () => void };

    let items: MenuItem[] = [];

    if (showPin) {
      items.push({
        key: "pin",
        icon: <PushpinOutlined />,
        label: resource.pinned ? t<string>("resource.operation.unpin") : t<string>("resource.operation.pin"),
        onAction: () => {
          BApi.resource.pinResource(resource.id, { pin: !resource.pinned }).then(() => reload?.());
        },
      });
    }
    if (showOpenFolder) {
      items.push({
        key: "openFolder",
        icon: <FolderOpenOutlined />,
        label: t<string>("common.action.openFolder"),
        onAction: openFolder,
      });
    }
    if (showEnhancements) {
      items.push({
        key: "enhancements",
        icon: <FireOutlined />,
        label: t<string>("resource.operation.enhancements"),
        onAction: () => {
          BApi.resource
            .getResourceEnhancements(resource.id, {
              additionalItem: EnhancementAdditionalItem.GeneratedPropertyValue,
            })
            .then((resp) => {
              createPortal(ResourceEnhancementsModal, {
                resourceId: resource.id,
                // @ts-ignore
                enhancements: resp.data || [],
              });
            });
        },
      });
    }
    if (showPreview) {
      items.push({
        key: "preview",
        icon: <AiOutlinePicture />,
        label: t<string>("resource.operation.preview"),
        onAction: showResourceMediaPlayer,
      });
    }
    if (showAddToPlaylist) {
      items.push({
        key: "addToPlaylist",
        icon: <VideoCameraAddOutlined />,
        label: t<string>("resource.operation.addToPlaylist"),
        onAction: () => {
          createPortal(Modal, {
            defaultVisible: true,
            title: t<string>("resource.operation.addToPlaylist"),
            children: <PlaylistCollection addingResourceId={resource.id} />,
            style: { minWidth: 600 },
            footer: {
              actions: ["cancel"],
            },
          });
        },
      });
    }
    if (showDelete) {
      items.push({
        key: "delete",
        icon: <DeleteOutlined />,
        label: t<string>("common.action.delete"),
        className: "text-danger",
        onAction: handleDelete,
      });
    }

    // If no individual operations selected, show all (default behavior)
    if (items.length === 0) {
      items = [
        {
          key: "pin",
          icon: <PushpinOutlined />,
          label: resource.pinned ? t<string>("resource.operation.unpin") : t<string>("resource.operation.pin"),
          onAction: () => {
            BApi.resource.pinResource(resource.id, { pin: !resource.pinned }).then(() => reload?.());
          },
        },
        {
          key: "enhancements",
          icon: <FireOutlined />,
          label: t<string>("resource.operation.enhancements"),
          onAction: () => {
            BApi.resource
              .getResourceEnhancements(resource.id, {
                additionalItem: EnhancementAdditionalItem.GeneratedPropertyValue,
              })
              .then((resp) => {
                createPortal(ResourceEnhancementsModal, {
                  resourceId: resource.id,
                  // @ts-ignore
                  enhancements: resp.data || [],
                });
              });
          },
        },
        {
          key: "preview",
          icon: <AiOutlinePicture />,
          label: t<string>("resource.operation.preview"),
          onAction: showResourceMediaPlayer,
        },
        {
          key: "addToPlaylist",
          icon: <VideoCameraAddOutlined />,
          label: t<string>("resource.operation.addToPlaylist"),
          onAction: () => {
            createPortal(Modal, {
              defaultVisible: true,
              title: t<string>("resource.operation.addToPlaylist"),
              children: <PlaylistCollection addingResourceId={resource.id} />,
              style: { minWidth: 600 },
              footer: {
                actions: ["cancel"],
              },
            });
          },
        },
      ];
    }

    // Always add refreshCache if applicable
    if (hasCacheData) {
      items.push({
        key: "refreshCache",
        icon: refreshingCache ? <LoadingOutlined spin /> : <ReloadOutlined />,
        label: t<string>("resource.action.refreshCache"),
        onAction: handleRefreshCache,
      });
    }

    const actionMap = Object.fromEntries(items.map((item) => [item.key, item.onAction]));

    return (
      <Dropdown>
        <DropdownTrigger>
          <Button
            isIconOnly
            className={"absolute top-1 right-1 z-10 opacity-0 group-hover/resource:opacity-100"}
            size={"sm"}
          >
            <ProductOutlined className={"text-lg"} />
          </Button>
        </DropdownTrigger>
        <DropdownMenu
          aria-label="Resource operations"
          onAction={(key) => actionMap[key as string]?.()}
        >
          {items.map((item) => (
            <DropdownItem key={item.key} className={item.className} startContent={item.icon}>
              {item.label}
            </DropdownItem>
          ))}
        </DropdownMenu>
      </Dropdown>
    );
  }

  // Non-aggregate: show individual icon buttons directly on the card
  const individualButtons = [];

  const buttonClassName = "min-w-6 w-6 h-6 bg-transparent hover:bg-white/20";
  const iconClassName = "text-sm";

  if (showPin) {
    individualButtons.push(
      <Button
        key="pin"
        isIconOnly
        className={buttonClassName}
        color={resource.pinned ? "warning" : "default"}
        title={resource.pinned ? t<string>("resource.operation.unpin") : t<string>("resource.operation.pin")}
        onPress={() => {
          BApi.resource.pinResource(resource.id, { pin: !resource.pinned }).then((r) => {
            reload?.();
          });
        }}
      >
        <PushpinOutlined className={iconClassName} />
      </Button>,
    );
  }
  if (showOpenFolder) {
    individualButtons.push(
      <Button
        key="openFolder"
        isIconOnly
        className={buttonClassName}
        title={t<string>("common.action.openFolder")}
        onPress={openFolder}
      >
        <FolderOpenOutlined className={iconClassName} />
      </Button>,
    );
  }
  if (showEnhancements) {
    individualButtons.push(
      <Button
        key="enhancements"
        isIconOnly
        className={buttonClassName}
        title={t<string>("resource.operation.enhancements")}
        onPress={() => {
          BApi.resource
            .getResourceEnhancements(resource.id, {
              additionalItem: EnhancementAdditionalItem.GeneratedPropertyValue,
            })
            .then((t) => {
              createPortal(ResourceEnhancementsModal, {
                resourceId: resource.id,
                // @ts-ignore
                enhancements: t.data || [],
              });
            });
        }}
      >
        <FireOutlined className={iconClassName} />
      </Button>,
    );
  }
  if (showPreview) {
    individualButtons.push(
      <Button
        key="preview"
        isIconOnly
        className={buttonClassName}
        title={t<string>("resource.operation.preview")}
        onPress={showResourceMediaPlayer}
      >
        <AiOutlinePicture className={iconClassName} />
      </Button>,
    );
  }
  if (showAddToPlaylist) {
    individualButtons.push(
      <Button
        key="addToPlaylist"
        isIconOnly
        className={buttonClassName}
        title={t<string>("resource.operation.addToPlaylist")}
        onPress={() => {
          createPortal(Modal, {
            defaultVisible: true,
            title: t<string>("resource.operation.addToPlaylist"),
            children: <PlaylistCollection addingResourceId={resource.id} />,
            style: { minWidth: 600 },
            footer: {
              actions: ["cancel"],
            },
          });
        }}
      >
        <VideoCameraAddOutlined className={iconClassName} />
      </Button>,
    );
  }
  if (showDelete) {
    individualButtons.push(
      <Button
        key="delete"
        isIconOnly
        className={buttonClassName}
        color="danger"
        title={t<string>("common.action.delete")}
        onPress={handleDelete}
      >
        <DeleteOutlined className={iconClassName} />
      </Button>,
    );
  }
  if (hasCacheData) {
    individualButtons.push(
      <Button
        key="refreshCache"
        isIconOnly
        className={buttonClassName}
        title={t<string>("resource.action.refreshCache")}
        isDisabled={refreshingCache}
        onClick={handleRefreshCache}
      >
        {refreshingCache ? <LoadingOutlined className={iconClassName} spin /> : <ReloadOutlined className={iconClassName} />}
      </Button>,
    );
  }

  return (
    <div
      className="absolute top-1 right-1 z-10 flex gap-0.5 p-0.5
                 rounded-md bg-black/40 backdrop-blur-sm
                 opacity-0 group-hover/resource:opacity-100 transition-opacity"
    >
      {individualButtons}
    </div>
  );
};

Operations.displayName = "Operations";

export default Operations;
