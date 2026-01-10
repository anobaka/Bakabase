"use client";

import React, { useEffect, useState } from "react";
import { Button, Spinner, toast } from "@/components/bakaui";
import MediaPlayer from "@/components/MediaPlayer";
import { IwFsType } from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import type { BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry } from "@/sdk/Api";

const TEST_DIRECTORY = "E:\\迅雷下载";

const MediaPlayerTest = () => {
  const [entries, setEntries] = useState<BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [autoPlay, setAutoPlay] = useState(false);

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

  const handleOpenMediaPlayer = () => {
    if (entries.length === 0) {
      toast.warning("No entries to display. Please load entries first.");
      return;
    }

    MediaPlayer.show({
      entries: entries,
      defaultActiveIndex: 0,
      autoPlay: autoPlay,
    });
  };

  return (
    <div className="media-player-test-container" style={{ padding: "20px" }}>
      <div
        style={{
          marginBottom: "20px",
          display: "flex",
          gap: "10px",
          flexWrap: "wrap",
          alignItems: "center",
        }}
      >
        <Button
          color="primary"
          onClick={handleOpenMediaPlayer}
          isDisabled={loading || entries.length === 0}
        >
          Open Media Player
        </Button>

        <Button
          color="secondary"
          onClick={loadEntries}
          isDisabled={loading}
        >
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

        <div style={{ marginLeft: "20px", color: "#666", fontSize: "14px" }}>
          {loading ? (
            <span>Loading...</span>
          ) : (
            <span>
              Entries: {entries.length} items from {TEST_DIRECTORY}
            </span>
          )}
        </div>
      </div>

      {error && (
        <div
          style={{
            marginBottom: "20px",
            padding: "15px",
            background: "#fee",
            borderRadius: "8px",
            color: "#c33",
          }}
        >
          <strong>Error:</strong> {error}
        </div>
      )}

      <div style={{ marginTop: "20px", padding: "15px", background: "#f5f5f5", borderRadius: "8px" }}>
        <h3 style={{ marginTop: 0, marginBottom: "10px" }}>Loaded Entries Preview:</h3>
        {loading ? (
          <div style={{ textAlign: "center", padding: "20px" }}>
            <Spinner size="lg" />
            <div style={{ marginTop: "10px", color: "#666" }}>Loading entries from {TEST_DIRECTORY}...</div>
          </div>
        ) : entries.length === 0 ? (
          <div style={{ color: "#666", textAlign: "center", padding: "20px" }}>
            No entries found. The directory might be empty or inaccessible.
          </div>
        ) : (
          <div style={{ fontSize: "12px", color: "#666", maxHeight: "200px", overflowY: "auto" }}>
            {entries.slice(0, 20).map((entry, index) => (
              <div key={index} style={{ marginBottom: "4px" }}>
                <span style={{ fontWeight: "bold" }}>{entry.name}</span> - {entry.path}
                {entry.type === IwFsType.Directory && (
                  <span style={{ color: "#0066cc", marginLeft: "8px" }}>[DIRECTORY]</span>
                )}
                {entry.type === IwFsType.CompressedFileEntry && (
                  <span style={{ color: "#cc6600", marginLeft: "8px" }}>[COMPRESSED]</span>
                )}
              </div>
            ))}
            {entries.length > 20 && <div>... and {entries.length - 20} more entries</div>}
          </div>
        )}
      </div>

      <div style={{ marginTop: "20px", padding: "15px", background: "#e8f4f8", borderRadius: "8px" }}>
        <h4 style={{ marginTop: 0, marginBottom: "10px" }}>Features to Test:</h4>
        <ul style={{ margin: 0, paddingLeft: "20px", fontSize: "14px", color: "#555" }}>
          <li>Left panel: Collapsible thumbnail panel with all entries</li>
          <li>Thumbnails: Click to preview files, double-click directories/compressed files to open nested player</li>
          <li>Page counter: Shows current/total playable items at top of left panel</li>
          <li>Nested players: Double-click directories or compressed files to open new player above current</li>
          <li>Right panel: Vertical scrolling content area for media preview</li>
          <li>Media types: Images, videos, audios, text files</li>
          <li>Visual indicators: Directories and compressed files show badges</li>
          <li>Preloading: Next 3 image items are preloaded automatically</li>
          <li>Keyboard: Arrow keys to navigate, Escape to close (closes nested players first)</li>
          <li>Flat structure: No tree expansion - all entries shown in flat list</li>
        </ul>
      </div>
    </div>
  );
};

MediaPlayerTest.displayName = "MediaPlayerTest";

export default MediaPlayerTest;

















