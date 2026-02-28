"use client";

import type { CSSProperties } from "react";
import type { IResourceCoverRef } from "@/components/Resource/components/ResourceCover";
import type SimpleSearchEngine from "@/core/models/SimpleSearchEngine";
import type { Property, Resource as ResourceModel } from "@/core/models/Resource";
import type { TagValue } from "@/components/StandardValue/models";

import React, { useCallback, useImperativeHandle, useReducer, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  ApartmentOutlined,
  CheckCircleFilled,
  DisconnectOutlined,
  FileUnknownOutlined,
  HistoryOutlined,
  LoadingOutlined,
  PlayCircleOutlined,
  PushpinOutlined,
  QuestionCircleOutlined,
} from "@ant-design/icons";
import { ControlledMenu } from "@szhsin/react-menu";
import { AiOutlineSync } from "react-icons/ai";
import moment from "moment";

import StandardValueRenderer from "../StandardValue/ValueRenderer";

import "./index.css";

import { buildLogger, useTraceUpdate } from "@/components/utils";
import ResourceDetailModal from "@/components/Resource/components/DetailModal";
import BApi from "@/sdk/BApi";
import ResourceCover from "@/components/Resource/components/ResourceCover";
import Operations from "@/components/Resource/components/Operations";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { Button, Chip, Link, Tooltip } from "@/components/bakaui";
import {
  PropertyPool,
  ResourceAdditionalItem,
  ResourceProperty,
  ResourceTag,
  StandardValueType,
} from "@/sdk/constants";
import { useUiOptionsStore } from "@/stores/options";
import PlayControl from "@/components/Resource/components/PlayControl";
import type { PlayControlPortalProps } from "@/components/Resource/components/PlayControl";
import ContextMenuItems from "@/components/Resource/components/ContextMenuItems";
import { autoBackgroundColor } from "@/components/utils"; // adjust the path as needed

// PlayButton component defined outside Resource to maintain stable reference
const PlayButton: React.FC<PlayControlPortalProps> = ({ status, onClick, tooltipContent, cacheEnabled }) => {
  // Only show tooltip when preparing cache (loading + cacheEnabled)
  const showTooltip = status === "loading" && cacheEnabled;

  // Button size: ~22% of container (between 1/4 and 1/5), min 32px, max 64px
  // Using cqw (container query width) units for proper sizing relative to container
  const iconClass = "text-lg @[150px]:text-xl @[250px]:text-3xl";
  const buttonClass = "!w-[22cqw] !h-[22cqw] !min-w-8 !min-h-8 !max-w-16 !max-h-16 !p-0";

  const button = (
    <Button
      onPress={onClick}
      isIconOnly
      isDisabled={status === "loading"}
      className={buttonClass}
    >
      {status === "loading" ? (
        <LoadingOutlined className={iconClass} spin />
      ) : status === "not-found" ? (
        <QuestionCircleOutlined className={`${iconClass} text-warning`} />
      ) : (
        <PlayCircleOutlined className={iconClass} />
      )}
    </Button>
  );

  return (
    <div className="hidden group-hover/cover:flex absolute left-0 bottom-0 z-[1]">
      <Tooltip content={tooltipContent} isDisabled={!showTooltip}>
        {button}
      </Tooltip>
    </div>
  );
};

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
  biggerCoverPlacement?: TooltipPlacement;
  searchEngines?: SimpleSearchEngine[] | null;
  ct?: AbortSignal;
  onTagClick?: (propertyId: number, value: TagValue) => any;
  style?: any;
  className?: string;
  selected?: boolean;
  selectionModeRef?: React.RefObject<boolean>;
  onSelected?: (id: number, shiftKey?: boolean) => any;
  selectedResourceIds?: number[];
  selectedResources?: ResourceModel[];
  onSelectedResourcesChanged?: (ids: number[]) => any;
  debug?: boolean;
  /** Disable cover click to prevent opening DetailModal */
  disableCoverClick?: boolean;
};

