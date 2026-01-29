"use client";

import React, { useRef, useImperativeHandle, forwardRef, useState, useEffect } from "react";
import ReactPlayer from "react-player";

import { MediaType } from "@/sdk/constants";
import { buildLogger } from "@/components/utils";
import { Spinner } from "@/components/bakaui";
import TextReader from "@/components/TextReader";
import envConfig from "@/config/env";
import { useTranslation } from "react-i18next";
import BApi from "@/sdk/BApi";

import type { MediaPlayerEntry } from "../types";
import type { BakabaseServiceModelsViewFilePlayabilityViewModel } from "@/sdk/Api";

export interface MediaRendererRef {
  getImageRef: () => HTMLImageElement | null;
  getPlayerRef: () => any;
}

interface MediaRendererProps {
  entry: MediaPlayerEntry;
  mediaType: MediaType;
  playing: boolean;
  currentInitialized: boolean;
  onLoad: () => void;
  onVideoReady?: (width: number, height: number) => void;
  onVideoPlay?: () => void;
  onVideoPause?: () => void;
  onVideoEnded?: () => void;
  onVideoSeek?: () => void;
  onVideoStart?: () => void;
  onVideoProgress?: (state: {
    played: number;
    playedSeconds: number;
    loaded: number;
    loadedSeconds: number;
  }) => void;
  onPlayabilityError?: (error: string) => void;
}

