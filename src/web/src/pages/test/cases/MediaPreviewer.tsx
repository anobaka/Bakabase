"use client";

import React, { useRef, useState } from "react";

import MediaPreviewerPage from "@/components/MediaPreviewer";
const MediaPreviewerPage = () => {
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
      {previewerVisible && <MediaPreviewerPage resourceId={2501} />}
    </div>
  );
};

MediaPreviewerPage.displayName = "MediaPreviewerPage";

export default MediaPreviewerPage;
