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

  const loadCover = useCallback(
    (disableBrowserCache?: boolean) => {
      const serverAddresses = appContext.apiEndpoints ?? [envConfig.apiEndpoint];

      const urls: string[] = [];

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
        urls.push(
          ...cps.map(
            (coverPath) => `${serverAddress}/tool/thumbnail?path=${encodeURIComponent(coverPath)}`,
          ),
        );
      } else {
        urls.push(`${serverAddress}/resource/${resource.id}/cover`);
      }

      if (disableBrowserCache) {
        for (let i = 0; i < urls.length; i++) {
          urls[i] += urls[i].includes("?") ? `&v=${uuidv4()}` : `?v=${uuidv4()}`;
        }
      }
      // log(urls, resource);
      setUrls(urls);
    },
    [resource, useCache],
  );

  useUpdateEffect(() => {
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
      let dynamicClassNames: string[] = [];

      if (containerRef.current && maxCoverRawSizeRef.current) {
        if (maxCoverRawSizeRef.current.w > containerRef.current.clientWidth) {
          dynamicClassNames.push("w-full");
        }
        if (maxCoverRawSizeRef.current.h > containerRef.current.clientHeight) {
          dynamicClassNames.push("h-full");
        }
        dynamicClassNames.push(coverFit == CoverFit.Cover ? "object-cover" : "object-contain");
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
                  ) : (
                    <Image
                      key={url}
                      className={`${dynamicClassName} max-w-full max-h-full`}
                      // fallbackSrc={renderBrokenCover()}
                      // fallbackSrc={
                      //   <FallbackCover
                      //     afterClearingCache={() => loadCover(true)}
                      //     id={resource.id}
                      //   />
                      // }
                      loading={"eager"}
                      src={url}
                      onError={() => {
                        failureUrls.add(url);
                        setFailureUrls(new Set<string>(failureUrls));
                        log("failed to load url", url);
                      }}
                      onLoad={(e) => {
                        // forceUpdate();
                        const img = e.target as HTMLImageElement;

                        // log("loaded", e, img);
                        if (img) {
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
  }, [urls, coverFit, disableCarousel]);

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
            {urls?.map((url) => (
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
                  ) : (
                    <Image
                      key={url}
                      alt={""}
                      // fallbackSrc={
                      //   <FallbackCover
                      //     afterClearingCache={() => loadCover(true)}
                      //     id={resource.id}
                      //   />
                      // }
                      loading={"eager"}
                      src={url}
                      style={{
                        maxWidth: tooltipWidth,
                        maxHeight: tooltipHeight,
                      }}
                      onError={() => {
                        failureUrls.add(url);
                        setFailureUrls(new Set<string>(failureUrls));
                      }}
                      onLoad={(e) => {
                        // log('loaded bigger', e);
                      }}
                    />
                  )}
                </div>
              </div>
            ))}
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
