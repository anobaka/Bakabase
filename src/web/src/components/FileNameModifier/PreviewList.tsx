"use client";

import type { FileNameModificationResult } from "./index";

import React from "react";
import { useTranslation } from "react-i18next";
import { AutoSizer, List, ListRowProps } from "react-virtualized";

import PreviewItem from "./PreviewItem";

export type { FileNameModificationResult };

interface PreviewListProps {
  results: FileNameModificationResult[];
  showFullPaths: boolean;
  commonPrefix: string;
  isLoading?: boolean;
}

const ROW_HEIGHT = 32;

const PreviewList: React.FC<PreviewListProps> = ({
  results,
  showFullPaths,
  commonPrefix,
  isLoading,
}) => {
  const { t } = useTranslation();

  if (isLoading) {
    return (
      <div className="text-gray-400 text-center py-5">
        {t<string>("FileNameModifier.Loading")}
      </div>
    );
  }

  if (!results.length) {
    return (
      <div className="text-gray-400 text-center py-5">
        {t<string>("FileNameModifier.NoPreviewResults")}
      </div>
    );
  }

  const rowRenderer = ({ index, key, style }: ListRowProps) => {
    const result = results[index];
    return (
      <PreviewItem
        key={result.originalPath}
        commonPrefix={commonPrefix}
        result={result}
        showFullPaths={showFullPaths}
        style={style}
      />
    );
  };

  return (
    <div className="preview-list min-h-0 h-full">
      <AutoSizer>
        {({ width, height }) => (
          <List
            height={height || 300}
            overscanRowCount={5}
            rowCount={results.length}
            rowHeight={ROW_HEIGHT}
            rowRenderer={rowRenderer}
            width={width || 400}
          />
        )}
      </AutoSizer>
    </div>
  );
};

export default PreviewList;
