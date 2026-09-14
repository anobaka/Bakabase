"use client";

import type { FileNameModificationResult } from "./index";
import type { ListRowProps } from "react-virtualized";

import React from "react";
import { useTranslation } from "react-i18next";
import { AutoSizer, List } from "react-virtualized";

import PreviewItem from "./PreviewItem";

export type { FileNameModificationResult };

interface PreviewListProps {
  results: FileNameModificationResult[];
  showFullPaths: boolean;
  commonPrefix: string;
  isLoading?: boolean;
}

export const PREVIEW_ROW_HEIGHT = 56;

const PreviewList: React.FC<PreviewListProps> = ({
  results,
  showFullPaths,
  commonPrefix,
  isLoading,
}) => {
  const { t } = useTranslation();

  if (isLoading) {
    return (
      <div
        className="flex h-full min-h-32 items-center justify-center px-4 text-sm text-default-400"
        role="status"
      >
        {t<string>("FileNameModifier.Loading")}
      </div>
    );
  }

  if (!results.length) {
    return (
      <div
        className="flex h-full min-h-32 items-center justify-center px-4 text-sm text-default-400"
        role="status"
      >
        {t<string>("FileNameModifier.NoPreviewResults")}
      </div>
    );
  }

  const rowRenderer = ({ index, key, style }: ListRowProps) => {
    const result = results[index];

    return (
      <PreviewItem
        key={key}
        commonPrefix={commonPrefix}
        result={result}
        rowIndex={index + 2}
        showFullPaths={showFullPaths}
        style={style}
      />
    );
  };

  return (
    <div
      aria-rowcount={results.length + 1}
      className="preview-list flex h-full min-h-0 min-w-0 flex-col overflow-hidden"
      role="table"
    >
      <div
        aria-rowindex={1}
        className="grid shrink-0 grid-cols-[minmax(0,1fr)_minmax(0,1fr)] gap-4 border-b border-default-200 bg-default-50 px-3 py-2 text-xs font-medium text-default-500"
        role="row"
      >
        <span className="min-w-0 truncate" role="columnheader">
          {t<string>("fileNameModifier.preview.originalName")}
        </span>
        <span className="min-w-0 truncate" role="columnheader">
          {t<string>("fileNameModifier.preview.modifiedName")}
        </span>
      </div>
      <div className="min-h-0 min-w-0 flex-1">
        <AutoSizer>
          {({ width, height }) => (
            <List
              height={height || 300}
              overscanRowCount={5}
              role="rowgroup"
              rowCount={results.length}
              rowHeight={PREVIEW_ROW_HEIGHT}
              rowRenderer={rowRenderer}
              style={{ overflowX: "hidden" }}
              width={width || 400}
            />
          )}
        </AutoSizer>
      </div>
    </div>
  );
};

export default PreviewList;