const Resource = React.forwardRef((props: Props, ref) => {
  const {
    resource,
    onTagClick = (propertyId: number, value: TagValue) => {},
    ct = new AbortController().signal,
    biggerCoverPlacement,
    style: propStyle = {},
    selected = false,
    selectionModeRef,
    onSelected = (id: number, shiftKey?: boolean) => {},
    selectedResourceIds: propsSelectedResourceIds,
    selectedResources,
    onSelectedResourcesChanged,
    debug,
    disableCoverClick = false,
  } = props;

  // useTraceUpdate(props, props`Resource:${resource.id}|${resource.path}`);

  const { createPortal } = useBakabaseContext();

  const { t } = useTranslation();
  const log = buildLogger(`Resource:${resource.id}|${resource.path}`);
  const renderedTimes = useRef(0);

  renderedTimes.current += 1;

  const uiOptions = useUiOptionsStore((state) => state.data);

  // Use useReducer for stable forceUpdate reference
  const [, forceUpdate] = useReducer(x => x + 1, 0);

  const [contextMenuIsOpen, setContextMenuIsOpen] = useState(false);
  const [contextMenuAnchorPoint, setContextMenuAnchorPoint] = useState({
    x: 0,
    y: 0,
  });

  useImperativeHandle(ref, (): IResourceHandler => {
    return {
      id: resource.id,
      reload: reload,
      select: (selected: boolean) => {},
    };
  }, []);

  useTraceUpdate(props, `[${resource.fileName}]`);

  const displayPropertyKeys = uiOptions.resource?.displayProperties ?? [];

  log(`render #${renderedTimes.current}`, 'displayPropertyKeys:', displayPropertyKeys.length, 'hasProperties:', !!resource.properties, 'propertyPools:', resource.properties ? Object.keys(resource.properties) : '(none)', 'mediaLibraries:', resource.mediaLibraries, 'category:', resource.category?.name);

  // Discovery happens automatically via SSE now

  const coverRef = useRef<IResourceCoverRef>();

  // Keep a ref to the latest resource to avoid stale closure in reload
  const resourceRef = useRef(resource);
  resourceRef.current = resource;

  const reload = useCallback(async (ct?: AbortSignal) => {
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
  }, [forceUpdate]);

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
    const elementId = `resource-${resource.id}`;

    return (
      <div
        className="resource-cover-rectangle w-full max-w-full min-w-full pb-[100%] relative rounded group/cover @container [container-type:inline-size]"
        id={elementId}
      >
        <div className="absolute inset-0">
          <ResourceCover
            ref={coverRef}
            biggerCoverPlacement={biggerCoverPlacement}
            coverFit={uiOptions.resource?.coverFit}
            disableCarousel={uiOptions?.resource?.disableCoverCarousel}
            disableMediaPreviewer={uiOptions?.resource?.disableMediaPreviewer}
            resource={resource}
            showBiggerOnHover={uiOptions?.resource?.showBiggerCoverWhileHover}
            onClick={onCoverClick}
          />
        </div>
        {/* lef-top */}
        <div className={"absolute top-1 left-1 right-1 flex gap-1 items-center flex-wrap"}>
          {resource.tags.includes(ResourceTag.Pinned) && <PushpinOutlined />}
          {resource.tags.includes(ResourceTag.IsParent) && (
            <Tooltip content={t<string>("resource.tip.isParentResource")}>
              <ApartmentOutlined className={""} />
            </Tooltip>
          )}
          {resource.tags.includes(ResourceTag.PathDoesNotExist) && (
            <Tooltip content={t<string>("resource.tip.fileNotExist")}>
              <FileUnknownOutlined className={"text-warning"} />
            </Tooltip>
          )}
          {resource.tags.includes(ResourceTag.UnknownMediaLibrary) && (
            <Tooltip content={t<string>("resource.tip.unknownMediaLibrary")}>
              <DisconnectOutlined className={"text-warning"} />
            </Tooltip>
          )}
          {resource.playedAt && (
            <Chip radius={"sm"} size={"sm"} variant={"flat"} className="whitespace-break-spaces h-auto">
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
          {uiOptions.resource?.displayResourceId && (
            <Tooltip content={t<string>("resource.label.resourceId")}>
              <Chip radius={"sm"} size={"sm"} variant={"flat"}>
                {resource.id}
              </Chip>
            </Tooltip>
          )}
          {debug && (
            <Chip radius={"sm"} size={"sm"} variant={"flat"}>
              <div className={"flex items-center gap-1"}>
                <AiOutlineSync className={"text-base"} />
                {renderedTimes.current}
              </div>
            </Chip>
          )}
        </div>
        {(displayPropertyKeys.length > 0 || uiOptions.resource?.inlineDisplayName) && (
          <div
            className={
              "inline-flex flex-col gap-1 absolute bottom-0 right-0 items-end max-w-full max-h-full w-fit"
            }
          >
            {displayPropertyKeys.flatMap((dpk) => {
              const style: CSSProperties = {};

              let bizValue: any | undefined;
              let bizValueType: StandardValueType | undefined;

              try {
                switch (dpk.pool) {
                  case PropertyPool.Internal:
                    switch (dpk.id) {
                      case ResourceProperty.MediaLibrary:
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
                      case ResourceProperty.Category:
                        bizValue = resource.category?.name;
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
                  case PropertyPool.Custom:
                    const property = resource.properties?.[dpk.pool as PropertyPool]?.[dpk.id];

                    bizValue = property?.values?.find((x) => x.bizValue)?.bizValue;
                    bizValueType = property?.bizValueType;
                    break;
                }
              } catch (error) {
                log("error occurred while rendering display property", error);
              }

              log(`displayProperty pool:${dpk.pool} id:${dpk.id} => bizValue:`, bizValue, 'bizValueType:', bizValueType);

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
                  <StandardValueRenderer type={bizValueType} value={bizValue} variant="light" />
                </Chip>,
              ];
            })}
            {uiOptions.resource?.inlineDisplayName && renderDisplayNameAndTags(true)}
          </div>
        )}
        <PlayControl
          PortalComponent={PlayButton}
          afterPlaying={reload}
          resource={resource}
        />
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
        const values = p.values?.find((v) => (v.aliasAppliedBizValue as TagValue[])?.length > 0);

        if (
          values &&
          displayPropertyKeys.some((dpk) => dpk.pool == PropertyPool.Custom && dpk.id == id)
        ) {
          firstTagsValue = (values.aliasAppliedBizValue as TagValue[]).map((id, i) => {
            const bvs = values.aliasAppliedBizValue as TagValue[];
            const bv = bvs?.[i];

            return {
              value: id,
              ...bv,
            };
          });
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

  const selectedResourceIds = (propsSelectedResourceIds ?? []).slice();

  if (!selectedResourceIds.includes(resource.id)) {
    selectedResourceIds.push(resource.id);
  }

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

  // log("selectedResourceIds", selectedResourceIds);
  log("render", resource);

  return (
    <div
      key={resource.id}
      className={`flex flex-col p-1 rounded relative border-2 group/resource resource ${props.className} ${
        selected ? "border-primary ring-2 ring-primary/30 bg-primary/5" : "border-default-200"
      }`}
      data-id={resource.id}
      role={"resource"}
      style={style}
      onClickCapture={(e) => {
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
      <Operations coverRef={coverRef.current} reload={reload} resource={resource} />
      <div
        onContextMenu={(e) => {
          if (typeof document.hasFocus === "function" && !document.hasFocus()) return;

          e.preventDefault();
          setContextMenuAnchorPoint({
            x: e.clientX,
            y: e.clientY,
          });
          setContextMenuIsOpen(true);
        }}
      >
        <ControlledMenu
          key={resource.id}
          anchorPoint={contextMenuAnchorPoint}
          direction="right"
          state={contextMenuIsOpen ? "open" : "closed"}
          onClick={(e) => {
            e.preventDefault();
            e.stopPropagation();
          }}
          onClose={() => setContextMenuIsOpen(false)}
        >
          <ContextMenuItems
            selectedResourceIds={selectedResourceIds}
            selectedResources={selectedResources}
            onSelectedResourcesChanged={onSelectedResourcesChanged}
          />
        </ControlledMenu>
        <div className="relative">
          {renderCover()}
          {!uiOptions.resource?.inlineDisplayName && renderDisplayNameAndTags(false)}
        </div>
      </div>
    </div>
  );
});
const ResourceMemo = React.memo(Resource);

ResourceMemo.displayName = "Resource";

export default ResourceMemo;
