"use client";

import type { CSSProperties } from "react";
import type { IResourceCoverRef } from "@/components/Resource/components/ResourceCover";
import type SimpleSearchEngine from "@/core/models/SimpleSearchEngine";
import type {
  Property,
  Resource as ResourceModel,
  PropertyValueScopePreference,
} from "@/core/models/Resource";
import type { TagValue } from "@/components/StandardValue/models";
import type { PlayControlRef } from "@/components/Resource/components/PlayControl";
import type { PropertyType } from "@/sdk/constants";
import type { OwnerResourceSelection } from "@/features/management/resourceSelection";
import type { ManagementSource } from "@/features/management/api";

import React, { useCallback, useImperativeHandle, useMemo, useReducer, useRef } from "react";
import { useTranslation } from "react-i18next";
import {
  ApartmentOutlined,
  CheckCircleFilled,
  HistoryOutlined,
  PushpinOutlined,
  StopOutlined,
} from "@ant-design/icons";
import { AiOutlineCloudDownload, AiOutlineFolderOpen } from "react-icons/ai";
import moment from "moment";

import StandardValueRenderer from "../StandardValue/ValueRenderer";

import { convertFromApiValue } from "@/components/StandardValue/helpers";
import { resolveScopedValue } from "@/core/models/Resource";

import "./index.css";

import ResourceDetailModal from "@/components/Resource/components/DetailModal";
import HealthScoreDiagnosisModal from "@/components/Resource/components/HealthScoreDiagnosisModal";
import HealthScoreBadge from "@/components/HealthScoreBadge";
import {
  ResourceOwnerProvider,
  useResourceApi,
  useResourceOwner,
} from "@/features/management/resourceContext";
import ResourceCover from "@/components/Resource/components/ResourceCover";
import { useOwnerResourceDisplayOptions } from "@/features/management/useOwnerResourceDisplayOptions";
import Operations from "@/components/Resource/components/Operations";
import { getKnownMoveBlockReason } from "@/components/Resource/resourceMoveEligibility";
import { useMoveReasonText } from "@/components/ResourceMovePanel/messages";
import AcquisitionModal from "@/components/Resource/components/AcquisitionModal";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { Button, Chip, Link, Spinner, Tooltip } from "@/components/bakaui";
import { selectResourceMovingTask, useBTasksStore } from "@/stores/bTasks";
import {
  openMovePanel,
  selectPanelResourceReservation,
  useResourceMovePanelStore,
} from "@/stores/resourceMovePanel";
import { BTaskStopButton } from "@/components/BTask";
import {
  PropertyPool,
  PropertyValueScope,
  propertyValueScopes,
  ResourceAdditionalItem,
  ResourceProperty,
  ResourceSource,
  ResourceStatus,
  ResourceTag,
  BTaskStatus,
  StandardValueType,
} from "@/sdk/constants";
import { useResourceOptionsStore, useUiOptionsStore } from "@/stores/options";
import PlayControl from "@/components/Resource/components/PlayControl";
import { ResourceCardPlaybackButton as PlayButton } from "@/components/Resource/components/PlayControl/PlaybackButtons";
import ResourceContextMenu from "@/components/Resource/components/ContextMenu";
import ResourceSourceIcon from "@/components/Resource/components/ResourceSourceIcon";
import { autoBackgroundColor } from "@/components/utils"; // adjust the path as needed

export interface IResourceHandler {
  id: number;
  reload: (ct?: AbortSignal) => Promise<any>;
  select: (selected: boolean) => void;
}

type TooltipPlacement =
  | "top"
  | "bottom"
  | "right"
  | "left"
  | "top-start"
  | "top-end"
  | "bottom-start"
  | "bottom-end"
  | "left-start"
  | "left-end"
  | "right-start"
  | "right-end";

