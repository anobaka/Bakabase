"use client";

import type { IResourceCoverRef } from "@/components/Resource/components/ResourceCover";
import type { Resource } from "@/core/models/Resource";

import { useTranslation } from "react-i18next";
import React, { useState } from "react";
import {
  DeleteOutlined,
  ExportOutlined,
  FireOutlined,
  FolderOpenOutlined,
  LoadingOutlined,
  ProductOutlined,
  PushpinOutlined,
  ReloadOutlined,
  VideoCameraAddOutlined,
} from "@ant-design/icons";
import { AiOutlinePicture } from "react-icons/ai";

import { resolveCoverOperations } from "./coverOperations";

import BApi from "@/sdk/BApi";
import ResourceEnhancementsModal from "@/components/Resource/components/ResourceEnhancementsModal.tsx";
import { openMovePanel } from "@/stores/resourceMovePanel";
import { getKnownMoveBlockReason } from "@/components/Resource/resourceMoveEligibility";
import { useMoveReasonText } from "@/components/ResourceMovePanel/messages";
import DeleteResourceConfirmContent from "@/components/Resource/components/DeleteResourceConfirmContent";
import { EnhancementAdditionalItem } from "@/sdk/constants";
import { PlaylistCollection } from "@/components/Playlist";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import {
  Button,
  Dropdown,
  DropdownTrigger,
  DropdownMenu,
  DropdownItem,
  Modal,
  Tooltip,
  toast,
} from "@/components/bakaui";
import { useUiOptionsStore } from "@/stores/options";
import { useResourceBrowserPlayer } from "@/hooks/useResourceBrowserPlayer";
import { useOpenResourceDirectory } from "@/hooks/useOpenResourceDirectory";

interface IProps {
  resource: Resource;
  sourceTabId?: string;
  sourceTabName?: string;
  coverRef?: IResourceCoverRef;
  reload?: (ct?: AbortSignal) => Promise<any>;
  onResourcesDeleted?: (ids: number[]) => any;
}

