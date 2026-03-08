"use client";

import React, { useCallback, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  Button,
  Card,
  CardBody,
  Checkbox,
  Chip,
  Modal,
  Tabs,
  Tab,
  Input,
  Textarea,
  toast,
} from "@/components/bakaui";
import type { DestroyableProps } from "@/components/bakaui/types";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import AiFeatureButton from "@/components/AiFeatureButton";
import BApi from "@/sdk/BApi";
import type {
  BakabaseModulesAIServicesFileStructureAnalysisResult,
  BakabaseModulesAIServicesNamingConventionAnalysisResult,
  BakabaseModulesAIServicesFileNameCorrectionResult,
  BakabaseModulesAIServicesPathSimilarityGroupResult,
  BakabaseModulesAIServicesDirectoryStructureCorrectionResult,
  BakabaseModulesAIServicesFileOperation,
} from "@/sdk/Api";
import { AiFeature } from "@/sdk/constants";
import AiFeatureConfigShortcut from "@/components/AiFeatureConfigShortcut";
import FileOperationTree from "@/components/FileOperationTree";
import { RiRobot2Line } from "react-icons/ri";

type Props = {
  directoryPath: string;
  filePaths: string[];
} & DestroyableProps;

const AiAnalysisModal = ({ directoryPath, filePaths, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [pathsExpanded, setPathsExpanded] = useState(false);

  const [structureResult, setStructureResult] = useState<BakabaseModulesAIServicesFileStructureAnalysisResult | null>(null);
  const [namingResult, setNamingResult] = useState<BakabaseModulesAIServicesNamingConventionAnalysisResult | null>(null);
  const [nameCorrections, setNameCorrections] = useState<BakabaseModulesAIServicesFileNameCorrectionResult | null>(null);
  const [similarityResult, setSimilarityResult] = useState<BakabaseModulesAIServicesPathSimilarityGroupResult | null>(null);
  const [directoryCorrections, setDirectoryCorrections] = useState<BakabaseModulesAIServicesDirectoryStructureCorrectionResult | null>(null);

  const [loadingStructure, setLoadingStructure] = useState(false);
  const [loadingNaming, setLoadingNaming] = useState(false);
  const [loadingNameCorrections, setLoadingNameCorrections] = useState(false);
  const [loadingSimilarity, setLoadingSimilarity] = useState(false);
  const [loadingDirectoryCorrections, setLoadingDirectoryCorrections] = useState(false);

  const [targetConvention, setTargetConvention] = useState("");
  const [customGroupingLogic, setCustomGroupingLogic] = useState("");

  // Reference paths (exemplar paths that are already well-organized)
  const [referencePaths, setReferencePaths] = useState<Set<string>>(new Set());
  const [referenceExpanded, setReferenceExpanded] = useState(false);

  const toggleReferencePath = (path: string) => {
    setReferencePaths((prev) => {
      const next = new Set(prev);
      if (next.has(path)) {
        next.delete(path);
      } else {
        next.add(path);
      }
      return next;
    });
  };

  const referencePathsArray = referencePaths.size > 0 ? Array.from(referencePaths) : undefined;

  // Selected operations per tab
  const [selectedOps, setSelectedOps] = useState<Record<string, Set<number>>>({});
  const [applying, setApplying] = useState(false);

  // Which result modal is currently open
  const [resultModalTabKey, setResultModalTabKey] = useState<string | null>(null);

  const getSelectedSet = (tabKey: string): Set<number> => selectedOps[tabKey] ?? new Set();

  const setSelectedSet = (tabKey: string, set: Set<number>) => {
    setSelectedOps((prev) => ({ ...prev, [tabKey]: set }));
  };

  const toggleAll = (tabKey: string, operations: BakabaseModulesAIServicesFileOperation[]) => {
    setSelectedOps((prev) => {
      const current = prev[tabKey] ?? new Set();
      if (current.size === operations.length) {
        return { ...prev, [tabKey]: new Set() };
      }
      return { ...prev, [tabKey]: new Set(operations.map((_, i) => i)) };
    });
  };

  const selectAllOps = (tabKey: string, operations: BakabaseModulesAIServicesFileOperation[]) => {
    setSelectedOps((prev) => ({
      ...prev,
      [tabKey]: new Set(operations.map((_, i) => i)),
    }));
  };

  const handleApply = useCallback((tabKey: string, operations: BakabaseModulesAIServicesFileOperation[]) => {
    const selected = getSelectedSet(tabKey);
    if (selected.size === 0) return;

    const selectedOperations = operations
      .filter((_, i) => selected.has(i))
      .map((op, i) => ({ ...op, order: i }));

    createPortal(Modal, {
      defaultVisible: true,
      title: t<string>("fileProcessor.ai.applyOperations"),
      children: (
        <div className="flex flex-col gap-2">
          <p className="text-sm text-warning-600 font-medium">
            {t("fileProcessor.ai.applyWarning")}
          </p>
          <p className="text-sm text-default-500">
            {t("fileProcessor.ai.applyConfirm", { count: selectedOperations.length })}
          </p>
        </div>
      ),
      onOk: async () => {
        setApplying(true);
        try {
          const r = await BApi.ai.aiApplyFileOperations({ operations: selectedOperations });
          if (!r.code && r.data) {
            const { successCount, failureCount, errors } = r.data;
            if (failureCount > 0) {
              toast.warning(
                t<string>("fileProcessor.ai.applyPartialSuccess", {
                  success: successCount,
                  failure: failureCount,
                }),
              );
              if (errors && errors.length > 0) {
                console.error("Apply errors:", errors);
              }
            } else {
              toast.success(
                t<string>("fileProcessor.ai.applySuccess", { count: successCount }),
              );
            }
            // Close the AI analysis modal since file structure has changed
            onDestroyed?.();
          }
        } finally {
          setApplying(false);
        }
      },
    });
  }, [selectedOps, onDestroyed]);

  const analyzeStructure = async () => {
    if (!directoryPath) return;
    setLoadingStructure(true);
    try {
      const r = await BApi.ai.aiAnalyzeFileStructure({ directoryPath, referencePaths: referencePathsArray });
      if (!r.code && r.data) {
        setStructureResult(r.data);
        if (r.data.operations?.length) {
          selectAllOps("structure", r.data.operations);
        }
        setResultModalTabKey("structure");
      }
    } finally {
      setLoadingStructure(false);
    }
  };

  const analyzeNaming = async () => {
    if (filePaths.length === 0) return;
    setLoadingNaming(true);
    try {
      const r = await BApi.ai.aiAnalyzeNamingConvention({ filePaths, workingDirectory: directoryPath || undefined, referencePaths: referencePathsArray });
      if (!r.code && r.data) {
        setNamingResult(r.data);
        if (r.data.operations?.length) {
          selectAllOps("naming", r.data.operations);
        }
        setResultModalTabKey("naming");
      }
    } finally {
      setLoadingNaming(false);
    }
  };

  const suggestNames = async () => {
    if (filePaths.length === 0) return;
    setLoadingNameCorrections(true);
    try {
      const r = await BApi.ai.aiSuggestFileNameCorrections({
        filePaths,
        workingDirectory: directoryPath || undefined,
        targetConvention: targetConvention || undefined,
        referencePaths: referencePathsArray,
      });
      if (!r.code && r.data) {
        setNameCorrections(r.data);
        if (r.data.operations?.length) {
          selectAllOps("nameCorrections", r.data.operations);
        }
        setResultModalTabKey("nameCorrections");
      }
    } finally {
      setLoadingNameCorrections(false);
    }
  };

  const groupBySimilarity = async () => {
    if (filePaths.length === 0) return;
    setLoadingSimilarity(true);
    try {
      const r = await BApi.ai.aiGroupByPathSimilarity({
        filePaths,
        workingDirectory: directoryPath || undefined,
        customGroupingLogic: customGroupingLogic || undefined,
      });
      if (!r.code && r.data) {
        setSimilarityResult(r.data);
        if (r.data.operations?.length) {
          selectAllOps("similarity", r.data.operations);
        }
        setResultModalTabKey("similarity");
      }
    } finally {
      setLoadingSimilarity(false);
    }
  };

  const suggestDirectoryCorrections = async () => {
    if (!directoryPath) return;
    setLoadingDirectoryCorrections(true);
    try {
      const r = await BApi.ai.aiSuggestDirectoryCorrections({ directoryPath, referencePaths: referencePathsArray });
      if (!r.code && r.data) {
        setDirectoryCorrections(r.data);
        if (r.data.operations?.length) {
          selectAllOps("directoryCorrections", r.data.operations);
        }
        setResultModalTabKey("directoryCorrections");
      }
    } finally {
      setLoadingDirectoryCorrections(false);
    }
  };

  const renderStringList = (items: string[] | undefined, label: string) => {
    if (!items || items.length === 0) return null;
    return (
      <div className="flex flex-col gap-1">
        <span className="text-sm font-medium text-default-600">{label}</span>
        <ul className="list-disc list-inside text-sm text-default-500">
          {items.map((item, idx) => (
            <li key={idx}>{item}</li>
          ))}
        </ul>
      </div>
    );
  };

  const renderRawTextFallback = (rawText?: string) => {
    if (!rawText) return null;
    return (
      <Card className="border border-warning-200 rounded-md">
        <CardBody className="flex flex-col gap-2">
          <Chip size="sm" color="warning" variant="flat">
            {t("fileProcessor.ai.rawTextFallback")}
          </Chip>
          <pre className="whitespace-pre-wrap break-all text-sm text-default-500 bg-default-50 rounded-lg p-3 max-h-[400px] overflow-auto">
            {rawText}
          </pre>
        </CardBody>
      </Card>
    );
  };


  const getResultOperations = (tabKey: string): BakabaseModulesAIServicesFileOperation[] | undefined => {
    switch (tabKey) {
      case "structure": return structureResult?.operations;
      case "naming": return namingResult?.operations;
      case "nameCorrections": return nameCorrections?.operations;
      case "similarity": return similarityResult?.operations;
      case "directoryCorrections": return directoryCorrections?.operations;
      default: return undefined;
    }
  };

  const getResultModalTitle = (tabKey: string): string => {
    const titles: Record<string, string> = {
      structure: t("fileProcessor.ai.analyzeStructure"),
      naming: t("fileProcessor.ai.analyzeNaming"),
      nameCorrections: t("fileProcessor.ai.suggestNames"),
      similarity: t("fileProcessor.ai.groupSimilarity"),
      directoryCorrections: t("fileProcessor.ai.suggestDirectoryCorrections"),
    };
    return titles[tabKey] ?? "";
  };

  const hasResult = (tabKey: string): boolean => {
    switch (tabKey) {
      case "structure": return structureResult !== null;
      case "naming": return namingResult !== null;
      case "nameCorrections": return nameCorrections !== null;
      case "similarity": return similarityResult !== null;
      case "directoryCorrections": return directoryCorrections !== null;
      default: return false;
    }
  };

  const renderResultContent = (tabKey: string): React.ReactNode => {
    switch (tabKey) {
      case "structure": {
        if (!structureResult) return null;
        if (structureResult.rawText) return renderRawTextFallback(structureResult.rawText);
        return (
          <Card className="border border-default-200 rounded-md">
            <CardBody className="flex flex-col gap-2">
              {structureResult.summary && (
                <div className="flex flex-col gap-1">
                  <span className="text-sm font-medium text-default-600">{t("fileProcessor.ai.summary")}</span>
                  <p className="text-sm text-default-500">{structureResult.summary}</p>
                </div>
              )}
              <div className="flex gap-4">
                {structureResult.totalFiles != null && (
                  <Chip size="sm" variant="flat">{t("fileProcessor.ai.totalFiles")}: {structureResult.totalFiles}</Chip>
                )}
                {structureResult.totalDirectories != null && (
                  <Chip size="sm" variant="flat">{t("fileProcessor.ai.totalDirectories")}: {structureResult.totalDirectories}</Chip>
                )}
              </div>
              {renderStringList(structureResult.issues, t("fileProcessor.ai.issues"))}
              {renderStringList(structureResult.suggestions, t("fileProcessor.ai.suggestions"))}
            </CardBody>
          </Card>
        );
      }
      case "naming": {
        if (!namingResult) return null;
        if (namingResult.rawText) return renderRawTextFallback(namingResult.rawText);
        return (
          <Card className="border border-default-200 rounded-md">
            <CardBody className="flex flex-col gap-2">
              {namingResult.detectedPattern && (
                <div className="flex flex-col gap-1">
                  <span className="text-sm font-medium text-default-600">{t("fileProcessor.ai.detectedPattern")}</span>
                  <p className="text-sm text-default-500">{namingResult.detectedPattern}</p>
                </div>
              )}
              {namingResult.summary && (
                <div className="flex flex-col gap-1">
                  <span className="text-sm font-medium text-default-600">{t("fileProcessor.ai.summary")}</span>
                  <p className="text-sm text-default-500">{namingResult.summary}</p>
                </div>
              )}
              {renderStringList(namingResult.inconsistencies, t("fileProcessor.ai.inconsistencies"))}
              {renderStringList(namingResult.suggestions, t("fileProcessor.ai.suggestions"))}
            </CardBody>
          </Card>
        );
      }
      case "nameCorrections": {
        if (!nameCorrections) return null;
        if (nameCorrections.rawText) return renderRawTextFallback(nameCorrections.rawText);
        return (
          <Card className="border border-default-200 rounded-md">
            <CardBody className="flex flex-col gap-2">
              {nameCorrections.appliedConvention && (
                <div className="flex flex-col gap-1">
                  <span className="text-sm font-medium text-default-600">{t("fileProcessor.ai.appliedConvention")}</span>
                  <p className="text-sm text-default-500">{nameCorrections.appliedConvention}</p>
                </div>
              )}
              {nameCorrections.corrections && nameCorrections.corrections.filter(c => {
                const originalName = c.originalPath?.split(/[/\\]/).pop();
                return originalName !== c.suggestedName;
              }).length > 0 ? (
                <div className="flex flex-col gap-2">
                  <span className="text-sm font-medium text-default-600">{t("fileProcessor.ai.corrections")}</span>
                  <div className="flex flex-col gap-1">
                    {nameCorrections.corrections.filter(c => {
                      const originalName = c.originalPath?.split(/[/\\]/).pop();
                      return originalName !== c.suggestedName;
                    }).map((c, idx) => (
                      <div key={idx} className="flex flex-col gap-0.5 p-2 rounded bg-default-50">
                        <div className="flex items-center gap-2 text-sm">
                          <span className="text-default-400 line-through">{c.originalPath?.split(/[/\\]/).pop()}</span>
                          <span className="text-default-300">&rarr;</span>
                          <span className="text-primary font-medium">{c.suggestedName}</span>
                        </div>
                        {c.reason && <span className="text-xs text-default-400">{c.reason}</span>}
                      </div>
                    ))}
                  </div>
                </div>
              ) : (
                <p className="text-sm text-default-400">{t("fileProcessor.ai.noResults")}</p>
              )}
            </CardBody>
          </Card>
        );
      }
      case "similarity": {
        if (!similarityResult) return null;
        if (similarityResult.rawText) return renderRawTextFallback(similarityResult.rawText);
        return (
          <Card className="border border-default-200 rounded-md">
            <CardBody className="flex flex-col gap-2">
              {similarityResult.groups && similarityResult.groups.length > 0 ? (
                <div className="flex flex-col gap-3">
                  {similarityResult.groups.map((group, idx) => (
                    <div key={idx} className="flex flex-col gap-1">
                      <div className="flex items-center gap-2">
                        <Chip size="sm" color="primary" variant="flat">{group.groupName}</Chip>
                        {group.reason && <span className="text-xs text-default-400">{group.reason}</span>}
                      </div>
                      <ul className="list-disc list-inside text-sm text-default-500 ml-2">
                        {group.paths?.map((p, pIdx) => (
                          <li key={pIdx} className="truncate">{p}</li>
                        ))}
                      </ul>
                    </div>
                  ))}
                </div>
              ) : (
                <p className="text-sm text-default-400">{t("fileProcessor.ai.noResults")}</p>
              )}
            </CardBody>
          </Card>
        );
      }
      case "directoryCorrections": {
        if (!directoryCorrections) return null;
        if (directoryCorrections.rawText) return renderRawTextFallback(directoryCorrections.rawText);
        return (
          <Card className="border border-default-200 rounded-md">
            <CardBody className="flex flex-col gap-2">
              {directoryCorrections.summary && (
                <div className="flex flex-col gap-1">
                  <span className="text-sm font-medium text-default-600">{t("fileProcessor.ai.summary")}</span>
                  <p className="text-sm text-default-500">{directoryCorrections.summary}</p>
                </div>
              )}
              {directoryCorrections.corrections && directoryCorrections.corrections.length > 0 ? (
                <div className="flex flex-col gap-2">
                  <span className="text-sm font-medium text-default-600">{t("fileProcessor.ai.corrections")}</span>
                  <div className="flex flex-col gap-1">
                    {directoryCorrections.corrections.map((c, idx) => (
                      <div key={idx} className="flex flex-col gap-0.5 p-2 rounded bg-default-50">
                        <div className="flex items-center gap-2 text-sm">
                          <span className="text-default-400">{c.originalPath}</span>
                          <span className="text-default-300">&rarr;</span>
                          <span className="text-primary font-medium">{c.suggestedPath}</span>
                        </div>
                        {c.reason && <span className="text-xs text-default-400">{c.reason}</span>}
                      </div>
                    ))}
                  </div>
                </div>
              ) : (
                <p className="text-sm text-default-400">{t("fileProcessor.ai.noResults")}</p>
              )}
            </CardBody>
          </Card>
        );
      }
      default:
        return null;
    }
  };

  const renderResultModalFooter = () => {
    if (!resultModalTabKey) return false;
    const operations = getResultOperations(resultModalTabKey);
    if (!operations?.length) return false;

    const selected = getSelectedSet(resultModalTabKey);
    const allSelected = selected.size === operations.length;

    return (
      <div className="flex items-center justify-between w-full">
        <Checkbox
          size="sm"
          isSelected={allSelected}
          onValueChange={() => toggleAll(resultModalTabKey, operations)}
        >
          <span className="text-xs">{t("fileProcessor.ai.selectAll")}</span>
        </Checkbox>
        <Button
          size="sm"
          color="primary"
          isLoading={applying}
          isDisabled={selected.size === 0}
          onPress={() => handleApply(resultModalTabKey, operations)}
        >
          {t("fileProcessor.ai.apply")} ({selected.size}/{operations.length})
        </Button>
      </div>
    );
  };

  const renderViewResultsButton = (tabKey: string) => {
    if (!hasResult(tabKey)) return null;
    return (
      <Button
        size="sm"
        variant="flat"
        onPress={() => setResultModalTabKey(tabKey)}
      >
        {t("fileProcessor.ai.viewResults")}
      </Button>
    );
  };

  return (
    <>
      <Modal
        defaultVisible
        footer={false}
        size="lg"
        title={
          <div className="flex items-center gap-2">
            <RiRobot2Line className="text-lg" />
            {t("fileProcessor.ai.title")}
            <AiFeatureConfigShortcut feature={AiFeature.FileProcessor} />
          </div>
        }
        onDestroyed={onDestroyed}
      >
        <div className="flex flex-col gap-1 mb-2 px-1">
          {directoryPath && (
            <div className="text-xs text-default-400">
              {t("fileProcessor.ai.workingDirectory")}: <span className="text-default-500">{directoryPath}</span>
            </div>
          )}
          {filePaths.length > 0 ? (
            <div className="flex flex-col gap-1">
              <button
                type="button"
                className="text-xs text-primary cursor-pointer hover:underline text-left"
                onClick={() => setPathsExpanded(!pathsExpanded)}
              >
                {t("fileProcessor.ai.selectedPaths", { count: filePaths.length })}
                <span className="ml-1">{pathsExpanded ? "▴" : "▾"}</span>
              </button>
              {pathsExpanded && (
                <div className="max-h-[120px] overflow-y-auto rounded bg-default-50 p-2">
                  {filePaths.map((p, idx) => (
                    <div key={idx} className="text-xs text-default-500 truncate" title={p}>
                      {p}
                    </div>
                  ))}
                </div>
              )}
            </div>
          ) : (
            <div className="text-xs text-warning-500">
              {t("fileProcessor.ai.noPathsSelected")}
            </div>
          )}
        </div>
        {filePaths.length > 0 && (
          <div className="flex flex-col gap-1 mb-2 px-1">
            <button
              type="button"
              className="text-xs text-default-500 cursor-pointer hover:underline text-left"
              onClick={() => setReferenceExpanded(!referenceExpanded)}
            >
              {t("fileProcessor.ai.referencePaths")}
              {referencePaths.size > 0 && (
                <Chip size="sm" variant="flat" className="ml-1">{referencePaths.size}</Chip>
              )}
              <span className="ml-1">{referenceExpanded ? "▴" : "▾"}</span>
            </button>
            <div className="text-xs text-default-400">
              {t("fileProcessor.ai.referencePaths.description")}
            </div>
            {referenceExpanded && (
              <div className="max-h-[160px] overflow-y-auto rounded bg-default-50 p-2 flex flex-col gap-0.5">
                {filePaths.map((p, idx) => (
                  <Checkbox
                    key={idx}
                    size="sm"
                    isSelected={referencePaths.has(p)}
                    onValueChange={() => toggleReferencePath(p)}
                  >
                    <span className="text-xs text-default-500 truncate" title={p}>{p}</span>
                  </Checkbox>
                ))}
              </div>
            )}
          </div>
        )}
        <Tabs variant="underlined">
          {/* Structure Analysis */}
          <Tab key="structure" title={t("fileProcessor.ai.analyzeStructure")}>
            <div className="flex flex-col gap-3 p-2">
              <p className="text-sm text-default-400">{t("fileProcessor.ai.analyzeStructure.description")}</p>
              <div className="flex items-center gap-2">
                <AiFeatureButton
                  feature={AiFeature.FileProcessor}
                  color="primary"
                  size="sm"
                  isLoading={loadingStructure}
                  onPress={analyzeStructure}
                  isDisabled={!directoryPath}
                >
                  {loadingStructure ? t("fileProcessor.ai.analyzing") : t("fileProcessor.ai.analyzeStructure")}
                </AiFeatureButton>
                {renderViewResultsButton("structure")}
              </div>
            </div>
          </Tab>

          {/* Naming Convention Analysis */}
          <Tab key="naming" title={t("fileProcessor.ai.analyzeNaming")}>
            <div className="flex flex-col gap-3 p-2">
              <p className="text-sm text-default-400">{t("fileProcessor.ai.analyzeNaming.description")}</p>
              <div className="flex items-center gap-2">
                <AiFeatureButton
                  feature={AiFeature.FileProcessor}
                  color="primary"
                  size="sm"
                  isLoading={loadingNaming}
                  onPress={analyzeNaming}
                  isDisabled={filePaths.length === 0}
                >
                  {loadingNaming ? t("fileProcessor.ai.analyzing") : t("fileProcessor.ai.analyzeNaming")}
                </AiFeatureButton>
                {renderViewResultsButton("naming")}
              </div>
            </div>
          </Tab>

          {/* File Name Corrections */}
          <Tab key="nameCorrections" title={t("fileProcessor.ai.suggestNames")}>
            <div className="flex flex-col gap-3 p-2">
              <p className="text-sm text-default-400">{t("fileProcessor.ai.suggestNames.description")}</p>
              <Input
                size="sm"
                label={t("fileProcessor.ai.targetConvention")}
                value={targetConvention}
                onValueChange={setTargetConvention}
              />
              <div className="flex items-center gap-2">
                <AiFeatureButton
                  feature={AiFeature.FileProcessor}
                  color="primary"
                  size="sm"
                  isLoading={loadingNameCorrections}
                  onPress={suggestNames}
                  isDisabled={filePaths.length === 0}
                >
                  {loadingNameCorrections ? t("fileProcessor.ai.analyzing") : t("fileProcessor.ai.suggestNames")}
                </AiFeatureButton>
                {renderViewResultsButton("nameCorrections")}
              </div>
            </div>
          </Tab>

          {/* Path Similarity Grouping */}
          <Tab key="similarity" title={t("fileProcessor.ai.groupSimilarity")}>
            <div className="flex flex-col gap-3 p-2">
              <p className="text-sm text-default-400">{t("fileProcessor.ai.groupSimilarity.description")}</p>
              <Textarea
                size="sm"
                label={t("fileProcessor.ai.customGroupingLogic")}
                placeholder={t("fileProcessor.ai.customGroupingLogicPlaceholder")}
                value={customGroupingLogic}
                onValueChange={setCustomGroupingLogic}
                minRows={2}
                maxRows={4}
              />
              <div className="flex items-center gap-2">
                <AiFeatureButton
                  feature={AiFeature.FileProcessor}
                  color="primary"
                  size="sm"
                  isLoading={loadingSimilarity}
                  onPress={groupBySimilarity}
                  isDisabled={filePaths.length === 0}
                >
                  {loadingSimilarity ? t("fileProcessor.ai.analyzing") : t("fileProcessor.ai.groupSimilarity")}
                </AiFeatureButton>
                {renderViewResultsButton("similarity")}
              </div>
            </div>
          </Tab>

          {/* Directory Structure Corrections */}
          <Tab key="directoryCorrections" title={t("fileProcessor.ai.suggestDirectoryCorrections")}>
            <div className="flex flex-col gap-3 p-2">
              <p className="text-sm text-default-400">{t("fileProcessor.ai.suggestDirectoryCorrections.description")}</p>
              <div className="flex items-center gap-2">
                <AiFeatureButton
                  feature={AiFeature.FileProcessor}
                  color="primary"
                  size="sm"
                  isLoading={loadingDirectoryCorrections}
                  onPress={suggestDirectoryCorrections}
                  isDisabled={!directoryPath}
                >
                  {loadingDirectoryCorrections ? t("fileProcessor.ai.analyzing") : t("fileProcessor.ai.suggestDirectoryCorrections")}
                </AiFeatureButton>
                {renderViewResultsButton("directoryCorrections")}
              </div>
            </div>
          </Tab>
        </Tabs>
      </Modal>

      {resultModalTabKey && (
        <Modal
          defaultVisible
          size="7xl"
          title={getResultModalTitle(resultModalTabKey)}
          footer={renderResultModalFooter()}
          onClose={() => setResultModalTabKey(null)}
          onDestroyed={() => setResultModalTabKey(null)}
        >
          <div className="flex flex-col gap-4">
            {renderResultContent(resultModalTabKey)}
            {(() => {
              const ops = getResultOperations(resultModalTabKey);
              if (!ops?.length) return null;
              return (
                <div className="flex flex-col gap-2">
                  <span className="text-sm font-medium text-default-600">{t("fileProcessor.ai.operations")}</span>
                  <FileOperationTree
                    operations={ops}
                    selectedIndices={getSelectedSet(resultModalTabKey)}
                    onSelectionChange={(newSet) => setSelectedSet(resultModalTabKey, newSet)}
                  />
                </div>
              );
            })()}
          </div>
        </Modal>
      )}
    </>
  );
};

AiAnalysisModal.displayName = "AiAnalysisModal";

export default AiAnalysisModal;
