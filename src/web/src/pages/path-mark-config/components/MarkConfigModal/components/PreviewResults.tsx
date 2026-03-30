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
  /** When true, show danger styling for results with missing property values */
  highlightMissingValues?: boolean;
  /** Property name to display for Property mark type */
  propertyName?: string;
  /** Media library name to display for MediaLibrary mark type (Fixed mode) */
  mediaLibraryName?: string;
};

const PreviewResults = ({ loading, results, resultsByPath, isMultiplePaths, error, markType, t, applyScope, highlightMissingValues, propertyName, mediaLibraryName }: Props) => {
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

  const renderResult = (result: PathMarkPreviewResult, idx: number) => {
    const pathSegments = result.path.split(/[/\\]/).filter(Boolean);
    const separator = result.path.includes("/") ? "/" : "\\";
    const isSub = result.isSubdirectoryExample;

    // For Resource type: highlight the resource layer in the path
    if (markType === PathMarkType.Resource && result.resourceLayerIndex !== null && result.resourceLayerIndex !== undefined) {
      return (
        <div key={idx} className={`text-xs break-all ${isSub ? "ml-4 text-default-400 italic" : "text-default-600"}`}>
          <div className="flex items-center gap-2 flex-wrap">
            {isSub && <span className="text-default-300">↳</span>}
            <span className="flex items-center gap-0.5 flex-wrap">
              {pathSegments.map((segment, segIdx) => {
                const isResourceLayer = segIdx === result.resourceLayerIndex;
                return (
                  <span key={segIdx} className="flex items-center gap-0.5">
                    {segIdx > 0 && <span className="text-default-400">{separator}</span>}
                    {isResourceLayer && !isSub ? (
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
            </span>
          </div>
        </div>
      );
    }

    // For Property type: show path, property name, and property value
    if (markType === PathMarkType.Property) {
      const hasValue = result.propertyValue !== null && result.propertyValue !== undefined && result.propertyValue !== "";
      const showDanger = highlightMissingValues && !hasValue;
      const hasProperty = !!propertyName;
      const showPropertyDanger = highlightMissingValues && !hasProperty;
      return (
        <div key={idx} className={`text-xs break-all ${isSub ? "ml-4 text-default-400 italic" : "text-default-600"}`}>
          <div className="flex items-center gap-2 flex-wrap">
            {isSub && <span className="text-default-300">↳</span>}
            <span className="break-all">
              {result.path}
            </span>
            {!isSub && showPropertyDanger ? (
              <Chip size="sm" color="danger" variant="flat" className="flex-shrink-0">
                <span className="font-semibold">{t("pathMarkConfig.label.targetProperty")}: {t("pathMarkConfig.status.notSet")}</span>
              </Chip>
            ) : !isSub && hasProperty ? (
              <Chip size="sm" color="default" variant="flat" className="flex-shrink-0">
                <span className="font-semibold">{propertyName}</span>
              </Chip>
            ) : null}
            {hasValue ? (
              <Chip size="sm" color={isSub ? "default" : "primary"} variant="flat" className="flex-shrink-0">
                <span className="font-bold">{result.propertyValue}</span>
              </Chip>
            ) : !isSub && showDanger ? (
              <Chip size="sm" color="danger" variant="flat" className="flex-shrink-0">
                <span className="font-semibold">{t("pathMarkConfig.label.propertyValue")}: {t("pathMarkConfig.status.notSet")}</span>
              </Chip>
            ) : null}
          </div>
        </div>
      );
    }

    // For MediaLibrary type: show path and media library name
    if (markType === PathMarkType.MediaLibrary) {
      // For Dynamic mode, propertyValue contains the extracted name; for Fixed mode, use resolved mediaLibraryName
      const displayName = result.propertyValue || mediaLibraryName;
      const hasValue = !!displayName;
      const showDanger = highlightMissingValues && !hasValue;
      return (
        <div key={idx} className={`text-xs break-all ${isSub ? "ml-4 text-default-400 italic" : "text-default-600"}`}>
          <div className="flex items-center gap-2 flex-wrap">
            {isSub && <span className="text-default-300">↳</span>}
            <span className="break-all">
              {result.path}
            </span>
            {hasValue ? (
              <Chip size="sm" color={isSub ? "default" : "secondary"} variant="flat" className="flex-shrink-0">
                <span className="font-semibold">{t("common.label.mediaLibrary")}: <span className="font-bold">{displayName}</span></span>
              </Chip>
            ) : !isSub && showDanger ? (
              <Chip size="sm" color="danger" variant="flat" className="flex-shrink-0">
                <span className="font-semibold">{t("common.label.mediaLibrary")}: {t("pathMarkConfig.status.notSet")}</span>
              </Chip>
            ) : null}
          </div>
        </div>
      );
    }

    // Fallback: show path only
    return (
      <div key={idx} className={`text-xs break-all ${isSub ? "ml-4 text-default-400 italic" : "text-default-600"}`}>
        {isSub && <span className="text-default-300">↳ </span>}
        <span>{result.path}</span>
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