const MediaRenderer = forwardRef<MediaRendererRef, MediaRendererProps>((props, ref) => {
  const {
    entry,
    mediaType,
    playing,
    currentInitialized,
    onLoad,
    onVideoReady,
    onVideoPlay,
    onVideoPause,
    onVideoEnded,
    onVideoSeek,
    onVideoStart,
    onVideoProgress,
    onPlayabilityError,
  } = props;

  const { t } = useTranslation();
  const log = buildLogger("MediaRenderer");
  const imageRef = useRef<HTMLImageElement | null>(null);
  const playerRef = useRef<any>(null);
  const videoSizeRef = useRef<{ width: number; height: number }>();
  const mediaContainerRef = useRef<HTMLDivElement | null>(null);

  // Playability check state
  const [playabilityInfo, setPlayabilityInfo] = useState<BakabaseServiceModelsViewFilePlayabilityViewModel | null>(null);
  const [isCheckingPlayability, setIsCheckingPlayability] = useState(false);
  const [playabilityChecked, setPlayabilityChecked] = useState(false);

  useImperativeHandle(ref, () => ({
    getImageRef: () => imageRef.current,
    getPlayerRef: () => playerRef.current,
  }));

  // Use playPath for compressed file entries, otherwise use path
  const playPath = entry.playPath || entry.path;

  // Check playability when entry changes (only for video/audio)
  useEffect(() => {
    const checkPlayability = async () => {
      // Only check for video and audio files
      if (mediaType !== MediaType.Video && mediaType !== MediaType.Audio) {
        setPlayabilityChecked(true);
        setPlayabilityInfo({ playable: true, mediaType });
        return;
      }

      setIsCheckingPlayability(true);
      setPlayabilityChecked(false);
      setPlayabilityInfo(null);

      try {
        const response = await BApi.file.checkFilePlayability({ fullname: playPath });
        if (response.data) {
          setPlayabilityInfo(response.data);
          if (!response.data.playable && response.data.error) {
            log("File not playable:", response.data.error);
            onPlayabilityError?.(response.data.error);
          }
        }
      } catch (err) {
        log("Playability check failed:", err);
        // On error, assume playable and let the player handle it
        setPlayabilityInfo({ playable: true, mediaType });
      } finally {
        setIsCheckingPlayability(false);
        setPlayabilityChecked(true);
      }
    };

    checkPlayability();
  }, [playPath, mediaType]);

  const renderMediaContent = () => {
    // Show checking state for video/audio
    if ((mediaType === MediaType.Video || mediaType === MediaType.Audio) && isCheckingPlayability) {
      return (
        <div className="flex flex-col items-center justify-center text-white/70">
          <Spinner size="lg" />
          <div className="mt-2">{t("Checking playability...")}</div>
        </div>
      );
    }

    // Show error if not playable
    if (playabilityChecked && playabilityInfo && !playabilityInfo.playable) {
      return (
        <div className="flex flex-col items-center justify-center text-white/70 text-center p-4">
          <div className="text-red-400 text-xl mb-2">⚠️ {t("Cannot play this file")}</div>
          <div className="text-sm opacity-80">{playabilityInfo.error || t("Unknown error")}</div>
        </div>
      );
    }

    switch (mediaType) {
      case MediaType.Audio:
      case MediaType.Video:
        return (
          <ReactPlayer
            {...({
              ref: (player: any) => {
                playerRef.current = player;
              },
              controls: true,
              className: "max-w-full max-h-full object-contain",
              config: {
                file: {
                  attributes: {
                    crossOrigin: "anonymous",
                  },
                },
              },
              height: videoSizeRef.current?.height,
              playing: playing,
              url: `${envConfig.apiEndpoint}/file/play?fullname=${encodeURIComponent(playPath)}`,
              width: videoSizeRef.current?.width,
            } as any)}
            {...({
              onDuration: (d: number) => {
                // alert(d);
              },
              onEnded: () => {
                onVideoEnded?.();
              },
              onPause: () => {
                log("Video pause");
                onVideoPause?.();
              },
              onPlay: () => {
                log("Video play");
                onVideoPlay?.();
              },
              onProgress: (state: {
                played: number;
                playedSeconds: number;
                loaded: number;
                loadedSeconds: number;
              }) => {
                onVideoProgress?.(state);
              },
              onReady: () => {
                if (playerRef.current) {
                  const internalPlayer =
                    playerRef.current.getInternalPlayer() as HTMLVideoElement;

                  const width = internalPlayer.videoWidth;
                  const height = internalPlayer.videoHeight;

                  if (width > 0 && height > 0) {
                    videoSizeRef.current = {
                      width: Math.min(width, mediaContainerRef.current!.clientWidth),
                      height: Math.min(height, mediaContainerRef.current!.clientHeight),
                    };
                  }
                  log("Video ready", `${width}x${height}`);
                  onVideoReady?.(width, height);
                  onLoad();
                }
              },
              onSeek: () => {
                log("Video seek");
                onVideoSeek?.();
              },
              onStart: () => {
                log("Video start");
                onVideoStart?.();
              },
            } as any)}
          />
        );
      case MediaType.Image:
        return (
          <img
            ref={imageRef}
            className="max-w-full max-h-full object-contain"
            crossOrigin={"anonymous"}
            src={`${envConfig.apiEndpoint}/file/play?fullname=${encodeURIComponent(playPath)}`}
            onLoad={() => {
              onLoad();
            }}
          />
        );
      case MediaType.Text:
        return (
          <TextReader
            className="max-w-full max-h-full object-contain"
            file={playPath}
            style={{ padding: "20px" }}
            onLoad={() => {
              onLoad();
            }}
          />
        );
      default:
        return (
          <div
            className="max-w-full max-h-full object-contain text-white text-2xl"
            onLoad={() => {
              onLoad();
            }}
          >
            {t<string>("Unsupported")}
          </div>
        );
    }
  };

  return (
    <div
      ref={mediaContainerRef}
      className="relative w-full h-full flex flex-col items-center justify-center p-5 z-[1]"
    >
      {!currentInitialized && (
        <div className="absolute inset-0 flex items-center justify-center z-[2] bg-black/30">
          <Spinner size="lg" />
        </div>
      )}
      {renderMediaContent()}
    </div>
  );
});

MediaRenderer.displayName = "MediaRenderer";
export default MediaRenderer;
