import type { FileNameModificationResult } from "./index";

import React from "react";
import { AiOutlineRightCircle } from "react-icons/ai";

import { Chip } from "../bakaui";

interface PreviewItemProps {
  result: FileNameModificationResult;
  showFullPaths: boolean;
  commonPrefix: string;
}

const PreviewItem: React.FC<PreviewItemProps> = ({
  result,
  showFullPaths,
  commonPrefix,
}) => {
  // 根据 showFullPaths 决定显示内容
  const originalDisplay = showFullPaths
    ? result.originalPath
    : result.originalFileName;
  const modifiedDisplay = showFullPaths
    ? result.modifiedPath
    : result.modifiedFileName;

  return (
    <div
      className="flex items-center gap-1 flex-wrap"
      style={{ fontSize: "12px" }}
    >
      <span className="opacity-60" title={originalDisplay}>
        {originalDisplay}
      </span>
      <Chip className="opacity-80" size="sm" variant="light">
        <AiOutlineRightCircle className="text-base" />
      </Chip>
      <span className="text-blue-600" title={modifiedDisplay}>
        {result.modifiedFileName}
      </span>
    </div>
  );
};

export default PreviewItem;
