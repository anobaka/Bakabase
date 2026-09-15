import type { FileNameModificationResult } from "./index";
import type { CSSProperties } from "react";

import React from "react";

import DiffHighlight from "./DiffHighlight";

interface PreviewItemProps {
  result: FileNameModificationResult;
  showFullPaths: boolean;
  commonPrefix: string;
  style?: CSSProperties;
  rowIndex?: number;
}

function directoryOf(path: string) {
  return path.slice(0, Math.max(path.lastIndexOf("/"), path.lastIndexOf("\\")) + 1);
}

const PreviewItem: React.FC<PreviewItemProps> = ({
  result,
  showFullPaths,
  commonPrefix,
  style,
  rowIndex,
}) => {
  const hasChanged = result.originalPath !== result.modifiedPath;
  const originalDirectory = directoryOf(result.originalPath);
  const modifiedDirectory = directoryOf(result.modifiedPath);
  const sharedDirectory =
    originalDirectory.startsWith(commonPrefix) && modifiedDirectory.startsWith(commonPrefix)
      ? commonPrefix
      : "";

  return (
    <div
      aria-rowindex={rowIndex}
      className={`grid grid-cols-[minmax(0,1fr)_minmax(0,1fr)] items-center gap-4 border-b border-default-100 px-3 py-2 text-xs hover:bg-default-50 ${hasChanged ? "text-default-700" : "text-default-400"}`}
      role="row"
      style={style}
    >
      {(["original", "modified"] as const).map((mode) => {
        const path = mode === "original" ? result.originalPath : result.modifiedPath;

        return (
          <div key={mode} className="min-w-0" role="cell" title={path}>
            <DiffHighlight
              className="block truncate leading-5"
              mode={mode}
              modified={result.modifiedFileName}
              original={result.originalFileName}
            />
            {showFullPaths && (
              <div className="truncate text-[10px] leading-4 text-default-500">
                <span className="text-default-400">{sharedDirectory}</span>
                <DiffHighlight
                  mode={mode}
                  modified={modifiedDirectory.slice(sharedDirectory.length)}
                  original={originalDirectory.slice(sharedDirectory.length)}
                />
              </div>
            )}
          </div>
        );
      })}
    </div>
  );
};

export default PreviewItem;
