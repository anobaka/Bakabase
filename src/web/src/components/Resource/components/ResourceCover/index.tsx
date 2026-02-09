"use client";

import React, { useCallback, useEffect, useImperativeHandle, useMemo, useRef, useState } from "react";
import { useUpdate, useUpdateEffect } from "react-use";
import { useTranslation } from "react-i18next";

import envConfig from "@/config/env";
import { buildLogger } from "@/components/utils";
import MediaPreviewerPage from "@/components/MediaPreviewer";
import "./index.scss";
import { useAppContextStore } from "@/stores/appContext";
import { useUiOptionsStore } from "@/stores/options";
import { CoverFit } from "@/sdk/constants";
import { Carousel, Tooltip, Image, Spinner } from "@/components/bakaui";

import type { Resource as ResourceModel } from "@/core/models/Resource";

import FallbackCover from "@/components/Resource/components/ResourceCover/components/FallbackCover.tsx";
import { useCoverDiscovery } from "@/hooks/useResourceDiscovery";

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
  onClick?: () => any;
  showBiggerOnHover?: boolean;
  disableMediaPreviewer?: boolean;
  biggerCoverPlacement?: TooltipPlacement;
  coverFit?: CoverFit;
  disableCarousel?: boolean;
};

export interface IResourceCoverRef {
  reload: () => void;
}

