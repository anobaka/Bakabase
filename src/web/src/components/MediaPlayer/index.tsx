"use client";

import React, {
  forwardRef,
  useCallback,
  useEffect,
  useImperativeHandle,
  useMemo,
  useRef,
  useState,
} from "react";
import { useTranslation } from "react-i18next";
import "./index.scss";
import MediaPlayerLayout from "./components/MediaPlayerLayout";
import {
  COMPRESSED_FILE_ROOT_SEPARATOR,
  type MediaPlayerEntry,
  type MediaPlayerProps,
  type MediaPlayerRef,
} from "./types";
import { getEntryMediaType, isPreviewEntry, selectInitialMediaIndex } from "./media";
import { IwFsType, MediaType } from "@/sdk/constants";
import { Spinner } from "@/components/bakaui";
import BApi from "@/sdk/BApi";

export type { MediaPlayerEntry, MediaPlayerProps, MediaPlayerRef };

const MediaPlayer = forwardRef<MediaPlayerRef, MediaPlayerProps>(
  (
    {
      defaultActiveIndex,
      entries: propEntries,
      interval = 3000,
      renderOperations,
      autoPlay = false,
      ...otherProps
    },
    ref,
  ) => {
    const { t } = useTranslation();
    const root = useRef<HTMLDivElement>(null);
    const [entries, setEntries] = useState<MediaPlayerEntry[]>(() =>
      propEntries.map((entry) => ({ ...entry, playPath: entry.path })),
    );
    const [activeIndex, setActiveIndex] = useState(
      defaultActiveIndex ?? selectInitialMediaIndex(entries.filter(isPreviewEntry)),
    );
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState(false);
    const [loadAttempt, setLoadAttempt] = useState(0);
    const [collapsed, setCollapsed] = useState(false);
    const [playing, setPlaying] = useState(autoPlay);
    const [initialized, setInitialized] = useState(false);
    const timer = useRef<ReturnType<typeof setTimeout>>();
    const playableEntries = useMemo(() => entries.filter(isPreviewEntry), [entries]);
    const activeEntry = playableEntries[activeIndex];

    useEffect(() => {
      root.current?.focus({ preventScroll: true });
    }, []);

    useEffect(() => {
      let cancelled = false;
      const controller = new AbortController();
      const timeout = setTimeout(() => controller.abort(), 15_000);
      const initialize = async () => {
        let next: MediaPlayerEntry[] = propEntries.map((entry) => ({
          ...entry,
          playPath: entry.path,
        }));
        const single = propEntries.length === 1 ? propEntries[0] : undefined;
        setError(false);
        if (single && [IwFsType.Directory, IwFsType.CompressedFileEntry].includes(single.type)) {
          setLoading(true);
          try {
            if (single.type === IwFsType.Directory) {
              const rsp = await BApi.file.getAllFiles(
                { path: single.path },
                { signal: controller.signal },
              );
              if (rsp.code || !rsp.data) throw new Error();
              next = rsp.data.map((path) => ({
                path,
                playPath: path,
                name: path.split(/[/\\]/).pop() || path,
                type: IwFsType.Unknown,
                passwordsForDecompressing: [],
              }));
            } else {
              const rsp = await BApi.file.getCompressedFileEntries(
                { compressedFilePath: single.path },
                { signal: controller.signal },
              );
              if (rsp.code || !rsp.data) throw new Error();
              next = rsp.data.map((entry) => ({
                path: entry.path || "",
                playPath: `${single.path}${COMPRESSED_FILE_ROOT_SEPARATOR}${entry.path}`,
                name: entry.path?.split(/[/\\]/).pop() || "",
                type: IwFsType.Unknown,
                passwordsForDecompressing: [],
              }));
            }
          } catch {
            if (!cancelled) setError(true);
          }
        }
        if (cancelled) return;
        clearTimeout(timeout);
        setEntries(next);
        const files = next.filter(isPreviewEntry);
        setActiveIndex(
          defaultActiveIndex === undefined
            ? selectInitialMediaIndex(files)
            : Math.max(0, Math.min(defaultActiveIndex, files.length - 1)),
        );
        setLoading(false);
      };
      void initialize();
      return () => {
        cancelled = true;
        clearTimeout(timeout);
        controller.abort();
      };
    }, [propEntries, defaultActiveIndex, loadAttempt]);

    const previous = useCallback(() => setActiveIndex((index) => Math.max(0, index - 1)), []);
    const next = useCallback(
      () => setActiveIndex((index) => Math.min(playableEntries.length - 1, index + 1)),
      [playableEntries.length],
    );
    useImperativeHandle(ref, () => ({ gotoPrevEntry: previous, gotoNextEntry: next }), [
      previous,
      next,
    ]);

    useEffect(() => {
      setPlaying(autoPlay);
      setInitialized(false);
      clearTimeout(timer.current);
      return () => clearTimeout(timer.current);
    }, [activeEntry?.playPath, autoPlay]);

    const loaded = () => {
      setInitialized(true);
      if (
        autoPlay &&
        activeEntry &&
        [MediaType.Image, MediaType.Text].includes(getEntryMediaType(activeEntry))
      )
        timer.current = setTimeout(next, Math.max(interval, 1000));
    };

    /* eslint-disable jsx-a11y/no-noninteractive-element-interactions, jsx-a11y/no-noninteractive-tabindex -- This named player region intentionally receives focus for file-navigation shortcuts, while its native controls keep their own keys. */
    return (
      <div
        {...otherProps}
        ref={root}
        className={`media-player w-full h-full ${otherProps.className || ""}`}
        role="region"
        aria-label={t("mediaPlayer.title")}
        tabIndex={0}
        onKeyDown={(event) => {
          const target = event.target as HTMLElement;
          if (
            target.closest(
              "input, textarea, select, button, video, audio, [contenteditable=true]",
            ) ||
            event.altKey ||
            event.ctrlKey ||
            event.metaKey
          )
            return;
          if (event.key === "ArrowLeft") {
            event.preventDefault();
            previous();
          }
          if (event.key === "ArrowRight") {
            event.preventDefault();
            next();
          }
          if (
            event.key === " " &&
            activeEntry &&
            [MediaType.Video, MediaType.Audio].includes(getEntryMediaType(activeEntry))
          ) {
            event.preventDefault();
            setPlaying((value) => !value);
          }
        }}
      >
        {loading ? (
          <div className="media-player-empty">
            <Spinner />
            <span>{t("mediaPlayer.loadingFiles")}</span>
          </div>
        ) : !activeEntry ? (
          <div className="media-player-empty">
            <span>{t(error ? "mediaPlayer.filesFailed" : "mediaPlayer.noFiles")}</span>
            {error && (
              <button
                className="media-player-button"
                onClick={() => setLoadAttempt((value) => value + 1)}
              >
                {t("mediaPlayer.retry")}
              </button>
            )}
          </div>
        ) : (
          <MediaPlayerLayout
            activeEntry={activeEntry}
            activeIndex={activeIndex}
            autoPlay={autoPlay}
            currentInitialized={initialized}
            entries={entries}
            getMediaType={getEntryMediaType}
            leftPanelCollapsed={collapsed}
            playableEntries={playableEntries}
            playing={playing}
            renderOperations={renderOperations}
            onEntryClick={(entry) => {
              const index = playableEntries.findIndex((item) => item.path === entry.path);
              if (index >= 0) setActiveIndex(index);
            }}
            onLoad={loaded}
            onNextEntry={next}
            onPrevEntry={previous}
            onToggleCollapse={() => setCollapsed((value) => !value)}
            onVideoEnded={() => {
              if (autoPlay) next();
            }}
            onVideoPause={() => setPlaying(false)}
            onVideoPlay={() => setPlaying(true)}
          />
        )}
      </div>
    );
    /* eslint-enable jsx-a11y/no-noninteractive-element-interactions, jsx-a11y/no-noninteractive-tabindex */
  },
);
MediaPlayer.displayName = "MediaPlayer";
export default MediaPlayer;
