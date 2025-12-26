import type { FileNameModificationResult } from "./index";

import React, { CSSProperties } from "react";

import DiffHighlight from "./DiffHighlight";

interface PreviewItemProps {
  result: FileNameModificationResult;
  showFullPaths: boolean;
  commonPrefix: string;
  style?: CSSProperties;
}

const PreviewItem: React.FC<PreviewItemProps> = ({
  result,
  showFullPaths,
  commonPrefix,
  style,
}) => {
  // 根据 showFullPaths 决定显示内容
  const originalDisplay = showFullPaths
    ? result.originalPath
    : result.originalFileName;
  const modifiedDisplay = showFullPaths
    ? result.modifiedPath
    : result.modifiedFileName;

  const hasChanged = originalDisplay !== modifiedDisplay;

  return (
    <div
      className="flex items-center gap-2 py-1 px-2 hover:bg-default-100 rounded"
      style={{ fontSize: "12px", ...style }}
      title={hasChanged ? `${originalDisplay} → ${modifiedDisplay}` : originalDisplay}
    >
      {hasChanged ? (
        <DiffHighlight
          className="flex-1 min-w-0 truncate"
          modified={modifiedDisplay}
          original={originalDisplay}
        />
      ) : (
        <span className="opacity-60 flex-1 min-w-0 truncate">
          {originalDisplay}
        </span>
      )}
    </div>
  );
};

export default PreviewItem;
