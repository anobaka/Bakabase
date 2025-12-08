"use client";

import React, {
  useCallback,
  useEffect,
  useImperativeHandle,
  useRef,
  useState,
  forwardRef,
} from "react";
import { useUpdateEffect } from "react-use";

import "./index.scss";

import MediaPlayerLayout from "./components/MediaPlayerLayout";
import {
  COMPRESSED_FILE_ROOT_SEPARATOR,
  type MediaPlayerEntry,
  type MediaPlayerProps,
  type MediaPlayerRef,
} from "./types";

import { IwFsType, MediaType } from "@/sdk/constants";
import { buildLogger, forceFocus, useTraceUpdate } from "@/components/utils";
import BApi from "@/sdk/BApi";

import type { BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry } from "@/sdk/Api";

export type { MediaPlayerEntry, MediaPlayerProps, MediaPlayerRef };

// Helper to build play path for an entry
const buildPlayPath = (
  entry: BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry,
  compressedFilePath?: string,
): string => {
  if (compressedFilePath) {
    // Entry is inside a compressed file, construct path with separator
    return `${compressedFilePath}${COMPRESSED_FILE_ROOT_SEPARATOR}${entry.path}`;
  }

  return entry.path;
};

// Helper to convert entries to MediaPlayerEntry with playPath
const convertToMediaPlayerEntries = (
  entries: BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry[],
  compressedFilePath?: string,
): MediaPlayerEntry[] => {
  return entries.map((entry) => ({
    ...entry,
    playPath: buildPlayPath(entry, compressedFilePath),
  }));
};