type Props = {
  resource: ResourceModel;
  sourceTabId?: string;
  sourceTabName?: string;
  biggerCoverPlacement?: TooltipPlacement;
  searchEngines?: SimpleSearchEngine[] | null;
  ct?: AbortSignal;
  onTagClick?: (propertyId: number, value: TagValue) => any;
  style?: any;
  className?: string;
  selected?: boolean;
  selectionModeRef?: React.RefObject<boolean>;
  onSelected?: (id: number, shiftKey?: boolean) => any;
  /**
   * Prefer the *Ref variants when this component lives inside a virtualized
   * grid: stable ref objects keep React.memo effective so flipping selection
   * on one card doesn't re-render every visible card.
   */
  selectedResourceIds?: number[];
  selectedResources?: ResourceModel[];
  selectedResourceIdsRef?: React.RefObject<number[]>;
  selectedResourcesRef?: React.RefObject<ResourceModel[]>;
  selectedResourceGroups?: OwnerResourceSelection[];
  onSelectedResourceGroupChanged?: (owner: ManagementSource | undefined, ids: number[]) => any;
  onResourceGroupDeleted?: (owner: ManagementSource | undefined, ids: number[]) => any;
  onSelectedResourcesChanged?: (ids: number[]) => any;
  /** Called after resources are deleted so the parent can prune them from the list. */
  onResourcesDeleted?: (ids: number[]) => any;
  debug?: boolean;
  /** Disable cover click to prevent opening DetailModal */
  disableCoverClick?: boolean;
};

