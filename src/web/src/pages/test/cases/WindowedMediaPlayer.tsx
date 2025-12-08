"use client";

import type { BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry } from "@/sdk/Api";

import React, { useEffect, useState } from "react";

import { Button, Spinner, toast } from "@/components/bakaui";
import MediaPlayer from "@/components/MediaPlayer";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { IwFsType } from "@/sdk/constants";
import BApi from "@/sdk/BApi";

const TEST_DIRECTORY = "/Users/anobaka/git";

const WindowedMediaPlayerTest = () => {
  const { createWindow } = useBakabaseContext();
  const [entries, setEntries] = useState<
    BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry[]
  >([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [autoPlay, setAutoPlay] = useState(false);
  const [windowCount, setWindowCount] = useState(0);

  const loadEntries = async () => {
    setLoading(true);
    setError(null);
    try {
      const rsp = await BApi.file.getChildrenIwFsInfo(
        { root: TEST_DIRECTORY },
        {
          showErrorToast: (r) => {
            return r.code >= 404 || (r.code < 200 && r.code !== 404);
          },
        },
      );

      if (rsp.code) {
        setError(rsp.message || "Failed to load entries");
        toast.danger(rsp.message || "Failed to load entries");
      } else {
        // @ts-ignore
        const loadedEntries = rsp.data?.entries || [];

        setEntries(loadedEntries);
        if (loadedEntries.length === 0) {
          toast.default("No entries found in the directory");
        }
      }
    } catch (e: any) {
      const errorMsg = e?.message || "An error occurred while loading entries";

      setError(errorMsg);
      toast.danger(errorMsg);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadEntries();
  }, []);

  const handleOpenWindowedMediaPlayer = () => {
    if (entries.length === 0) {
      toast.warning("No entries to display. Please load entries first.");

      return;
    }

    const instanceId = windowCount + 1;

    setWindowCount(instanceId);

    createWindow(MediaPlayer, {
      entries: entries,
      defaultActiveIndex: 0,
      autoPlay: autoPlay,
    }, {
      title: `Media Player ${instanceId}`,
      persistent: true,
      afterClose: () => {
        setWindowCount((prev) => Math.max(0, prev - 1));
      },
    });
  };

  const handleOpenMultipleWindows = () => {
    if (entries.length === 0) {
      toast.warning("No entries to display. Please load entries first.");

      return;
    }

    // Open 3 windows with different starting positions
    for (let i = 0; i < 3; i++) {
      setTimeout(() => {
        const instanceId = windowCount + i + 1;

        setWindowCount((prev) => prev + 1);

        createWindow(MediaPlayer, {
          entries: entries,
          defaultActiveIndex: i % Math.min(entries.length, 10), // Different starting index for each
          autoPlay: autoPlay,
        }, {
          title: `Media Player ${instanceId}`,
          persistent: true,
          afterClose: () => {
            setWindowCount((prev) => Math.max(0, prev - 1));
          },
        });
      }, i * 100); // Stagger the opening slightly
    }
  };

  return (
    <div className="windowed-media-player-test-container p-5">
      <div className="flex flex-wrap gap-2.5 items-center mb-5">
        <Button
          color="primary"
          isDisabled={loading || entries.length === 0}
          onClick={handleOpenWindowedMediaPlayer}
        >
          Open Windowed Media Player
        </Button>

        <Button
          color="secondary"
          isDisabled={loading || entries.length === 0}
          onClick={handleOpenMultipleWindows}
        >
          Open 3 Windows
        </Button>

        <Button color="secondary" isDisabled={loading} onClick={loadEntries}>
          {loading ? <Spinner size="sm" /> : "Reload Entries"}
        </Button>

        <Button
          color={autoPlay ? "success" : "default"}
          variant={autoPlay ? "solid" : "bordered"}
          onClick={() => {
            setAutoPlay(!autoPlay);
          }}
        >
          Auto Play: {autoPlay ? "ON" : "OFF"}
        </Button>

        <div className="ml-5 text-gray-600 text-sm">
          {loading ? (
            <span>Loading...</span>
          ) : (
            <span>
              Entries: {entries.length} items from {TEST_DIRECTORY}
            </span>
          )}
        </div>

        {windowCount > 0 && (
          <div className="ml-5 px-3 py-1.5 bg-blue-100 dark:bg-blue-900 rounded text-blue-700 dark:text-blue-300 text-sm font-medium">
            Active Windows: {windowCount}
          </div>
        )}
      </div>

      {error && (
        <div className="mb-5 p-4 bg-red-50 dark:bg-red-900/20 rounded-lg text-red-700 dark:text-red-400">
          <strong>Error:</strong> {error}
        </div>
      )}

      <div className="mt-5 p-4 bg-gray-50 dark:bg-gray-800 rounded-lg">
        <h3 className="mt-0 mb-2.5 text-lg font-semibold">Loaded Entries Preview:</h3>
        {loading ? (
          <div className="text-center py-5">
            <Spinner size="lg" />
            <div className="mt-2.5 text-gray-600">Loading entries from {TEST_DIRECTORY}...</div>
          </div>
        ) : entries.length === 0 ? (
          <div className="text-gray-600 text-center py-5">
            No entries found. The directory might be empty or inaccessible.
          </div>
        ) : (
          <div className="text-xs text-gray-600 dark:text-gray-400 max-h-[200px] overflow-y-auto">
            {entries.slice(0, 20).map((entry, index) => (
              <div key={index} className="mb-1">
                <span className="font-bold">{entry.name}</span> - {entry.path}
                {entry.type === IwFsType.Directory && (
                  <span className="text-blue-600 dark:text-blue-400 ml-2">[DIRECTORY]</span>
                )}
                {entry.type === IwFsType.CompressedFileEntry && (
                  <span className="text-orange-600 dark:text-orange-400 ml-2">[COMPRESSED]</span>
                )}
              </div>
            ))}
            {entries.length > 20 && <div>... and {entries.length - 20} more entries</div>}
          </div>
        )}
      </div>

      <div className="mt-5 p-4 bg-blue-50 dark:bg-blue-900/20 rounded-lg">
        <h4 className="mt-0 mb-2.5 text-base font-semibold">Features to Test:</h4>
        <ul className="m-0 pl-5 text-sm text-gray-700 dark:text-gray-300 space-y-1">
          <li>
            <strong>Multiple Instances:</strong> Click "Open 3 Windows" to test multiple windows
            simultaneously
          </li>
          <li>
            <strong>Drag:</strong> Click and hold the window header, then drag to move windows
            around
          </li>
          <li>
            <strong>Resize:</strong> Hover over window edges/corners and drag to resize
          </li>
          <li>
            <strong>Minimize:</strong> Click the minimize button (or double-click header) to
            minimize to title bar
          </li>
          <li>
            <strong>Maximize:</strong> Click the maximize button (or double-click header) to toggle
            full screen
          </li>
          <li>
            <strong>Close:</strong> Click the close button (X) to close windows
          </li>
          <li>
            <strong>Magnetic Snapping:</strong> Windows automatically snap to viewport edges and
            other windows when dragging/resizing
          </li>
          <li>
            <strong>Z-Index:</strong> Clicking a window brings it to the front
          </li>
          <li>
            <strong>Independent Control:</strong> Each window can be dragged, resized, minimized,
            and maximized independently
          </li>
          <li>
            <strong>MediaPlayer Features:</strong> All MediaPlayer features work within windows
            (thumbnails, navigation, nested players, etc.)
          </li>
        </ul>
      </div>

      <div className="mt-5 p-4 bg-yellow-50 dark:bg-yellow-900/20 rounded-lg">
        <h4 className="mt-0 mb-2.5 text-base font-semibold">Testing Tips:</h4>
        <ul className="m-0 pl-5 text-sm text-gray-700 dark:text-gray-300 space-y-1">
          <li>
            Open multiple windows and try dragging them near each other to see magnetic snapping
          </li>
          <li>Resize windows near viewport edges to see edge snapping</li>
          <li>Minimize some windows and maximize others to test different states</li>
          <li>Try overlapping windows and clicking on them to test z-index management</li>
          <li>Test that each window maintains its own media playback state independently</li>
        </ul>
      </div>
    </div>
  );
};

WindowedMediaPlayerTest.displayName = "WindowedMediaPlayerTest";

export default WindowedMediaPlayerTest;
