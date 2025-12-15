"use client";

import { Spinner } from "@/components/bakaui";

type Props = {
  loading: boolean;
  results: string[];
  error: string | null;
  t: (key: string) => string;
};

const PreviewResults = ({ loading, results, error, t }: Props) => {
  if (loading) {
    return (
      <div className="flex items-center gap-2 text-sm text-default-500 py-2">
        <Spinner size="sm" />
        <span>{t("Loading preview...")}</span>
      </div>
    );
  }

  if (error) {
    return (
      <div className="text-sm text-danger py-2">{error}</div>
    );
  }

  if (results.length === 0) {
    return (
      <div className="bg-warning-50 text-warning-600 rounded p-2 text-xs">
        {t("No matches found. Please check your configuration.")}
      </div>
    );
  }

  return (
    <div className="bg-default-100 rounded p-2">
      <div className="text-xs text-default-500 mb-1">{t("Preview matches")}:</div>
      <div className="flex flex-col gap-1 max-h-32 overflow-y-auto">
        {results.map((path, idx) => (
          <div key={idx} className="text-xs text-default-600 break-all">
            {path}
          </div>
        ))}
      </div>
    </div>
  );
};

PreviewResults.displayName = "PreviewResults";

export default PreviewResults;
