"use client";

import React, { useCallback, useEffect, useImperativeHandle, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { useUpdate, useUpdateEffect } from "react-use";

import envConfig from "@/config/env";
import { buildLogger, uuidv4 } from "@/components/utils";
import MediaPreviewerPage from "@/components/MediaPreviewer";
import "./index.scss";
import { useAppContextStore } from "@/stores/appContext";
import { CoverFit, ResourceCacheType } from "@/sdk/constants";
import { Carousel, Tooltip, Image } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import ResourceRequestQueue from "@/utils/requestQueue";

import type { Resource as ResourceModel } from "@/core/models/Resource";

import FallbackCover from "@/components/Resource/components/ResourceCover/components/FallbackCover.tsx";

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
  useCache?: boolean;
  disableMediaPreviewer?: boolean;
  biggerCoverPlacement?: TooltipPlacement;
  coverFit?: CoverFit;
  disableCarousel?: boolean;
};

export interface IResourceCoverRef {
  load: (disableBrowserCache?: boolean) => void;
}

const ResourceCover = React.forwardRef((props: Props, ref) => {
  const {
    resource,
    onClick: propsOnClick,
    showBiggerOnHover = true,
    useCache = false,
    disableMediaPreviewer = false,
    biggerCoverPlacement,
    coverFit = CoverFit.Contain,
    disableCarousel = false,
  } = props;
  // log('rendering', props);
  const { t } = useTranslation();
  const log = buildLogger(`ResourceCover:${resource.id}|${resource.path}`);

  // useTraceUpdate(props, `ResourceCover:${resource.id}|${resource.path}`);
  const forceUpdate = useUpdate();
  const [urls, setUrls] = useState<string[]>();

  const [previewerVisible, setPreviewerVisible] = useState(false);
  const previewerHoverTimerRef = useRef<any>();

  const disableCacheRef = useRef(useCache);

  const appContext = useAppContextStore((state) => state);
  const { createPortal } = useBakabaseContext();

  const containerRef = useRef<HTMLDivElement>(null);
  const maxCoverRawSizeRef = useRef<{ w: number; h: number }>({
    w: 0,
    h: 0,
  });

  const [failureUrls, setFailureUrls] = useState<Set<string>>(new Set());
  const [blobUrls, setBlobUrls] = useState<Map<string, string>>(new Map());
  const loadingRef = useRef(false);
  const mountedRef = useRef(true);

  // log(resource);

  useUpdateEffect(() => {
    forceUpdate();
  }, [coverFit]);

  useEffect(() => {
    // log('urls changed', urls);
  }, [urls]);

  useEffect(() => {
    disableCacheRef.current = useCache;
  }, [useCache]);

  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
      // Clean up blob URLs on unmount
      blobUrls.forEach((blobUrl) => URL.revokeObjectURL(blobUrl));
    };
  }, []);

  const loadCover = useCallback(
    (disableBrowserCache?: boolean) => {
      if (loadingRef.current) return;
      loadingRef.current = true;

      const serverAddresses = appContext.apiEndpoints ?? [envConfig.apiEndpoint];

      const newUrls: string[] = [];

      const cps = resource.coverPaths ?? [];

      if (cps.length == 0) {
        if (useCache) {
          if (resource.cache && resource.cache.cachedTypes.includes(ResourceCacheType.Covers)) {
            if (resource.cache.coverPaths && resource.cache.coverPaths.length > 0) {
              cps.push(...resource.cache.coverPaths);
            } else {
              cps.push(resource.path);
            }
          }
        }
      }

      const resourceServerAddresses =
        serverAddresses.length == 1 ? serverAddresses : serverAddresses.slice(1);
      const serverAddress =
        resourceServerAddresses[Math.floor(Math.random() * resourceServerAddresses.length)];

      log("cps", cps, "cache", resource.cache);

      if (cps.length > 0) {
        newUrls.push(
          ...cps.map(
            (coverPath) => `${serverAddress}/tool/thumbnail?path=${encodeURIComponent(coverPath)}`,
          ),
        );
      } else {
        newUrls.push(`${serverAddress}/resource/${resource.id}/cover`);
      }

      if (disableBrowserCache) {
        for (let i = 0; i < newUrls.length; i++) {
          newUrls[i] += newUrls[i].includes("?") ? `&v=${uuidv4()}` : `?v=${uuidv4()}`;
        }
      }

      setUrls(newUrls);

      // Load images through queue
      newUrls.forEach((url) => {
        ResourceRequestQueue.push(async () => {
          if (!mountedRef.current) return;
          try {
            const response = await fetch(url);
            if (!response.ok) throw new Error("Failed to load");
            const blob = await response.blob();
            if (!mountedRef.current) return;
            const blobUrl = URL.createObjectURL(blob);
            setBlobUrls((prev) => new Map(prev).set(url, blobUrl));
          } catch (e) {
            if (!mountedRef.current) return;
            setFailureUrls((prev) => new Set(prev).add(url));
            log("failed to load url", url);
          }
        });
      });

      loadingRef.current = false;
    },
    [resource, useCache],
  );

  useUpdateEffect(() => {
    // Clean up old blob URLs before loading new ones
    blobUrls.forEach((blobUrl) => URL.revokeObjectURL(blobUrl));
    setBlobUrls(new Map());
    setFailureUrls(new Set());
    loadCover(false);
  }, [resource, loadCover]);

  useEffect(() => {
    loadCover(false);

    const resizeObserver = new ResizeObserver(() => {
      // Do what you want to do when the size of the element changes
      forceUpdate();
    });

    resizeObserver.observe(containerRef.current!);

    return () => resizeObserver.disconnect(); // clean up
  }, []);

  useImperativeHandle(ref, (): IResourceCoverRef => {
    return {
      load: loadCover,
    };
  }, [loadCover]);

  // useTraceUpdate(props, '[ResourceCover]');
  const onClick = useCallback(() => {
    if (propsOnClick) {
      propsOnClick();
    }
  }, [propsOnClick]);

  const renderCover = useCallback(() => {
    if (urls) {
      let dynamicClassNames: string[] = [
        coverFit == CoverFit.Cover ? "object-cover" : "object-contain",
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
          // autoplay={false}
          dots={urls && urls.length > 1}
        >
          {renderingUrls?.map((url) => {
            const blobUrl = blobUrls.get(url);
            const isLoading = !blobUrl && !failureUrls.has(url);

            return (
              <div key={url}>
                <div
                  className={"flex items-center justify-center"}
                  style={{
                    width: containerRef.current?.clientWidth,
                    height: containerRef.current?.clientHeight,
                  }}
                >
                  {/* https://github.com/heroui-inc/heroui/issues/4756 */}
                  {failureUrls.has(url) ? (
                    <FallbackCover afterClearingCache={() => loadCover(true)} id={resource.id} />
                  ) : isLoading ? (
                    <div className="w-full h-full bg-default-100 animate-pulse" />
                  ) : (
                    <Image
                      key={blobUrl}
                      removeWrapper
                      className={`${dynamicClassName} max-w-full max-h-full`}
                      loading={"eager"}
                      src={blobUrl}
                      onLoad={(e) => {
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
                      }}
                    />
                  )}
                </div>
              </div>
            );
          })}
        </Carousel>
      );
    }

    return null;
  }, [urls, coverFit, disableCarousel, blobUrls, failureUrls]);

  const renderContainer = () => {
    return (
      <div
        ref={containerRef}
        className="resource-cover-container relative overflow-hidden"
        onClick={onClick}
        onMouseLeave={() => {
          // console.log('mouse leave');
          if (!disableMediaPreviewer) {
            clearTimeout(previewerHoverTimerRef.current);
            previewerHoverTimerRef.current = undefined;
            if (previewerVisible) {
              setPreviewerVisible(false);
            }
          }
        }}
        onMouseOver={(e) => {
          // console.log('mouse over');
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
    // ignore small cover
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

  // log("render", "failure urls", failureUrls);

  return (
    <Tooltip
      // key={urls?.join(',')}
      // isOpen
      content={
        <div
          style={{
            width: tooltipWidth,
            height: tooltipHeight,
          }}
        >
          <Carousel
            adaptiveHeight
            autoplay={urls && urls.length > 1}
            dots={urls && urls.length > 1}
          >
            {urls?.map((url) => {
              const blobUrl = blobUrls.get(url);

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
                      <FallbackCover afterClearingCache={() => loadCover(true)} id={resource.id} />
                    ) : blobUrl ? (
                      <Image
                        key={blobUrl}
                        removeWrapper
                        alt={""}
                        loading={"eager"}
                        src={blobUrl}
                        style={{
                          maxWidth: tooltipWidth,
                          maxHeight: tooltipHeight,
                        }}
                      />
                    ) : (
                      <div
                        className="bg-default-100 animate-pulse"
                        style={{
                          width: tooltipWidth,
                          height: tooltipHeight,
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
      isDisabled={tooltipWidth == undefined}
      placement={biggerCoverPlacement}
    >
      {renderContainer()}
    </Tooltip>
  );
});
const ResourceCoverMemo = React.memo(ResourceCover);

ResourceCoverMemo.displayName = "ResourceCover";

export default ResourceCoverMemo;
