import React from 'react';
import type { FileNameModificationResult } from './index';
import PreviewItem from './PreviewItem';
import { useTranslation } from 'react-i18next';

export type { FileNameModificationResult };

interface PreviewListProps {
  results: FileNameModificationResult[];
  showFullPaths: boolean;
  commonPrefix: string;
}

const PreviewList: React.FC<PreviewListProps> = ({ results, showFullPaths, commonPrefix }) => {
  const { t } = useTranslation();
  if (!results.length) {
    return <div className="text-gray-400 text-center py-5">{t('FileNameModifier.NoPreviewResults')}</div>;
  }
  return (
    <div className="preview-list min-h-0 max-h-full overflow-y-auto">
      {results.map((result, idx) => (
        <PreviewItem
          key={idx}
          result={result}
          showFullPaths={showFullPaths}
          commonPrefix={commonPrefix}
        />
      ))}
    </div>
  );
};

export default PreviewList; 