const MediaPlayer = forwardRef<MediaPlayerRef, MediaPlayerProps>((props, ref) => {
  const {
    defaultActiveIndex = 0,
    entries: propEntries = [],
    interval = 1000,
    renderOperations = (): any => {},
    autoPlay = false,
    ...otherProps
  } = props;

  useTraceUpdate(props, "[MediaPlayer]");
  const log = buildLogger("MediaPlayer");

  log(props);

  const [extensionsMediaTypes, setExtensionsMediaTypes] = useState<Map<string | undefined, number>>(
    new Map<string, number>(),
  );
  const [playing, setPlaying] = useState(autoPlay);

  const [progress, setProgress] = useState(0);
  const progressRef = useRef(0);
  const progressIntervalRef = useRef<any>();
  const activeDtRef = useRef<Date | undefined>();
  const autoGoToNextHandleRef = useRef<any>();

  const [leftPanelCollapsed, setLeftPanelCollapsed] = useState(false);

  // Current entries being displayed (may be expanded from a single directory/compressed file)
  const [currentEntries, setCurrentEntries] = useState<MediaPlayerEntry[]>(() =>
    convertToMediaPlayerEntries(propEntries),
  );
  // Loading state for async operations
  const [isLoading, setIsLoading] = useState(false);

  // Filter to only playable entries (files, not directories or compressed files)
  const playableEntries = currentEntries.filter(
    (entry) =>
      entry.type !== IwFsType.Directory &&
      entry.type !== IwFsType.CompressedFileEntry &&
      entry.type !== IwFsType.Drive,
  );
  const [activeIndex, setActiveIndex] = useState(
    Math.min(defaultActiveIndex, Math.max(0, playableEntries.length - 1)),
  );
  const activeEntry = playableEntries[activeIndex];

  // Preload following contents
  const preloadRefs = useRef<Map<string, HTMLImageElement | HTMLVideoElement>>(new Map());

  const [currentInitialized, setCurrentInitialized] = useState(false);

  // Check if we should auto-expand a single directory or compressed file entry
  const checkAndAutoExpand = useCallback(async (entries: MediaPlayerEntry[]) => {
    // Only auto-expand if there's exactly one entry and it's a directory or compressed file
    if (entries.length !== 1) {
      return { shouldExpand: false, entries };
    }

    const singleEntry = entries[0];

    if (singleEntry.type === IwFsType.Directory) {
      setIsLoading(true);
      try {
        const rsp = await BApi.file.getAllFiles({ path: singleEntry.path });

        if (!rsp.code && rsp.data && rsp.data.length > 0) {
          // Convert file paths to entries
          const expandedEntries: MediaPlayerEntry[] = rsp.data.map((filePath) => {
            const name = filePath.split(/[/\\]/).pop() || filePath;
            const ext = name.includes(".") ? name.split(".").pop() : undefined;

            return {
              path: filePath,
              name: name,
              meaningfulName: name,
              ext: ext,
              type: IwFsType.Unknown,
              passwordsForDecompressing: [],
              playPath: filePath,
            };
          });

          return { shouldExpand: true, entries: expandedEntries };
        }
      } finally {
        setIsLoading(false);
      }
    } else if (singleEntry.type === IwFsType.CompressedFileEntry) {
      setIsLoading(true);
      try {
        const rsp = await BApi.file.getCompressedFileEntries({
          compressedFilePath: singleEntry.path,
        });

        if (!rsp.code && rsp.data && rsp.data.length > 0) {
          // Convert compressed file entries with proper play paths
          const expandedEntries: MediaPlayerEntry[] = rsp.data.map((ce) => {
            const path = ce.path || "";
            const name = path.split(/[/\\]/).pop() || path;
            const ext = name.includes(".") ? name.split(".").pop() : undefined;

            return {
              path: path,
              name: name,
              meaningfulName: name,
              ext: ext,
              type: IwFsType.Unknown,
              passwordsForDecompressing: [],
              playPath: `${singleEntry.path}${COMPRESSED_FILE_ROOT_SEPARATOR}${path}`,
            };
          });

          return { shouldExpand: true, entries: expandedEntries };
        }
      } finally {
        setIsLoading(false);
      }
    }

    return { shouldExpand: false, entries };
  }, []);

  // Initialize and auto-expand if needed
  useEffect(() => {
    const initEntries = async () => {
      const initialEntries = convertToMediaPlayerEntries(propEntries);
      const result = await checkAndAutoExpand(initialEntries);

      if (result.shouldExpand) {
        setCurrentEntries(result.entries);
      } else {
        setCurrentEntries(initialEntries);
      }
      setActiveIndex(0);
    };

    initEntries();
  }, [propEntries, checkAndAutoExpand]);

  useUpdateEffect(() => {
    progressRef.current = progress;
  }, [progress]);

  useEffect(() => {
    BApi.api.getAllExtensionMediaTypes().then((t) => {
      if (t.data) {
        const map = new Map<string | undefined, number>();

        Object.keys(t.data).forEach((ext) => {
          map.set(ext, t.data![ext]);
        });

        setExtensionsMediaTypes(map);
        console.log("[MediaPlayer]Extension-Media-Type initialized", map);
      }
    });

    return () => {
      console.log("unmounting");
      clearProgressHandler();
    };
  }, []);

  const getMediaType = useCallback(
    (entry: BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry): MediaType => {
      // First try to get from IwFsType
      if (entry.type === IwFsType.Image) return MediaType.Image;
      if (entry.type === IwFsType.Video) return MediaType.Video;
      if (entry.type === IwFsType.Audio) return MediaType.Audio;

      // Fallback to extension-based detection
      const ext = entry.ext || entry.path.split(".").pop();

      if (!ext) return MediaType.Unknown;

      return extensionsMediaTypes.get(`.${ext}`) ?? MediaType.Unknown;
    },
    [extensionsMediaTypes],
  );

  const gotoPrevEntry = useCallback(() => {
    if (activeIndex > 0) {
      setActiveIndex(activeIndex - 1);
    }
  }, [activeIndex]);

  const gotoNextEntry = useCallback(() => {
    if (activeIndex < playableEntries.length - 1) {
      setActiveIndex(activeIndex + 1);
    }
  }, [activeIndex, playableEntries.length]);

  const handleEntryClick = useCallback(
    (entry: BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry) => {
      // If it's a directory or compressed file, do nothing on single click
      if (entry.type === IwFsType.Directory || entry.type === IwFsType.CompressedFileEntry) {
        return;
      }

      const index = playableEntries.findIndex((e) => e.path === entry.path);

      if (index >= 0) {
        setActiveIndex(index);
      }
    },
    [playableEntries],
  );

  // Preload following contents
  useEffect(() => {
    if (!activeEntry) return;

    const preloadCount = 3; // Preload next 3 items

    for (
      let i = activeIndex + 1;
      i < playableEntries.length && i < activeIndex + 1 + preloadCount;
      i++
    ) {
      const entry = playableEntries[i] as MediaPlayerEntry;

      if (entry && getMediaType(entry) === MediaType.Image) {
        const img = new Image();
        const playPath = entry.playPath || entry.path;

        img.src = `${import.meta.env.VITE_API_ENDPOINT || ""}/file/play?fullname=${encodeURIComponent(playPath)}`;
        preloadRefs.current.set(entry.path, img);
      }
    }

    return () => {
      // Cleanup old preloads
      preloadRefs.current.forEach((_, key) => {
        if (key !== activeEntry.path) {
          const isInPreloadRange = playableEntries
            .slice(activeIndex + 1, activeIndex + 1 + preloadCount)
            .some((e) => e.path === key);

          if (!isInPreloadRange) {
            preloadRefs.current.delete(key);
          }
        }
      });
    };
  }, [activeIndex, activeEntry, playableEntries, getMediaType]);

  const clearProgressHandler = () => {
    clearInterval(progressIntervalRef.current);
    clearTimeout(autoGoToNextHandleRef.current);
  };

  useUpdateEffect(() => {
    console.log("current initialized", currentInitialized);
  }, [currentInitialized]);

  useEffect(() => {
    setPlaying(autoPlay);
    setProgress(0);
    setCurrentInitialized(false);
    clearProgressHandler();
  }, [activeEntry]);

  // Expose methods to parent via ref
  useImperativeHandle(
    ref,
    () => ({
      gotoPrevEntry,
      gotoNextEntry,
    }),
    [gotoPrevEntry, gotoNextEntry],
  );

  const tryAutoGotoNextEntry = () => {
    if (autoPlay) {
      clearProgressHandler();
      activeDtRef.current = new Date();
      progressIntervalRef.current = setInterval(() => {
        const raw = ((new Date().getTime() - activeDtRef.current!.getTime()) / interval) * 100;
        const percentage = Math.floor(raw);

        if (percentage != progressRef.current) {
          setProgress(percentage);
        }
      }, 100);
      autoGoToNextHandleRef.current = setTimeout(
        () => {
          gotoNextEntry();
        },
        Math.max(interval, 1000),
      );
    }
  };

  const handleLoad = () => {
    setCurrentInitialized(true);
    tryAutoGotoNextEntry();
  };

  const handleVideoPlay = () => {
    log("Video play");
    setPlaying(true);
  };

  const handleVideoPause = () => {
    log("Video pause");
    setPlaying(false);
  };

  const handleVideoEnded = () => {
    tryAutoGotoNextEntry();
  };

  const handleWheel = (e: React.WheelEvent) => {
    // Prevent default scrolling behavior
    e.preventDefault();
    e.stopPropagation();

    // Scroll up (negative deltaY) = previous, scroll down (positive deltaY) = next
    if (e.deltaY < 0) {
      gotoPrevEntry();
    } else if (e.deltaY > 0) {
      gotoNextEntry();
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    switch (e.key) {
      case "ArrowLeft":
        e.preventDefault();
        gotoPrevEntry();
        break;
      case "ArrowRight":
        e.preventDefault();
        gotoNextEntry();
        break;
      case "Escape":
        // Allow parent to handle Escape (e.g., close window)
        break;
      default:
        break;
    }
  };

  log("Rendering", playableEntries, activeIndex, activeEntry);

  if (isLoading) {
    return (
      <div className="w-full h-full flex items-center justify-center bg-black/90">
        <div className="text-white">Loading...</div>
      </div>
    );
  }

  // No playable entries - show message with thumbnail panel if there are non-playable entries (dirs/archives)
  if (!activeEntry) {
    return (
      <div className="w-full h-full flex items-center justify-center bg-black/90">
        <div className="text-white/70 text-lg">No playable files</div>
      </div>
    );
  }

  return (
    <div
      ref={(r) => {
        if (r) {
          forceFocus(r);
          r.focus();
        }
      }}
      className="w-full h-full"
      tabIndex={0}
      onKeyDown={handleKeyDown}
      {...otherProps}
    >
      <MediaPlayerLayout
        activeEntry={activeEntry}
        activeIndex={activeIndex}
        autoPlay={autoPlay}
        currentInitialized={currentInitialized}
        entries={currentEntries}
        getMediaType={getMediaType}
        leftPanelCollapsed={leftPanelCollapsed}
        playableEntries={playableEntries}
        playing={playing}
        progress={progress}
        renderOperations={renderOperations}
        onEntryClick={handleEntryClick}
        onLoad={handleLoad}
        onNextEntry={gotoNextEntry}
        onPrevEntry={gotoPrevEntry}
        onToggleCollapse={() => setLeftPanelCollapsed(!leftPanelCollapsed)}
        onVideoEnded={handleVideoEnded}
        onVideoPause={handleVideoPause}
        onVideoPlay={handleVideoPlay}
        onWheel={handleWheel}
      />
    </div>
  );
});

MediaPlayer.displayName = "MediaPlayer";
export default MediaPlayer;
