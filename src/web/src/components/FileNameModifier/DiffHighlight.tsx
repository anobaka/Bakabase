"use client";

import React from "react";
import { computeDiff } from "./utils";

interface DiffHighlightProps {
  original: string;
  modified: string;
  className?: string;
}

const DiffHighlight: React.FC<DiffHighlightProps> = ({
  original,
  modified,
  className = "",
}) => {
  const diff = computeDiff(original, modified);

  // 如果没有变化，直接显示原文
  if (original === modified) {
    return <span className={className}>{original}</span>;
  }

  return (
    <span className={className}>
      <span>{diff.commonPrefix}</span>
      {diff.removedPart && (
        <span className="bg-red-200 text-red-800 line-through dark:bg-red-900 dark:text-red-200">
          {diff.removedPart}
        </span>
      )}
      {diff.addedPart && (
        <span className="bg-green-200 text-green-800 dark:bg-green-900 dark:text-green-200">
          {diff.addedPart}
        </span>
      )}
      <span>{diff.commonSuffix}</span>
    </span>
  );
};

export default DiffHighlight;
