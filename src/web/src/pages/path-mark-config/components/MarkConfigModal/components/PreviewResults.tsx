"use client";

import type { PreviewResultsByPath, PathMarkPreviewResult } from "../hooks/usePreview";

import { Spinner, Chip } from "@/components/bakaui";
import { PathMarkType, PathMarkApplyScope } from "@/sdk/constants";

type Props = {
  loading: boolean;
  results: PathMarkPreviewResult[];
  resultsByPath?: PreviewResultsByPath[];
  isMultiplePaths?: boolean;
  error: string | null;
  markType: PathMarkType;
  t: (key: string) => string;
  applyScope?: PathMarkApplyScope;
};

const PreviewResults = ({
  loading,
  results,
  resultsByPath,
  isMultiplePaths,
  error,
  markType,
  t,
  applyScope,
}: Props) => {
  const isResource = markType === PathMarkType.Resource;
  const renderResult = (result: PathMarkPreviewResult, index: number) => (
    <div
      key={`${result.path}-${index}`}
      className="rounded-xl border border-default-200 bg-default-50/60 p-3"
    >
      <div className="mb-1 text-[11px] font-medium text-default-400">
        {t(
          isResource ? "pathMarkConfig.preview.matchedPath" : "pathMarkConfig.preview.resourcePath",
        )}
      </div>
      <div className="break-all text-xs font-medium leading-relaxed text-default-700">
        {result.path}
      </div>
      {isResource ? (
        <div className="mt-2 text-xs text-primary-600">
          {result.resourceSegmentName && (
            <Chip color="success" size="sm" variant="flat">
              {result.resourceSegmentName}
            </Chip>
          )}
          {applyScope === PathMarkApplyScope.MatchedAndSubdirectories && (
            <span className="ml-1">{t("pathMarkConfig.label.andAllSubdirectories")}</span>
          )}
        </div>
      ) : (
        <>
          {result.valueSourcePath && (
            <div className="mt-3 border-t border-default-200 pt-2">
              <div className="text-[11px] text-default-400">
                {t("pathMarkConfig.preview.valueSource")}
              </div>
              <div className="mt-1 break-all font-mono text-[11px] leading-relaxed text-default-500">
                {result.valueSourcePath}
              </div>
            </div>
          )}
          <div className="mt-3 flex flex-wrap items-center gap-2">
            <span className="text-xs text-default-400">
              {t(
                markType === PathMarkType.MediaLibrary
                  ? "common.label.mediaLibrary"
                  : "pathMarkConfig.label.propertyValue",
              )}
            </span>
            {result.propertyValue !== null && result.propertyValue !== undefined ? (
              <Chip
                color={markType === PathMarkType.MediaLibrary ? "secondary" : "primary"}
                size="sm"
                variant="flat"
              >
                {result.propertyValue}
              </Chip>
            ) : (
              <span className="text-xs text-default-400">
                {t("pathMarkConfig.preview.noValue")}
              </span>
            )}
          </div>
        </>
      )}
    </div>
  );

  return (
    <div aria-busy={loading} aria-live="polite">
      {loading ? (
        <div className="flex min-h-40 items-center justify-center gap-2 text-sm text-default-500">
          <Spinner size="sm" />
          {t("pathMarkConfig.status.loadingPreview")}
        </div>
      ) : error ? (
        <div
          className="rounded-xl bg-danger-50 p-3 text-xs leading-relaxed text-danger"
          role="alert"
        >
          {t(error)}
        </div>
      ) : results.length === 0 ? (
        <div className="rounded-xl border border-dashed border-default-300 px-4 py-8 text-center text-xs leading-relaxed text-default-500">
          {t("pathMarkConfig.warning.noMatches")}
        </div>
      ) : (
        <div className="flex max-h-[58vh] flex-col gap-3 overflow-y-auto pr-1">
          {isMultiplePaths && resultsByPath?.length
            ? resultsByPath.map((group) => (
                <div key={group.path} className="flex flex-col gap-2">
                  <div className="break-all text-xs font-medium text-default-400">{group.path}</div>
                  {group.results.map(renderResult)}
                </div>
              ))
            : results.map(renderResult)}
        </div>
      )}
    </div>
  );
};

export default PreviewResults;
