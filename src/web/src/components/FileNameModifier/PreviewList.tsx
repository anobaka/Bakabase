"use client";

import type { FileNameModificationResult } from "./index";

import React from "react";
import { useTranslation } from "react-i18next";

import PreviewItem from "./PreviewItem";

export type { FileNameModificationResult };

interface PreviewListProps {
  results: FileNameModificationResult[];
  showFullPaths: boolean;
  commonPrefix: string;
}

const PreviewList: React.FC<PreviewListProps> = ({
  results,
  showFullPaths,
  commonPrefix,
}) => {
  const { t } = useTranslation();

  if (!results.length) {
    return (
      <div className="text-gray-400 text-center py-5">
        {t<string>("FileNameModifier.NoPreviewResults")}
      </div>
    );
  }

  return (
    <div className="preview-list min-h-0 max-h-full overflow-y-auto">
      {results.map((result, idx) => (
        <PreviewItem
          key={idx}
          commonPrefix={commonPrefix}
          result={result}
          showFullPaths={showFullPaths}
        />
      ))}
    </div>
  );
};

export default PreviewList;
