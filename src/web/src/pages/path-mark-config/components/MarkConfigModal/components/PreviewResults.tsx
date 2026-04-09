"use client";

import type { PreviewResultsByPath, PathMarkPreviewResult } from "../hooks/usePreview";
import { Spinner, Chip } from "@/components/bakaui";
import { PathMarkType, PathMarkApplyScope } from "@/sdk/constants";
import { ResourceTerm } from "@/components/Chips/Terms";

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

const PreviewResults = ({ loading, results, resultsByPath, isMultiplePaths, error, markType, t, applyScope }: Props) => {
  const showSubdirectoriesSuffix = applyScope === PathMarkApplyScope.MatchedAndSubdirectories;
  if (loading) {
    return (
      <div className="flex items-center gap-2 text-sm text-default-500 py-2">
        <Spinner size="sm" />
        <span>{t("pathMarkConfig.status.loadingPreview")}</span>
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
        {t("pathMarkConfig.warning.noMatches")}
      </div>
    );
  }

  const renderSubdirectoriesSuffix = () => {
    if (!showSubdirectoriesSuffix) return null;
    return (
      <span className="text-primary-500 italic ml-1">
        {t("pathMarkConfig.label.andAllSubdirectories")}
      </span>
    );
  };

  const renderResult = (result: PathMarkPreviewResult, idx: number) => {
    const pathSegments = result.path.split(/[/\\]/).filter(Boolean);
    const separator = result.path.includes("/") ? "/" : "\\";

    // For Resource type: highlight the resource layer in the path
    if (markType === PathMarkType.Resource && result.resourceLayerIndex !== null && result.resourceLayerIndex !== undefined) {
      return (
        <div key={idx} className="text-xs text-default-600 break-all">
          <div className="flex items-center gap-2 flex-wrap">
            <span className="flex items-center gap-0.5 flex-wrap">
              {pathSegments.map((segment, segIdx) => {
                const isResourceLayer = segIdx === result.resourceLayerIndex;
                return (
                  <span key={segIdx} className="flex items-center gap-0.5">
                    {segIdx > 0 && <span className="text-default-400">{separator}</span>}
                    {isResourceLayer ? (
                    <span className="flex items-center gap-1">
                      <Chip
                        size="sm"
                        color="success"
                        variant="flat"
                        className="font-semibold"
                      >
                        <ResourceTerm size="sm" />
                      </Chip>
                      <span>{segment}</span>
                    </span>
                    ) : (
                      <span>{segment}</span>
                    )}
                  </span>
                );
              })}
              {renderSubdirectoriesSuffix()}
            </span>
          </div>
        </div>
      );
    }

    // For Property type: show path and property value prominently
    if (markType === PathMarkType.Property) {
      return (
        <div key={idx} className="text-xs text-default-600 break-all">
          <div className="flex items-center gap-2 flex-wrap">
            <span className="break-all">
              {result.path}
              {renderSubdirectoriesSuffix()}
            </span>
            {result.propertyValue !== null && result.propertyValue !== undefined && (
              <Chip
                size="sm"
                color="primary"
                variant="flat"
                className="flex-shrink-0"
              >
                <span className="font-semibold">{t("pathMarkConfig.label.propertyValue")}: <span className="font-bold">{result.propertyValue}</span></span>
              </Chip>
            )}
          </div>
        </div>
      );
    }

    // For MediaLibrary type: show path and media library value
    if (markType === PathMarkType.MediaLibrary) {
      return (
        <div key={idx} className="text-xs text-default-600 break-all">
          <div className="flex items-center gap-2 flex-wrap">
            <span className="break-all">
              {result.path}
              {renderSubdirectoriesSuffix()}
            </span>
            {result.propertyValue !== null && result.propertyValue !== undefined && (
              <Chip
                size="sm"
                color="secondary"
                variant="flat"
                className="flex-shrink-0"
              >
                <span className="font-semibold">{t("common.label.mediaLibrary")}: <span className="font-bold">{result.propertyValue}</span></span>
              </Chip>
            )}
          </div>
        </div>
      );
    }

    // Fallback: show path only
    return (
      <div key={idx} className="text-xs text-default-600 break-all">
        <span>
          {result.path}
          {renderSubdirectoriesSuffix()}
        </span>
      </div>
    );
  };

  // Show grouped results when multiple paths
  if (isMultiplePaths && resultsByPath && resultsByPath.length > 0) {
    return (
      <div className="bg-default-100 rounded p-2">
        <div className="text-xs text-default-500 mb-1">{t("pathMarkConfig.label.previewMatches")}:</div>
        <div className="flex flex-col gap-2 max-h-48 overflow-y-auto">
          {resultsByPath.map((group, groupIdx) => (
            <div key={groupIdx} className="border-l-2 border-primary-200 pl-2">
              <div className="text-xs text-primary-600 font-medium mb-1 break-all">
                {group.path}
              </div>
              <div className="flex flex-col gap-1">
                {group.results.map((result, idx) => renderResult(result, idx))}
              </div>
            </div>
          ))}
        </div>
      </div>
    );
  }

  return (
    <div className="bg-default-100 rounded p-2">
      <div className="text-xs text-default-500 mb-1">{t("pathMarkConfig.label.previewMatches")}:</div>
      <div className="flex flex-col gap-1 max-h-32 overflow-y-auto">
        {results.map((result, idx) => renderResult(result, idx))}
      </div>
    </div>
  );
};

PreviewResults.displayName = "PreviewResults";

export default PreviewResults;