const ResourceCover = React.forwardRef((props: Props, ref) => {
  const {
    resource,
    onClick: propsOnClick,
    showBiggerOnHover = true,
    disableMediaPreviewer = false,
    biggerCoverPlacement,
    coverFit = CoverFit.Contain,
    disableCarousel = false,
  } = props;

  const { t } = useTranslation();

  const log = buildLogger(`ResourceCover:${resource.id}|${resource.path}`);

  const forceUpdate = useUpdate();

  // Get cache enabled status from UI options
  const resourceUiOptions = useUiOptionsStore((state) => state.data?.resource);
  const cacheEnabled = !resourceUiOptions?.disableCache;

  // Use SSE-based discovery for covers
  const discoveryState = useCoverDiscovery(resource, cacheEnabled);

  const [previewerVisible, setPreviewerVisible] = useState(false);
  const previewerHoverTimerRef = useRef<any>();

  const appContext = useAppContextStore((state) => state);

  const containerRef = useRef<HTMLDivElement>(null);
  const maxCoverRawSizeRef = useRef<{ w: number; h: number }>({
    w: 0,
    h: 0,
  });

  const [failureUrls, setFailureUrls] = useState<Set<string>>(new Set());
  const [reloadKey, setReloadKey] = useState(0);

  // Stabilize array references using useMemo to prevent unnecessary re-renders
  const stableDiscoveryCoverPaths = useMemo(
    () => discoveryState.coverPaths,
    [discoveryState.coverPaths?.join(",")]
  );
  const stableResourceCoverPaths = useMemo(
    () => resource.coverPaths,
    [resource.coverPaths?.join(",")]
  );
  const stableApiEndpoints = useMemo(
    () => appContext.apiEndpoints,
    [appContext.apiEndpoints?.join(",")]
  );

  // Build URLs from discovery result
  // Uses only 2nd-nth endpoints for resource loading (1st is for API calls)
  const urls = useMemo(() => {
    if (discoveryState.status !== "ready") {
      return null;
    }

    const serverAddresses = stableApiEndpoints ?? [envConfig.apiEndpoint];
    const resourceServerAddresses =
      serverAddresses.length === 1 ? serverAddresses : serverAddresses.slice(1);
    const serverAddress =
      resourceServerAddresses[Math.floor(Math.random() * resourceServerAddresses.length)];

    // Priority: resource.coverPaths (from ReservedPropertyValue) > discovered covers (from cache) > resource.path
    // This ensures user-configured or enhancer-created covers take precedence over auto-discovered cache
    const coverPaths = stableResourceCoverPaths?.length
      ? stableResourceCoverPaths
      : stableDiscoveryCoverPaths?.length
        ? stableDiscoveryCoverPaths
        : [resource.path];

    return coverPaths.map(
      (coverPath) => `${serverAddress}/tool/thumbnail?path=${encodeURIComponent(coverPath)}`
    );
  }, [discoveryState.status, stableDiscoveryCoverPaths, stableApiEndpoints, stableResourceCoverPaths, resource.path, reloadKey]);

  useUpdateEffect(() => {
    forceUpdate();
  }, [coverFit]);

  // Reset failure state when URLs change
  useUpdateEffect(() => {
    setFailureUrls(new Set());
  }, [urls]);

  // Reset state when resource changes
  useUpdateEffect(() => {
    setFailureUrls(new Set());
    maxCoverRawSizeRef.current = { w: 0, h: 0 };
  }, [resource.id]);

  // ResizeObserver for container
  useEffect(() => {
    if (!containerRef.current) return;

    const resizeObserver = new ResizeObserver(() => {
      forceUpdate();
    });

    resizeObserver.observe(containerRef.current);
    return () => resizeObserver.disconnect();
  }, []);

  const reload = useCallback(() => {
    setReloadKey((k) => k + 1);
  }, []);

  useImperativeHandle(ref, (): IResourceCoverRef => {
    return { reload };
  }, [reload]);

  const onClick = useCallback(() => {
    if (propsOnClick) {
      propsOnClick();
    }
  }, [propsOnClick]);

  // Compute tooltip content for loading state
  const loadingTooltipContent = useMemo(() => {
    if (discoveryState.status === "loading") {
      if (discoveryState.cacheEnabled) {
        return t("resource.cover.tooltip.cachePreparing");
      }
      return t("resource.cover.tooltip.loading");
    }
    return undefined;
  }, [discoveryState.status, discoveryState.cacheEnabled, t]);

  const handleImageLoad = useCallback((e: React.SyntheticEvent<HTMLImageElement>) => {
    const img = e.target as HTMLImageElement;

    if (img) {
      const prevW = maxCoverRawSizeRef.current?.w ?? 0;
      const prevH = maxCoverRawSizeRef.current?.h ?? 0;

      if (!maxCoverRawSizeRef.current) {
        maxCoverRawSizeRef.current = {
          w: img.naturalWidth,
          h: img.naturalHeight,
        };
      } else {
        maxCoverRawSizeRef.current.w = Math.max(
          maxCoverRawSizeRef.current.w,
          img.naturalWidth,
        );
        maxCoverRawSizeRef.current.h = Math.max(
          maxCoverRawSizeRef.current.h,
          img.naturalHeight,
        );
      }

      if (
        maxCoverRawSizeRef.current.w !== prevW ||
        maxCoverRawSizeRef.current.h !== prevH
      ) {
        forceUpdate();
      }
    }
  }, [forceUpdate]);

  const handleImageError = useCallback((url: string) => {
    setFailureUrls((prev) => new Set(prev).add(url));
    log("failed to load url", url);
  }, []);

  const renderCover = useCallback(() => {
    // Show loading state with tooltip
    if (discoveryState.status === "loading" || !urls) {
      return (
        <Tooltip content={loadingTooltipContent} isDisabled={!loadingTooltipContent}>
          <div className="w-full h-full flex items-center justify-center bg-default-100">
            <Spinner size="sm" />
          </div>
        </Tooltip>
      );
    }

    // Show error state
    if (discoveryState.status === "error") {
      return (
        <FallbackCover afterClearingCache={reload} id={resource.id} />
      );
    }

    let dynamicClassNames: string[] = [
      coverFit === CoverFit.Cover ? "object-cover" : "object-contain",
    ];

    if (containerRef.current && maxCoverRawSizeRef.current) {
      if (maxCoverRawSizeRef.current.w > containerRef.current.clientWidth) {
        dynamicClassNames.push("w-full");
      }
      if (maxCoverRawSizeRef.current.h > containerRef.current.clientHeight) {
        dynamicClassNames.push("h-full");
      }
    }
    const dynamicClassName = dynamicClassNames.join(" ");

    const renderingUrls = disableCarousel ? urls.slice(0, 1) : urls;

    return (
      <Carousel
        key={renderingUrls.join(",")}
        autoplay={renderingUrls && renderingUrls.length > 1}
        dots={urls && urls.length > 1}
      >
        {renderingUrls?.map((url) => {
          return (
            <div key={url}>
              <div
                className={"flex items-center justify-center"}
                style={{
                  width: containerRef.current?.clientWidth,
                  height: containerRef.current?.clientHeight,
                }}
              >
                {failureUrls.has(url) ? (
                  <FallbackCover afterClearingCache={reload} id={resource.id} />
                ) : (
                  <Image
                    key={url}
                    removeWrapper
                    className={`${dynamicClassName} max-w-full max-h-full`}
                    loading={"eager"}
                    // @ts-ignore - fetchPriority is supported but not in all type defs
                    fetchPriority={"low"}
                    src={url}
                    onLoad={handleImageLoad}
                    onError={() => handleImageError(url)}
                  />
                )}
              </div>
            </div>
          );
        })}
      </Carousel>
    );
  }, [urls, coverFit, disableCarousel, failureUrls, discoveryState.status, loadingTooltipContent, handleImageLoad, handleImageError]);

  const renderContainer = () => {
    return (
      <div
        ref={containerRef}
        className="resource-cover-container relative overflow-hidden"
        onClick={onClick}
        onMouseLeave={() => {
          if (!disableMediaPreviewer) {
            clearTimeout(previewerHoverTimerRef.current);
            previewerHoverTimerRef.current = undefined;
            if (previewerVisible) {
              setPreviewerVisible(false);
            }
          }
        }}
        onMouseOver={() => {
          if (!disableMediaPreviewer) {
            if (!previewerHoverTimerRef.current) {
              previewerHoverTimerRef.current = setTimeout(() => {
                setPreviewerVisible(true);
              }, 1000);
            }
          }
        }}
      >
        {previewerVisible && <MediaPreviewerPage resourceId={resource.id} />}
        {renderCover()}
      </div>
    );
  };

  let tooltipWidth: number | undefined;
  let tooltipHeight: number | undefined;

  if (showBiggerOnHover && typeof window !== "undefined") {
    const containerWidth = containerRef.current?.clientWidth ?? 100;
    const containerHeight = containerRef.current?.clientHeight ?? 100;

    if (
      maxCoverRawSizeRef.current.w > containerWidth &&
      maxCoverRawSizeRef.current.h > containerHeight
    ) {
      const tooltipScale = Math.min(
        (window.innerWidth * 0.6) / maxCoverRawSizeRef.current.w,
        (window.innerHeight * 0.6) / maxCoverRawSizeRef.current.h,
      );

      tooltipWidth = maxCoverRawSizeRef.current.w * tooltipScale;
      tooltipHeight = maxCoverRawSizeRef.current.h * tooltipScale;
    }
  }

  return (
    <Tooltip
      content={
        <div
          style={{
            width: tooltipWidth,
            height: tooltipHeight,
          }}
        >
          <Carousel
            adaptiveHeight
            autoplay={!!(urls && urls.length > 1)}
            dots={!!(urls && urls.length > 1)}
          >
            {urls?.map((url) => {
              return (
                <div key={url}>
                  <div
                    className={"flex items-center justify-center"}
                    style={{
                      maxWidth: tooltipWidth,
                      maxHeight: tooltipHeight,
                    }}
                  >
                    {failureUrls.has(url) ? (
                      <FallbackCover afterClearingCache={reload} id={resource.id} />
                    ) : (
                      <Image
                        key={url}
                        removeWrapper
                        alt={""}
                        loading={"eager"}
                        src={url}
                        style={{
                          maxWidth: tooltipWidth,
                          maxHeight: tooltipHeight,
                        }}
                      />
                    )}
                  </div>
                </div>
              );
            })}
          </Carousel>
        </div>
      }
      isDisabled={tooltipWidth === undefined}
      placement={biggerCoverPlacement}
    >
      {renderContainer()}
    </Tooltip>
  );
});

const ResourceCoverMemo = React.memo(ResourceCover);

ResourceCoverMemo.displayName = "ResourceCover";

export default ResourceCoverMemo;