const Operations = ({
  resource,
  reload,
  onResourcesDeleted,
  sourceTabId,
  sourceTabName,
}: IProps) => {
  const { t } = useTranslation();
  const moveReasonText = useMoveReasonText();
  const moveBlockReason = getKnownMoveBlockReason(resource);
  const moveDisabledReason = moveBlockReason ? moveReasonText(moveBlockReason) : undefined;
  const { createPortal } = useBakabaseContext();
  const folderAction = useOpenResourceDirectory();
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

  const { showAggregate, directOperations } = resolveCoverOperations(uiOptions?.resource);

  const openBrowserPlayer = useResourceBrowserPlayer();
  const showResourceMediaPlayer = () => openBrowserPlayer(resource);

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
        await BApi.resource.bulkDeleteResources({
          ids: [resource.id],
          deleteFiles,
        });
        // Prune the card from the list. reload?.() only re-fetches this single
        // resource, which now returns nothing, leaving the deleted card visible.
        onResourcesDeleted?.([resource.id]);
      },
    });
  };

  const handleMove = () => {
    if (moveBlockReason) return;
    openMovePanel({
      resources: [{ id: resource.id, path: resource.path, displayName: resource.displayName }],
      sourceTabId,
      sourceTabName,
    });
  };

  const openFolder = () => {
    folderAction.open(resource);
  };

  type Operation = {
    key: string;
    icon: React.ReactNode;
    label: string;
    color?: "warning" | "danger";
    disabledReason?: string;
    disabled?: boolean;
    onAction: () => void;
  };

  // One action catalog drives both surfaces. Shortcut preferences never narrow
  // the aggregate menu; availability and capability checks apply to both.
  const operations: Operation[] = [
    {
      key: "pin",
      icon: <PushpinOutlined />,
      label: resource.pinned
        ? t<string>("resource.operation.unpin")
        : t<string>("resource.operation.pin"),
      color: resource.pinned ? "warning" : undefined,
      onAction: () => {
        BApi.resource.pinResource(resource.id, { pin: !resource.pinned }).then(() => reload?.());
      },
    },
    ...(resource.path
      ? [
          {
            key: "openFolder",
            icon: <FolderOpenOutlined />,
            label: folderAction.label,
            onAction: openFolder,
          },
        ]
      : []),
    {
      key: "enhancements",
      icon: <FireOutlined />,
      label: t<string>("resource.operation.enhancements"),
      onAction: () => {
        BApi.resource
          .getResourceEnhancements(resource.id, {
            additionalItem: EnhancementAdditionalItem.GeneratedPropertyValue,
          })
          .then((response) =>
            createPortal(ResourceEnhancementsModal, {
              resourceId: resource.id,
              // @ts-ignore
              enhancements: response.data || [],
            }),
          );
      },
    },
    ...(resource.path
      ? [
          {
            key: "preview",
            icon: <AiOutlinePicture />,
            label: t<string>("resource.operation.preview"),
            onAction: showResourceMediaPlayer,
          },
        ]
      : []),
    {
      key: "addToPlaylist",
      icon: <VideoCameraAddOutlined />,
      label: t<string>("resource.operation.addToPlaylist"),
      onAction: () =>
        createPortal(Modal, {
          defaultVisible: true,
          title: t<string>("resource.operation.addToPlaylist"),
          children: <PlaylistCollection addingResourceId={resource.id} />,
          style: { minWidth: 600 },
          footer: { actions: ["cancel"] },
        }),
    },
    {
      key: "move",
      icon: <ExportOutlined />,
      label: t<string>("resource.operation.move"),
      disabledReason: moveDisabledReason,
      onAction: handleMove,
    },
    {
      key: "delete",
      icon: <DeleteOutlined />,
      label: t<string>("common.action.delete"),
      color: "danger",
      onAction: handleDelete,
    },
    ...(hasCacheData
      ? [
          {
            key: "refreshCache",
            icon: refreshingCache ? <LoadingOutlined spin /> : <ReloadOutlined />,
            label: t<string>("resource.action.refreshCache"),
            disabled: refreshingCache,
            onAction: handleRefreshCache,
          },
        ]
      : []),
  ];
  const shortcuts = operations.filter((operation) => directOperations.includes(operation.key));
  const actionMap = new Map(operations.map((operation) => [operation.key, operation]));

  if (!showAggregate && shortcuts.length === 0) return null;

  return (
    <div
      className="absolute top-1 right-1 z-10 flex gap-0.5 p-0.5
                 rounded-md bg-black/40 backdrop-blur-sm
                 opacity-0 group-hover/resource:opacity-100 group-focus-within/resource:opacity-100 transition-opacity"
    >
      {shortcuts.map((operation) => (
        <Tooltip key={operation.key} content={operation.disabledReason ?? operation.label}>
          <span className="inline-flex" title={operation.disabledReason}>
            <Button
              isIconOnly
              aria-label={operation.label}
              className="min-w-6 w-6 h-6 bg-transparent hover:bg-white/20 text-sm"
              color={operation.color ?? "default"}
              isDisabled={operation.disabled || !!operation.disabledReason}
              title={operation.disabledReason ?? operation.label}
              onPress={operation.onAction}
            >
              {operation.icon}
            </Button>
          </span>
        </Tooltip>
      ))}
      {showAggregate && (
        <Dropdown>
          <DropdownTrigger>
            <Button
              isIconOnly
              aria-label={t<string>("resource.display.aggregateButton")}
              className="min-w-6 w-6 h-6 bg-transparent hover:bg-white/20"
              size="sm"
              title={t<string>("resource.display.aggregateButton")}
            >
              <ProductOutlined className="text-sm" />
            </Button>
          </DropdownTrigger>
          <DropdownMenu
            aria-label={t<string>("resource.display.operations")}
            onAction={(key) => {
              const operation = actionMap.get(key as string);

              if (operation && !operation.disabled && !operation.disabledReason)
                operation.onAction();
            }}
          >
            {operations.map((operation) => (
              <DropdownItem
                key={operation.key}
                className={operation.color === "danger" ? "text-danger" : undefined}
                classNames={
                  operation.disabledReason
                    ? { description: "max-w-64 whitespace-normal" }
                    : undefined
                }
                description={operation.disabledReason}
                isDisabled={operation.disabled || !!operation.disabledReason}
                startContent={operation.icon}
                title={operation.disabledReason}
              >
                {operation.label}
              </DropdownItem>
            ))}
          </DropdownMenu>
        </Dropdown>
      )}
    </div>
  );
};

Operations.displayName = "Operations";

export default Operations;
