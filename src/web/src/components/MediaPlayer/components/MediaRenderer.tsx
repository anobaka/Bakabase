"use client";

import type { MediaPlayerEntry } from "../types";
import React, { forwardRef, useEffect, useImperativeHandle, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { MediaType } from "@/sdk/constants";
import { Spinner } from "@/components/bakaui";
import TextReader from "@/components/TextReader";
import envConfig from "@/config/env";

export interface MediaRendererRef {
  getImageRef: () => HTMLImageElement | null;
  getPlayerRef: () => HTMLMediaElement | null;
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
export const MEDIA_LOAD_TIMEOUT = 20_000;

/** /file/play already chooses direct play, remux or transcode. The browser judges
 * the delivered bytes; a probe of the original codec must not reject that stream. */
const MediaRenderer = forwardRef<MediaRendererRef, MediaRendererProps>((props, ref) => {
  const {
    entry,
    mediaType,
    playing,
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
  const image = useRef<HTMLImageElement>(null);
  const media = useRef<HTMLVideoElement | HTMLAudioElement>(null);
  const [status, setStatus] = useState<"loading" | "ready" | "buffering" | "error">("loading");
  const [errorKey, setErrorKey] = useState("mediaPlayer.media.failed");
  const [attempt, setAttempt] = useState(0);
  const path = entry.playPath || entry.path;
  const isMedia = mediaType === MediaType.Video || mediaType === MediaType.Audio;
  const needsLoad = isMedia || mediaType === MediaType.Image;
  const source = `${envConfig.apiEndpoint}/file/play?fullname=${encodeURIComponent(path)}`;
  useImperativeHandle(ref, () => ({
    getImageRef: () => image.current,
    getPlayerRef: () => media.current,
  }));

  useEffect(() => {
    setStatus("loading");
  }, [path, mediaType, attempt]);
  useEffect(() => {
    if (!needsLoad || (status !== "loading" && status !== "buffering")) return;
    const timer = setTimeout(() => {
      setErrorKey("mediaPlayer.media.timeout");
      setStatus("error");
      onPlayabilityError?.(t("mediaPlayer.media.timeout"));
    }, MEDIA_LOAD_TIMEOUT);
    return () => clearTimeout(timer);
  }, [path, mediaType, attempt, status, needsLoad, t]);

  useEffect(() => {
    const element = media.current;
    if (!element || !isMedia || status === "error") return;
    let cancelled = false;
    if (playing && element.paused) {
      void element.play()?.catch((error: DOMException) => {
        if (cancelled || media.current !== element) return;
        // Autoplay rejection leaves native controls available for a user gesture.
        if (error.name === "NotAllowedError" || error.name === "AbortError") onVideoPause?.();
        else {
          setErrorKey("mediaPlayer.media.unsupported");
          setStatus("error");
        }
      });
    } else if (!playing && !element.paused) element.pause();
    return () => {
      cancelled = true;
    };
  }, [playing, isMedia, status, path]);

  const ready = () => {
    setStatus("ready");
    onLoad();
    const element = media.current;
    if (element instanceof HTMLVideoElement)
      onVideoReady?.(element.videoWidth, element.videoHeight);
  };
  const failed = () => {
    const code = media.current?.error?.code;
    const key =
      code === 2
        ? "mediaPlayer.media.network"
        : code === 3
          ? "mediaPlayer.media.decode"
          : code === 4
            ? "mediaPlayer.media.unsupported"
            : "mediaPlayer.media.failed";
    setErrorKey(key);
    setStatus("error");
    onPlayabilityError?.(t(key));
  };
  const events = {
    onLoadedMetadata: ready,
    onCanPlay: ready,
    onError: failed,
    onWaiting: () => {
      if (!media.current?.paused || media.current.readyState < 1) setStatus("buffering");
    },
    onStalled: () => {
      if (!media.current?.paused || media.current.readyState < 1) setStatus("buffering");
    },
    onPlaying: () => {
      setStatus("ready");
      onVideoStart?.();
    },
    onPlay: () => onVideoPlay?.(),
    onPause: () => onVideoPause?.(),
    onEnded: () => onVideoEnded?.(),
    onSeeked: () => onVideoSeek?.(),
    onTimeUpdate: () => {
      const element = media.current;
      if (!element) return;
      const duration = Number.isFinite(element.duration) ? element.duration : 0;
      const loaded = element.buffered.length
        ? element.buffered.end(element.buffered.length - 1)
        : 0;
      onVideoProgress?.({
        played: duration ? element.currentTime / duration : 0,
        playedSeconds: element.currentTime,
        loaded: duration ? loaded / duration : 0,
        loadedSeconds: loaded,
      });
    },
  };

  if (mediaType === MediaType.Text)
    return <TextReader key={path} file={path} onLoad={onLoad} onError={onPlayabilityError} />;
  if (!needsLoad)
    return (
      <div className="media-player-empty">
        <span className="media-player-state-icon">◇</span>
        <strong>{t("mediaPlayer.unsupported")}</strong>
        <span className="media-player-hint">{t("mediaPlayer.unsupportedDescription")}</span>
        <code>{entry.ext || entry.name}</code>
      </div>
    );
  if (status === "error")
    return (
      <div className="media-player-empty" role="alert">
        <span className="media-player-state-icon">!</span>
        <strong>{t(errorKey)}</strong>
        <p className="media-player-hint">
          {t(isMedia ? "mediaPlayer.media.failureHint" : "mediaPlayer.image.failureHint")}
        </p>
        <button
          className="media-player-button"
          onClick={() => {
            setStatus("loading");
            setAttempt((value) => value + 1);
          }}
        >
          {t("mediaPlayer.retry")}
        </button>
      </div>
    );

  /* eslint-disable jsx-a11y/media-has-caption -- User-selected local media has no supplied caption track. Subtitle files remain available as separate text previews; do not attach a fictitious caption URL. */
  return (
    <div className="media-renderer">
      {mediaType === MediaType.Image ? (
        <img
          key={`${path}:${attempt}`}
          ref={image}
          alt={entry.name}
          src={source}
          onLoad={ready}
          onError={failed}
        />
      ) : mediaType === MediaType.Video ? (
        <video
          key={`${path}:${attempt}`}
          ref={media as React.RefObject<HTMLVideoElement>}
          src={source}
          controls
          playsInline
          preload="metadata"
          {...events}
          aria-label={entry.name}
        />
      ) : (
        <div className="media-player-audio">
          <span className="media-player-audio-symbol">♫</span>
          <h2>{entry.name}</h2>
          <audio
            key={`${path}:${attempt}`}
            ref={media as React.RefObject<HTMLAudioElement>}
            src={source}
            controls
            preload="metadata"
            {...events}
            aria-label={entry.name}
          />
        </div>
      )}
      {(status === "loading" || status === "buffering") && (
        <div className="media-player-loading" role="status">
          <Spinner size="lg" />
          <span>
            {t(status === "buffering" ? "mediaPlayer.buffering" : "mediaPlayer.loadingMedia")}
          </span>
        </div>
      )}
    </div>
  );
  /* eslint-enable jsx-a11y/media-has-caption */
});
MediaRenderer.displayName = "MediaRenderer";
export default MediaRenderer;
