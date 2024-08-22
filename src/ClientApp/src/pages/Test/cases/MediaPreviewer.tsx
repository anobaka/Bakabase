import React, { useRef, useState } from 'react';
import MediaPreviewer from '@/components/MediaPreviewer';

export default () => {
  const [previewerVisible, setPreviewerVisible] = useState(false);
  const hoverTimerRef = useRef<any>();

  return (
    <div
      className={'media-previewer-container'}
      onMouseOver={() => {
        if (!hoverTimerRef.current) {
          hoverTimerRef.current = setTimeout(() => {
            setPreviewerVisible(true);
          }, 1000);
        }
      }}
      onMouseLeave={() => {
        clearTimeout(hoverTimerRef.current);
        hoverTimerRef.current = undefined;
        if (previewerVisible) {
          setPreviewerVisible(false);
        }
      }}
    >
      {previewerVisible && (
        <MediaPreviewer resourceId={2501} />
      )}
    </div>
  );
};