const Resource = React.forwardRef((props: Props, ref) => {
  const BApi = useResourceApi();
  const owner = useResourceOwner();
  const ownerDisplayOptions = useOwnerResourceDisplayOptions();
  const isRemoteOwner = !!owner && !owner.isLocal;
  const {
    resource,
    sourceTabId,
    sourceTabName,
    onTagClick = (propertyId: number, value: TagValue) => {},
    ct = new AbortController().signal,
    biggerCoverPlacement,
    style: propStyle = {},
    selected = false,
    selectionModeRef,
    onSelected = (id: number, shiftKey?: boolean) => {},
    selectedResourceIds: propsSelectedResourceIds,
    selectedResources: propsSelectedResources,
    selectedResourceIdsRef,
    selectedResourcesRef,
    onSelectedResourcesChanged,
    onResourcesDeleted,
    debug,
    disableCoverClick = false,
  } = props;

  const { createPortal } = useBakabaseContext();

  const { t } = useTranslation();

  // Active MoveResources task covering this resource — while present, every interaction on
  // the card is disabled and a busy overlay is shown. Stable undefined for uncovered cards,
  // so the virtualized grid doesn't re-render them on task pushes.
  const moveReasonText = useMoveReasonText();
  const moveBlockReason = getKnownMoveBlockReason(resource);
  const movePanelEnabled = useResourceMovePanelStore(
    (state) => !isRemoteOwner && state.panelEnabled,
  );
  const movingTask = useBTasksStore((state) =>
    isRemoteOwner ? undefined : selectResourceMovingTask(resource.id)(state),
  );
  const moveReservation = useResourceMovePanelStore((state) =>
    isRemoteOwner ? undefined : selectPanelResourceReservation(resource.id, resource.path)(state),
  );
  const moveLocked = !!movingTask || !!moveReservation;
  const moveStatus =
    moveReservation?.phase === "preview"
      ? t<string>("resourceMove.panel.preparing", "Preparing move")
      : movingTask?.status === BTaskStatus.WaitingForInput
        ? t<string>("resourceMove.panel.waitingForInput", "Needs attention")
        : movingTask?.status === BTaskStatus.Cancelling
          ? t<string>("resourceMove.panel.stopping", "Finishing current resource before stopping")
          : movingTask?.status === BTaskStatus.NotStarted
            ? t<string>("resourceMove.panel.queued", "Queued")
            : t<string>("resourceMove.status.moving");

  // Narrow selectors: any change to the *full* options object would otherwise
  // re-render every visible ResourceCard. Subscribing to specific fields
  // ensures only the cards whose displayed field changed will re-render.
  const displayProperties = useUiOptionsStore((s) => s.data?.resource?.displayProperties);
  const coverFit = useUiOptionsStore((s) => s.data?.resource?.coverFit);
  const disableCoverCarousel = useUiOptionsStore((s) => s.data?.resource?.disableCoverCarousel);
  const disableMediaPreviewer = useUiOptionsStore((s) => s.data?.resource?.disableMediaPreviewer);
  const showBiggerCoverWhileHover = useUiOptionsStore(
    (s) => s.data?.resource?.showBiggerCoverWhileHover,
  );
  const displayResourceId = useUiOptionsStore((s) => s.data?.resource?.displayResourceId);
  const hideHealthScore = useUiOptionsStore((s) => s.data?.resource?.hideHealthScore);
  const inlineDisplayName = useUiOptionsStore((s) => s.data?.resource?.inlineDisplayName);
  const hideResourceBorder = useUiOptionsStore((s) => s.data?.resource?.hideResourceBorder);
  const propertyValueScopePriority = useResourceOptionsStore(
    (s) => s.data?.propertyValueScopePriority,
  );

  const valueScopePriority = useMemo<PropertyValueScope[]>(() => {
    const configured = isRemoteOwner
      ? ownerDisplayOptions.data?.propertyValueScopePriority
      : propertyValueScopePriority;
    const vsp =
      configured && configured.length > 0
        ? configured.slice()
        : propertyValueScopes.map((s) => s.value);

    for (const scope of propertyValueScopes) {
      if (!vsp.includes(scope.value)) {
        if (scope.value == PropertyValueScope.Manual) {
          vsp.splice(0, 0, scope.value);
        } else {
          vsp.push(scope.value);
        }
      }
    }

    return vsp;
  }, [propertyValueScopePriority, isRemoteOwner, ownerDisplayOptions.data]);

  const scopePreferenceMap = useMemo(() => {
    const map = new Map<string, PropertyValueScopePreference>();

    for (const p of resource.scopePreferences ?? []) {
      map.set(`${p.propertyPool}-${p.propertyId}`, p);
    }

    return map;
  }, [resource.scopePreferences]);

  // Use useReducer for stable forceUpdate reference
  const [, forceUpdate] = useReducer((x) => x + 1, 0);

  useImperativeHandle(ref, (): IResourceHandler => {
    return {
      id: resource.id,
      reload: reload,
      select: (selected: boolean) => {},
    };
  }, []);

  const displayPropertyKeys = isRemoteOwner
    ? (ownerDisplayOptions.data?.uiResource?.displayProperties ?? [])
    : (displayProperties ?? []);

  // Discovery happens automatically via SSE now

  const coverRef = useRef<IResourceCoverRef>();
  const playControlRef = useRef<PlayControlRef>(null);

  // Keep a ref to the latest resource to avoid stale closure in reload
  const resourceRef = useRef(resource);

  resourceRef.current = resource;

  const reload = useCallback(
    async (ct?: AbortSignal) => {
      const currentResource = resourceRef.current;
      const newResourceRsp = await BApi.resource.getResourcesByKeys({
        ids: [currentResource.id],
        additionalItems: ResourceAdditionalItem.All,
      });

      if (!newResourceRsp.code) {
        const nr = (newResourceRsp.data || [])[0];

        if (nr) {
          Object.keys(nr).forEach((k) => {
            currentResource[k] = nr[k];
          });
          coverRef.current?.reload();
          forceUpdate();
        }
      } else {
        throw new Error(newResourceRsp.message!);
      }
    },
    [forceUpdate],
  );

  const onCoverClick = useCallback(() => {
    if (disableCoverClick) return;
    createPortal(ResourceDetailModal, {
      id: resource.id,
      initialResource: resource,
      onDestroyed: () => {
        reload();
      },
    });
  }, [resource, disableCoverClick]);

  const renderCover = () => {
    const elementId = `resource-${owner?.serverId ?? "local"}-${resource.id}`;

    return (
      <div
        className="resource-cover-rectangle w-full max-w-full min-w-full pb-[100%] relative rounded group/cover @container [container-type:inline-size]"
        id={elementId}
        onMouseEnter={() => playControlRef.current?.triggerDiscovery()}
      >
        <div className="absolute inset-0">
          <ResourceCover
            ref={coverRef}
            biggerCoverPlacement={biggerCoverPlacement}
            coverFit={coverFit}
            disableCarousel={disableCoverCarousel}
            disableMediaPreviewer={disableMediaPreviewer}
            resource={resource}
            showBiggerOnHover={showBiggerCoverWhileHover}
            onClick={onCoverClick}
          />
        </div>
        {/* lef-top */}
        <div className={"absolute top-1 left-1 right-1 flex gap-1 items-center flex-wrap"}>
          {movePanelEnabled && moveBlockReason && (
            <Tooltip content={moveReasonText(moveBlockReason)}>
              <span
                aria-label={moveReasonText(moveBlockReason)}
                className="inline-flex items-center justify-center w-6 h-6 rounded bg-default-100/90 text-warning"
                title={moveReasonText(moveBlockReason)}
              >
                <StopOutlined />
              </span>
            </Tooltip>
          )}
          {!resource.hasLocalPath && (
            <Tooltip content={t<string>("resource.tip.notMaterialized")}>
              <Chip color="primary" radius={"sm"} size={"sm"} variant={"flat"}>
                {t<string>("resource.label.notMaterialized")}
              </Chip>
            </Tooltip>
          )}
          {resource.tags.includes(ResourceTag.Pinned) && <PushpinOutlined />}
          {resource.tags.includes(ResourceTag.IsParent) && (
            <Tooltip content={t<string>("resource.tip.isParentResource")}>
              <ApartmentOutlined className={""} />
            </Tooltip>
          )}
          {resource.status === ResourceStatus.Absent && (
            <Chip color="danger" radius={"sm"} size={"sm"} variant={"flat"}>
              {t("enum.resourceStatus.absent")}
            </Chip>
          )}
          {resource.status === ResourceStatus.Unavailable && (
            <Chip color="warning" radius={"sm"} size={"sm"} variant={"flat"}>
              {t("enum.resourceStatus.unavailable")}
            </Chip>
          )}
          {resource.playedAt && (
            <Chip
              className="whitespace-break-spaces h-auto"
              radius={"sm"}
              size={"sm"}
              variant={"flat"}
            >
              <div className={"flex items-center gap-1"}>
                <HistoryOutlined />
                {(() => {
                  const playedMoment = moment(resource.playedAt);
                  const now = moment();
                  const diffMinutes = now.diff(playedMoment, "minutes");
                  const diffHours = now.diff(playedMoment, "hours");
                  const diffDays = now.diff(playedMoment, "days");

                  if (diffMinutes < 1) {
                    return t("resource.label.playedJustNow");
                  } else if (diffMinutes < 60) {
                    return t("resource.label.playedMinutesAgo", { n: diffMinutes });
                  } else if (diffHours < 24) {
                    return t("resource.label.playedHoursAgo", { n: diffHours });
                  } else {
                    return t("resource.label.playedDaysAgo", { n: diffDays });
                  }
                })()}
              </div>
            </Chip>
          )}
          {displayResourceId && (
            <Tooltip content={t<string>("resource.label.resourceId")}>
              <Chip radius={"sm"} size={"sm"} variant={"flat"}>
                {resource.id}
              </Chip>
            </Tooltip>
          )}
          {resource.healthScore != null && !hideHealthScore && (
            <Tooltip
              content={t<string>("healthScore.label.scoreTip", { score: resource.healthScore })}
            >
              <HealthScoreBadge
                score={resource.healthScore}
                onClick={(e) => {
                  e.stopPropagation();
                  createPortal(HealthScoreDiagnosisModal, { resourceId: resource.id });
                }}
              />
            </Tooltip>
          )}
        </div>
        {(displayPropertyKeys.length > 0 || inlineDisplayName) && (
          <div
            className={
              "inline-flex flex-col gap-1 absolute bottom-0 right-0 items-end max-w-full max-h-full w-fit overflow-hidden"
            }
          >
            {displayPropertyKeys.flatMap((dpk) => {
              const style: CSSProperties = {};

              let bizValue: any | undefined;
              let bizValueType: StandardValueType | undefined;
              let propertyType: PropertyType | undefined;

              try {
                switch (dpk.pool) {
                  case PropertyPool.Internal:
                    switch (dpk.id) {
                      case ResourceProperty.CollectionMulti:
                        // One chip per collection. Rule members are in here too — the server does
                        // not distinguish, because a member is a member.
                        if (resource.collections && resource.collections.length > 0) {
                          return resource.collections.map((c) => {
                            const cStyle: CSSProperties = {};

                            if (c.color) {
                              cStyle.color = c.color;
                              cStyle.backgroundColor = autoBackgroundColor(c.color);
                            }

                            return (
                              <Chip
                                key={`${dpk.pool}-${dpk.id}-${c.id}`}
                                className={"h-auto w-fit resource-display-property-chip"}
                                radius={"sm"}
                                size={"sm"}
                                style={cStyle}
                                variant={"flat"}
                              >
                                <StandardValueRenderer
                                  type={StandardValueType.String}
                                  value={c.name}
                                  variant="light"
                                />
                              </Chip>
                            );
                          });
                        }

                        return [];
                      case ResourceProperty.MediaLibraryV2:
                      case ResourceProperty.MediaLibraryV2Multi:
                        // Render multiple chips for multiple media libraries
                        if (resource.mediaLibraries && resource.mediaLibraries.length > 0) {
                          return resource.mediaLibraries.map((ml) => {
                            const mlStyle: CSSProperties = {};

                            if (ml.color) {
                              mlStyle.color = ml.color;
                              mlStyle.backgroundColor = autoBackgroundColor(ml.color);
                            }

                            return (
                              <Chip
                                key={`${dpk.pool}-${dpk.id}-${ml.id}`}
                                className={"h-auto w-fit resource-display-property-chip"}
                                radius={"sm"}
                                size={"sm"}
                                style={mlStyle}
                                variant={"flat"}
                              >
                                <StandardValueRenderer
                                  type={StandardValueType.String}
                                  value={ml.name}
                                  variant="light"
                                />
                              </Chip>
                            );
                          });
                        }
                        // Fallback to legacy single value
                        if (resource.mediaLibraryColor) {
                          style.color = resource.mediaLibraryColor;
                          style.backgroundColor = autoBackgroundColor(resource.mediaLibraryColor);
                        }
                        bizValue = resource.mediaLibraryName;
                        bizValueType = StandardValueType.String;
                        break;
                      case ResourceProperty.CreatedAt:
                        bizValue = moment(resource.createdAt).format("YYYY-MM-DD");
                        bizValueType = StandardValueType.String;
                        break;
                      case ResourceProperty.PlayedAt:
                        if (resource.playedAt) {
                          bizValue = resource.playedAt;
                          bizValueType = StandardValueType.String;
                        }
                        break;
                      case ResourceProperty.FileCreatedAt:
                        bizValue = moment(resource.fileCreatedAt).format("YYYY-MM-DD");
                        bizValueType = StandardValueType.String;
                        break;
                      case ResourceProperty.FileModifiedAt:
                        bizValue = moment(resource.fileModifiedAt).format("YYYY-MM-DD");
                        bizValueType = StandardValueType.String;
                        break;
                    }
                    break;
                  case PropertyPool.Reserved:
                  case PropertyPool.Custom: {
                    const property = resource.properties?.[dpk.pool as PropertyPool]?.[dpk.id];
                    const selectedValue = resolveScopedValue(
                      property?.values,
                      valueScopePriority,
                      scopePreferenceMap.get(`${dpk.pool}-${dpk.id}`),
                      property?.profileScopePriority,
                    );
                    const rawBizValue =
                      selectedValue?.aliasAppliedBizValue ?? selectedValue?.bizValue;

                    bizValueType = property?.bizValueType;
                    propertyType = property?.type;
                    bizValue =
                      bizValueType != undefined
                        ? convertFromApiValue(rawBizValue, bizValueType)
                        : rawBizValue;
                    break;
                  }
                }
              } catch (error) {
                console.warn(`[Resource:${resource.id}] error rendering display property`, error);
              }

              if (bizValue == undefined || bizValueType == undefined) {
                return [];
              }

              return [
                <Chip
                  key={`${dpk.pool}-${dpk.id}`}
                  className={"h-auto w-fit resource-display-property-chip"}
                  radius={"sm"}
                  size={"sm"}
                  style={style}
                  variant={"flat"}
                >
                  <StandardValueRenderer
                    propertyType={propertyType}
                    type={bizValueType}
                    value={bizValue}
                    variant="light"
                  />
                </Chip>,
              ];
            })}
            {inlineDisplayName && renderDisplayNameAndTags(true)}
          </div>
        )}
        {(() => {
          const externalSources = [
            ...new Set(
              resource.sourceLinks
                ?.map((l) => l.source)
                .filter((s) => s !== ResourceSource.PathMark) ?? [],
            ),
          ];

          if (externalSources.length === 0) return null;

          return (
            <div className="absolute right-1 bottom-1 z-[1] flex items-end gap-0.5 pointer-events-none">
              {externalSources.map((source) => (
                <div
                  key={source}
                  className="flex items-center justify-center w-6 h-6 rounded-md bg-black/60 text-white text-sm [&_img]:h-3.5"
                >
                  <ResourceSourceIcon source={source} />
                </div>
              ))}
            </div>
          );
        })()}
        {resource.hasLocalPath ? (
          <PlayControl
            ref={playControlRef}
            PortalComponent={PlayButton}
            afterPlaying={reload}
            resource={resource}
          />
        ) : (
          // There is nothing to play. What the user wants here is the way to get the files, so
          // the play button's place is taken by the acquisition entry point.
          <div className="hidden group-hover/cover:flex group-focus-within/cover:flex has-[[data-bakabase-tooltip-open]]:flex absolute left-0 bottom-0 z-[1]">
            <Tooltip
              content={t<string>(
                resource.sourceLinks?.length
                  ? "resource.tip.acquire"
                  : "resource.tip.linkLocalFolder",
              )}
            >
              <Button
                isIconOnly
                className={"!p-0"}
                onPress={() =>
                  createPortal(AcquisitionModal, { resource, onChanged: () => reload() })
                }
              >
                {resource.sourceLinks?.length ? (
                  <AiOutlineCloudDownload className={"text-2xl"} />
                ) : (
                  <AiOutlineFolderOpen className={"text-2xl"} />
                )}
              </Button>
            </Tooltip>
          </div>
        )}
      </div>
    );
  };

  let firstTagsValue: TagValue[] | undefined;
  let firstTagsValuePropertyId: number | undefined;

  {
    const customPropertyValues = resource.properties?.[PropertyPool.Custom] || {};

    Object.keys(customPropertyValues).find((idStr) => {
      const id = parseInt(idStr, 10);
      const p: Property = customPropertyValues[id];

      if (p.bizValueType == StandardValueType.ListTag) {
        const selectedValue = resolveScopedValue(
          p.values,
          valueScopePriority,
          scopePreferenceMap.get(`${PropertyPool.Custom}-${id}`),
          p.profileScopePriority,
        );
        const tags = (selectedValue?.aliasAppliedBizValue ?? selectedValue?.bizValue) as
          | TagValue[]
          | undefined;

        if (
          tags &&
          tags.length > 0 &&
          displayPropertyKeys.some((dpk) => dpk.pool == PropertyPool.Custom && dpk.id == id)
        ) {
          firstTagsValue = tags.map((tag) => ({
            value: tag,
            ...tag,
          }));
          firstTagsValuePropertyId = id;

          return true;
        }
      }

      return false;
    });
  }

  const style: CSSProperties = {
    ...propStyle,
  };

  // Prefer ref-based selection state when provided (hot path inside the
  // virtualized grid); fall back to the array props for other callers.
  const resolvedSelectedResourceIds = selectedResourceIdsRef
    ? (selectedResourceIdsRef.current ?? [])
    : (propsSelectedResourceIds ?? []);
  const resolvedSelectedResources = selectedResourcesRef
    ? (selectedResourcesRef.current ?? [])
    : (propsSelectedResources ?? []);

  const selectedResourceIds = resolvedSelectedResourceIds.slice();

  if (!selectedResourceIds.includes(resource.id)) {
    selectedResourceIds.push(resource.id);
  }

  const selectedResources = (() => {
    if (resolvedSelectedResources.some((r) => r.id === resource.id)) {
      return resolvedSelectedResources;
    }

    return [...resolvedSelectedResources, resource];
  })();

  const renderDisplayNameAndTags = (highContrastBackground: boolean = false) => {
    // inline 模式下 (highContrastBackground=true) 父元素是 w-fit，不能用 container queries
    // 非 inline 模式下可以用 container queries 实现字体随容器缩放
    return (
      <div
        className={`rounded text-right ${highContrastBackground ? "" : "[container-type:inline-size]"}`}
      >
        <div
          className={`${highContrastBackground ? "bg-default/70 backdrop-blur-sm text-default-700 px-1.5 py-0.5 rounded-md text-xs" : "mt-1 text-[clamp(11px,5cqw,22px)]"}`}
        >
          <div className="select-text resource-limited-content">{resource.displayName}</div>
        </div>
        {firstTagsValue && firstTagsValue.length > 0 && (
          <div
            className={`${highContrastBackground ? "bg-default/70 backdrop-blur-sm text-default-700 px-1.5 py-0.5 rounded-md mt-1 text-xs" : "mt-1 text-[clamp(11px,5cqw,22px)]"}`}
          >
            <div className="select-text resource-limited-content flex flex-wrap opacity-70 leading-3 gap-px">
              {firstTagsValue.map((v) => {
                return (
                  <Link
                    key={`${v.group}:${v.name}`}
                    className={"text-xs cursor-pointer"}
                    color={"foreground"}
                    underline={"none"}
                    onPress={(e) => {
                      onTagClick?.(firstTagsValuePropertyId!, v);
                    }}
                    size={"sm"}
                    // variant={'light'}
                  >
                    #{v.group == undefined ? "" : `${v.group}:`}
                    {v.name}
                  </Link>
                );
              })}
            </div>
          </div>
        )}
      </div>
    );
  };

  return (
    <div
      key={resource.id}
      className={`flex flex-col p-1 rounded relative group/resource resource ${props.className} ${
        hideResourceBorder ? "" : "border-2"
      } ${
        selected
          ? "border-primary ring-2 ring-primary/30 bg-primary/5"
          : hideResourceBorder
            ? ""
            : "border-default-200"
      } ${
        resource.status === ResourceStatus.Absent
          ? "opacity-50 grayscale"
          : resource.status === ResourceStatus.Unavailable
            ? "opacity-60"
            : ""
      }`}
      data-id={resource.id}
      data-resource-owner={owner?.serverId}
      style={style}
      onClickCapture={(e) => {
        if (moveLocked) {
          // The overlay's own controls (the stop button) must stay clickable.
          if ((e.target as HTMLElement).closest?.("[data-moving-overlay]")) {
            return;
          }
          e.preventDefault();
          e.stopPropagation();

          return;
        }
        if (selectionModeRef?.current || e.shiftKey) {
          onSelected(resource.id, e.shiftKey);
          e.preventDefault();
          e.stopPropagation();
        }
      }}
    >
      {selected && (
        <div className="absolute top-2 right-2 z-20">
          <CheckCircleFilled className="text-primary text-xl drop-shadow-md" />
        </div>
      )}
      {!moveLocked && (
        <Operations
          coverRef={coverRef.current}
          reload={reload}
          resource={resource}
          sourceTabId={sourceTabId}
          sourceTabName={sourceTabName}
          onResourcesDeleted={onResourcesDeleted}
        />
      )}
      {moveLocked && (
        <div
          data-moving-overlay
          className="absolute inset-0 z-30 rounded bg-white/30 backdrop-blur-[2px] cursor-not-allowed group/moving"
          onContextMenu={(e) => {
            e.preventDefault();
            e.stopPropagation();
          }}
        >
          <div
            className="absolute left-0 top-0 h-full transition-[width] duration-300 ease-out"
            style={{
              width: `${movingTask?.percentage ?? 0}%`,
              background:
                "linear-gradient(90deg, rgba(105, 226, 248, 0.15), rgba(105, 226, 248, 0.3))",
            }}
          />
          <div className="absolute inset-0 flex flex-col items-center justify-center gap-1">
            <div className="flex items-center gap-1 bg-[var(--theme-body-background)] px-3 py-1 rounded-md text-xs font-medium shadow-md">
              <Spinner size="sm" />
              {moveStatus}
              &nbsp;
              {movingTask?.percentage != null && `${movingTask.percentage}%`}
            </div>
            <div className="opacity-0 group-hover/moving:opacity-100 transition-opacity">
              {movingTask && movingTask.status !== BTaskStatus.Cancelling && (
                <BTaskStopButton color={"warning"} id={movingTask.id} size={"small"} />
              )}
              {!movingTask && moveReservation?.phase === "task" && (
                <Button
                  size="sm"
                  onPress={() => openMovePanel({ resources: [], sourceTabId, sourceTabName })}
                >
                  {t<string>("resourceMove.panel.open", "Move panel")}
                </Button>
              )}
            </div>
          </div>
        </div>
      )}
      <ResourceContextMenu
        contextResource={resource}
        disabled={moveLocked}
        moveResourceIds={
          resolvedSelectedResourceIds.includes(resource.id)
            ? resolvedSelectedResourceIds
            : [resource.id]
        }
        selectedResourceIds={selectedResourceIds}
        selectedResourceGroups={props.selectedResourceGroups}
        onSelectedResourceGroupChanged={props.onSelectedResourceGroupChanged}
        onResourceGroupDeleted={props.onResourceGroupDeleted}
        selectedResources={selectedResources}
        sourceTabId={sourceTabId}
        sourceTabName={sourceTabName}
        onResourcesDeleted={onResourcesDeleted}
        onSelectedResourcesChanged={onSelectedResourcesChanged}
      >
        <div className="relative">
          {renderCover()}
          {!inlineDisplayName && renderDisplayNameAndTags(false)}
        </div>
      </ResourceContextMenu>
    </div>
  );
});
const ResourceWithOwner = React.forwardRef((props: Props, ref) => {
  const inheritedOwner = useResourceOwner();
  const source = props.resource.owner ?? inheritedOwner;

  return (
    <ResourceOwnerProvider source={source} resourceId={props.resource.id}>
      <Resource {...props} ref={ref} />
    </ResourceOwnerProvider>
  );
});
const ResourceMemo = React.memo(ResourceWithOwner);

ResourceMemo.displayName = "Resource";

export default ResourceMemo;
