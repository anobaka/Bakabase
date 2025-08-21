"use client";

import React, { useRef, useState } from "react";

import MediaPreviewer from "@/components/MediaPreviewer";
const MediaPreviewerTest = () => {
  const [previewerVisible, setPreviewerVisible] = useState(false);
  const hoverTimerRef = useRef<any>();

  return (
    <div
      className={"media-previewer-container"}
      onMouseLeave={() => {
        clearTimeout(hoverTimerRef.current);
        hoverTimerRef.current = undefined;
        if (previewerVisible) {
          setPreviewerVisible(false);
        }
      }}
      onMouseOver={() => {
        if (!hoverTimerRef.current) {
          hoverTimerRef.current = setTimeout(() => {
            setPreviewerVisible(true);
          }, 1000);
        }
      }}
    >
      {previewerVisible && <MediaPreviewer resourceId={2501} />}
    </div>
  );
};

MediaPreviewerTest.displayName = "MediaPreviewerTest";

export default MediaPreviewerTest;
