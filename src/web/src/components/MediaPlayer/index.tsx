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
  type MediaBrowseScope,
  type MediaPreviewState,
} from "./types";
import { getEntryMediaType, isPreviewEntry, mediaPathKey, selectInitialMediaIndex } from "./media";
import { usePlayerInput } from "./usePlayerInput";

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
      renderFooterExtra,
      autoPlay = false,
      resourceScope = false,
      initialScope = "resource",
      configuredPaths,
      onOpenAlternative,
      canOpenAlternative,
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
    const [scope, setScope] = useState<MediaBrowseScope>(initialScope);
    const [previewStates, setPreviewStates] = useState<Record<string, MediaPreviewState>>({});
    const timer = useRef<ReturnType<typeof setTimeout>>();
    const scopedEntries = useMemo(() => {
      if (!resourceScope || scope !== "configured") return entries;
      const candidates = new Set((configuredPaths ?? []).map(mediaPathKey));

      return entries.filter((entry) => candidates.has(mediaPathKey(entry.path)));
    }, [entries, resourceScope, scope, configuredPaths]);
    const playableEntries = useMemo(() => scopedEntries.filter(isPreviewEntry), [scopedEntries]);
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
        const configured = new Set((configuredPaths ?? []).map(mediaPathKey));
        const files = next.filter(
          (entry) =>
            isPreviewEntry(entry) &&
            (!resourceScope || scope !== "configured" || configured.has(mediaPathKey(entry.path))),
        );

        const nextIndex =
          defaultActiveIndex === undefined
            ? selectInitialMediaIndex(files)
            : selectInitialMediaIndex(
                files,
                next[defaultActiveIndex] ? [next[defaultActiveIndex].path] : [],
              );
        const firstEntry = files[nextIndex];

        setActiveIndex(nextIndex);
        setPreviewStates(
          firstEntry
            ? {
                [firstEntry.playPath || firstEntry.path]: [
                  MediaType.Video,
                  MediaType.Audio,
                  MediaType.Image,
                  MediaType.Text,
                ].includes(getEntryMediaType(firstEntry))
                  ? "loading"
                  : "unavailable",
              }
            : {},
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

    const changeScope = (value: MediaBrowseScope) => {
      setScope(value);
      // Scope changes are local filtering; they do not start another discovery or lose load results.
      setActiveIndex(0);
    };

    const previewChanged = useCallback((path: string, state: MediaPreviewState) => {
      setPreviewStates((current) =>
        current[path] === state ? current : { ...current, [path]: state },
      );
    }, []);

    const previous = useCallback(() => setActiveIndex((index) => Math.max(0, index - 1)), []);
    const next = useCallback(
      () => setActiveIndex((index) => Math.max(0, Math.min(playableEntries.length - 1, index + 1))),
      [playableEntries.length],
    );

    usePlayerInput(root, {
      mediaType: activeEntry ? getEntryMediaType(activeEntry) : undefined,
      canGoPrevious: activeIndex > 0,
      canGoNext: activeIndex < playableEntries.length - 1,
      previous,
      next,
      togglePlaying: () => setPlaying((value) => !value),
    });
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

    /* eslint-disable jsx-a11y/no-noninteractive-tabindex -- This named player region intentionally receives focus for file-navigation shortcuts, while its native controls keep their own keys. */
    return (
      <div
        {...otherProps}
        ref={root}
        aria-label={t("mediaPlayer.title")}
        className={`media-player w-full h-full ${otherProps.className || ""}`}
        role="region"
        tabIndex={0}
      >
        {resourceScope && (
          <div className="media-player-scope" title={t("mediaPlayer.scope.hint")}>
            <label>
              <span>{t("mediaPlayer.scope.label")}</span>
              {configuredPaths !== undefined ? (
                <select
                  aria-label={t("mediaPlayer.scope.label")}
                  value={scope}
                  onChange={(event) => changeScope(event.target.value as MediaBrowseScope)}
                >
                  <option value="resource">{t("mediaPlayer.scope.resource")}</option>
                  <option value="configured">{t("mediaPlayer.scope.configured")}</option>
                </select>
              ) : (
                <span>{t("mediaPlayer.scope.resource")}</span>
              )}
            </label>
            <span>{scopedEntries.length}</span>
          </div>
        )}
        {loading ? (
          <div className="media-player-empty">
            <Spinner />
            <span>{t("mediaPlayer.loadingFiles")}</span>
          </div>
        ) : !activeEntry ? (
          <div className="media-player-empty">
            <span>
              {t(
                error
                  ? "mediaPlayer.filesFailed"
                  : scope === "configured"
                    ? "mediaPlayer.scope.configuredEmpty"
                    : "mediaPlayer.noFiles",
              )}
            </span>
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
            entries={scopedEntries}
            getMediaType={getEntryMediaType}
            leftPanelCollapsed={collapsed}
            playableEntries={playableEntries}
            playing={playing}
            previewStates={previewStates}
            renderFooterExtra={renderFooterExtra}
            renderOperations={renderOperations}
            onEntryClick={(entry) => {
              const index = playableEntries.findIndex((item) => item.path === entry.path);

              if (index >= 0) setActiveIndex(index);
            }}
            onLoad={loaded}
            onNextEntry={next}
            onOpenAlternative={
              !canOpenAlternative || canOpenAlternative(activeEntry.playPath || activeEntry.path)
                ? onOpenAlternative
                : undefined
            }
            onPrevEntry={previous}
            onPreviewStateChange={previewChanged}
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
    /* eslint-enable jsx-a11y/no-noninteractive-tabindex */
  },
);

MediaPlayer.displayName = "MediaPlayer";
export default MediaPlayer;
