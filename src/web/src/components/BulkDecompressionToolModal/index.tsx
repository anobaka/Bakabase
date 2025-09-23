"use client";

import type { DestroyableProps } from "@/components/bakaui/types";

import React, { useCallback, useEffect, useRef, useState, useMemo } from "react";
import { useTranslation } from "react-i18next";
import { Table, Column, AutoSizer, CellMeasurer, CellMeasurerCache } from "react-virtualized";
import "react-virtualized/styles.css";

import {
  AiOutlineCheckCircle,
  AiOutlineDelete,
  AiOutlineEnter,
  AiOutlineFieldTime,
  AiOutlineFolder,
  AiOutlineFolderAdd,
  AiOutlineFolderOpen,
  AiOutlineRightCircle,
  AiOutlineWarning,
  AiOutlineFileSync,
} from "react-icons/ai";
import { CgNotes } from "react-icons/cg";
import { MdOutlineCreateNewFolder } from "react-icons/md";
import { LoadingOutlined } from "@ant-design/icons";
import { ImMoveUp } from "react-icons/im";

import {
  Button,
  Chip,
  Modal,
  Spinner,
  toast,
  NumberInput,
  Checkbox,
  Tooltip,
  CircularProgress,
} from "@/components/bakaui";
import FileSystemEntryIcon from "@/components/FileSystemEntryIcon";
import { humanFileSize } from "@/components/utils";
import {
  IconType,
  CompressedFileDetectionResultStatus,
  DecompressionStatus,
} from "@/sdk/constants";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import BApi from "@/sdk/BApi";
import { BakabaseServiceModelsViewCompressedFileDetectionResultViewModel, BakabaseServiceModelsViewDecompressionResultViewModel } from "@/sdk/Api";
import BetaChip from "../Chips/BetaChip";
import _ from "lodash";

// Backend view model types
type CompressedFileDetectionResultDto = BakabaseServiceModelsViewCompressedFileDetectionResultViewModel;

type DecompressionResultDto = BakabaseServiceModelsViewDecompressionResultViewModel;

type TableItem = Omit<CompressedFileDetectionResultDto, 'status' | 'message'> & Omit<DecompressionResultDto, 'status' | 'percentage' | 'message'> & {
  index: number;
  detectionStatus?: CompressedFileDetectionResultStatus;
  detectionMessage?: string;
  decompressionStatus?: DecompressionStatus;
  decompressionPercentage?: number;
  decompressionMessage?: string;
};

type Props = { paths: string[] } & DestroyableProps;

type Operation = {
  decompressToNewFolder: boolean;
  deleteAfterDecompression: boolean;
  moveToParent: boolean;
  overwriteExistFiles: boolean;
};

const DetectCompressedFilesModal = ({ paths = [], onDestroyed }: Props) => {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(true);
  const [detecting, setDetecting] = useState(false);
  const includeUnknownFilesRef = useRef<boolean>(false);
  const unknownFilesMinMbRef = useRef<number | undefined>(0);
  const [includeUnknownFiles, setIncludeUnknownFiles] = useState<boolean>(
    includeUnknownFilesRef.current,
  );
  const [unknownFilesMinMb, setUnknownFilesMinMb] = useState<number | undefined>(
    unknownFilesMinMbRef.current,
  );
  const [onFailureContinue, setOnFailureContinue] = useState(true);
  const abortDetectingRef = useRef<AbortController | null>(null);
  const abortDecompressingRef = useRef<AbortController | null>(null);
  const [globalOperation, setGlobalOperation] = useState<Operation>({
    decompressToNewFolder: true,
    deleteAfterDecompression: false,
    moveToParent: false,
    overwriteExistFiles: false,
  });
  const [operationMap, setOperationMap] = useState<Record<string, Operation>>({});
  const { createPortal } = useBakabaseContext();

  const [results, setResults] = useState<TableItem[]>([]);
  const tableRef = useRef<Table>(null);

  // Create a cache for dynamic row heights
  const cache = useMemo(
    () =>
      new CellMeasurerCache({
        fixedWidth: true,
        defaultHeight: 150,
        minHeight: 50,
      }),
    [],
  );

  // Clear cache and recompute heights when results change
  useEffect(() => {
    if (results.length > 0) {
      cache.clearAll();
      // Force table to recompute row heights
      if (tableRef.current) {
        tableRef.current.recomputeRowHeights();
        tableRef.current.forceUpdateGrid();
      }
    }
  }, [results, cache]);

  const startDetecting = useCallback(async () => {
    if (!paths || paths.length === 0) return;
    setDetecting(true);
    setResults([]);
    const controller = new AbortController();

    abortDetectingRef.current = controller;

    try {
      const url = BApi.file.detectCompressedFilesUrl();
      const resp = await fetch(url, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          paths: paths,
          includeUnknownFiles: includeUnknownFilesRef.current,
          unknownFilesMinMb: includeUnknownFilesRef.current
            ? unknownFilesMinMbRef.current
            : undefined,
        }),
        signal: controller.signal,
      });

      if (!resp.ok || !resp.body) {
        throw new Error(`Request failed: ${resp.status}`);
      }

      const reader = resp.body.getReader();
      const decoder = new TextDecoder();
      let buffer = "";

      while (true) {
        const { value, done } = await reader.read();

        if (done) break;
        buffer += decoder.decode(value, { stream: true });

        let idx;

        while ((idx = buffer.indexOf("\n")) >= 0) {
          const line = buffer.slice(0, idx).trim();

          buffer = buffer.slice(idx + 1);
          if (!line) continue;
          const result = JSON.parse(line) as CompressedFileDetectionResultDto;

          setResults((prev) => {
            const idx = prev.findIndex((r) => r.key === result.key);
            const next = [...prev];

            if (idx >= 0) {
              // preserve local UI overrides
              const existed = next[idx] || {};

              // Map backend response to frontend unified type
              const mapped: Partial<TableItem> = {
                ...Object.fromEntries(
                  Object.entries(result).filter(
                    ([k, v]) =>
                      k !== "status" &&
                      k !== "message" &&
                      v !== null &&
                      v !== undefined &&
                      v !== "",
                  ),
                ),
              };

              // Map status and message with detection prefix
              if (result.status !== undefined) {
                mapped.detectionStatus = result.status;
              }
              if (result.message !== undefined && result.message !== "") {
                mapped.detectionMessage = result.message;
              }

              next[idx] = {
                ...existed,
                ...mapped,
              };
              // Clear cache for the updated row
              cache.clear(idx, 0);
            } else {
              const mapped: TableItem = {
                ...result,
                detectionMessage: result.message,
                detectionStatus: result.status,
                index: next.length,
              };
              next.push(mapped);
            }

            return next;
          });

          // Recompute heights after state update
          setTimeout(() => {
            if (tableRef.current) {
              tableRef.current.recomputeRowHeights();
              tableRef.current.forceUpdate();
            }
          }, 0);
        }
      }
    } catch (e) {
      toast.danger(e instanceof Error ? e.message : "Unknown error");
      // silently end on abort or error
    } finally {
      setDetecting(false);
      abortDetectingRef.current = null;
    }
  }, [paths, cache]);

  const stopDetecting = useCallback(() => {
    abortDetectingRef.current?.abort();
    abortDetectingRef.current = null;
    setDetecting(false);
  }, []);

  const close = useCallback(() => {
    stopDetecting();
    setVisible(false);
  }, [stopDetecting]);

  const startDecompressing = useCallback(async () => {
    const detectedItems = results.filter(
      (r) => r.detectionStatus === CompressedFileDetectionResultStatus.Complete,
    );

    if (detectedItems.length === 0) return;

    // Check for dangerous operation combination
    const dangerousItems = detectedItems.filter((item) => {
      const op = operationMap[item.key] ?? globalOperation;
      return op.moveToParent && !op.decompressToNewFolder;
    });

    if (dangerousItems.length > 0) {
      const confirmed = await new Promise<boolean>((resolve) => {
        createPortal(Modal, {
          defaultVisible: true,
          size: "lg",
          title: (
            <div className="flex items-center gap-2 text-warning">
              <AiOutlineWarning className="text-2xl" />
              {t("Warning")}
            </div>
          ),
          footer: {
            actions: ['ok', 'cancel'],
            okProps: {
              children: t("Continue anyway"),
              color: "danger",
            },
            cancelProps: {
              children: t("Cancel"),
              color: "default",
              variant: "light",
            }
          },
          onOk: () => {
            resolve(true);
          },
          onClose: () => {
            resolve(false);
          },
          children: (
            <div className="flex flex-col gap-3">
              <p className="text-base">
                {t(
                  "You have set 'Move to parent directory' without 'Extract to new directory' for {{count}} item(s). This is a dangerous combination that may cause unexpected behavior:",
                  { count: dangerousItems.length }
                )}
              </p>
              <ul className="list-disc list-inside space-y-1 text-sm text-danger">
                <li>{t("All file system entries in the same directory will be moved to parent folder")}</li>
                <li>{t("Pending compressed files may be moved before decompression, causing failures")}</li>
                <li>{t("The final file structure may be completely messed up")}</li>
              </ul>
              <div className="bg-primary-50 dark:bg-primary-900/20 border-l-4 border-primary p-3 rounded">
                <p className="text-sm text-foreground-600">
                  <span className="font-semibold">{t("Tip:")}</span>{" "}
                  {t("However, for some cases we recommend users to make this choice. Please make sure you clearly understand what will happen.")}
                </p>
              </div>
              <p className="text-base font-semibold">
                {t("Are you sure you want to continue?")}
              </p>
            </div>
          )
        });
      });

      if (!confirmed) {
        return;
      }
    }

    const controller = new AbortController();

    abortDecompressingRef.current = controller;

    try {
      const url = BApi.file.decompressCompressedFilesUrl();
      const items = detectedItems.map((item) => {
        const op = operationMap[item.key] ?? globalOperation;

        return {
          key: item.key,
          directory: item.directory,
          files: item.files,
          password: item.password,
          decompressToNewFolder: op.decompressToNewFolder,
          deleteAfterDecompression: op.deleteAfterDecompression,
          moveToParent: op.moveToParent,
          overwriteExistFiles: op.overwriteExistFiles,
        };
      });

      const resp = await fetch(url, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          items,
          onFailureContinue,
        }),
        signal: controller.signal,
      });

      if (!resp.ok || !resp.body) {
        throw new Error(`Request failed: ${resp.status}`);
      }

      const reader = resp.body.getReader();
      const decoder = new TextDecoder();
      let buffer = "";

      while (true) {
        const { value, done } = await reader.read();

        if (done) break;
        buffer += decoder.decode(value, { stream: true });

        let idx;

        while ((idx = buffer.indexOf("\n")) >= 0) {
          const line = buffer.slice(0, idx).trim();

          buffer = buffer.slice(idx + 1);
          if (!line) continue;

          const update = JSON.parse(line) as DecompressionResultDto;

          setResults((prev) => {
            const idx = prev.findIndex((r) => r.key === update.key);

            if (idx >= 0) {
              const next = [...prev];

              // Map backend response to frontend unified type
              const mapped: Partial<TableItem> = {
                ...Object.fromEntries(
                  Object.entries(update).filter(
                    ([k, v]) =>
                      k !== "status" &&
                      k !== "percentage" &&
                      k !== "message" &&
                      v !== null &&
                      v !== undefined &&
                      v !== "",
                  ),
                ),
              };

              // Map decompression fields with prefix
              if (update.status !== undefined) {
                mapped.decompressionStatus = update.status;
              }
              if (update.percentage !== undefined) {
                mapped.decompressionPercentage = update.percentage;
              }
              if (update.message !== undefined && update.message !== "") {
                mapped.decompressionMessage = update.message;
              }

              next[idx] = {
                ...next[idx],
                ...mapped,
              };
              // Clear cache for the updated row
              cache.clear(idx, 0);

              return next;
            }

            return prev;
          });

          // Recompute heights after state update
          setTimeout(() => {
            if (tableRef.current) {
              tableRef.current.recomputeRowHeights();
              tableRef.current.forceUpdate();
            }
          }, 0);
        }
      }

      toast.success(t("Decompression task completed"));
    } catch (e) {
      if (e instanceof Error && e.name !== "AbortError") {
        toast.danger(e.message);
      }
    } finally {
      abortDecompressingRef.current = null;
    }
  }, [results, operationMap, globalOperation, onFailureContinue, t, cache]);

  useEffect(() => {
    // auto start
    // startDetecting();

    return () => stopDetecting();
  }, []);

  const renderDetectionStatus = useCallback((result: TableItem) => {
    switch (result.detectionStatus) {
      case CompressedFileDetectionResultStatus.Init:
        return (
          <Chip color="default" size="sm" variant="light">
            <AiOutlineFieldTime className="text-lg" />
          </Chip>
        );
      case CompressedFileDetectionResultStatus.Inprogress:
        return (
          <Chip color="primary" variant="light">
            <div className="flex items-center gap-1">
              <Spinner color="primary" labelColor="foreground" size="sm" />
              {result.decompressionPercentage !== undefined && (
                <span>{result.decompressionPercentage}%</span>
              )}
            </div>
          </Chip>
        );
      case CompressedFileDetectionResultStatus.Complete:
        return (
          <Chip color="success" size="sm" variant="light">
            <AiOutlineCheckCircle className="text-lg" />
          </Chip>
        );
      case CompressedFileDetectionResultStatus.Error:
        return (
          <Chip color="danger" size="sm" variant="light">
            <AiOutlineWarning className="text-lg" />
          </Chip>
        );
    }
  }, []);

  const renderDecompressionStatus = useCallback((result: TableItem) => {
    switch (result.decompressionStatus) {
      case DecompressionStatus.Pending:
        return (
          <Chip color="default" size="sm" variant="light">
            <AiOutlineFieldTime className="text-lg" />
          </Chip>
        );
      case DecompressionStatus.Decompressing:
        return (
          // <Chip color="default" variant="light">
          //   <Spinner color="default" labelColor="foreground" size="sm" />
          // </Chip>
          <CircularProgress size='sm' showValueLabel={true} value={result.decompressionPercentage} />
        );
      case DecompressionStatus.Success:
        return (
          <Chip color="success" size="sm" variant="light">
            <AiOutlineCheckCircle className="text-lg" />
          </Chip>
        );
      case DecompressionStatus.Error:
        return (
          <Chip color="danger" size="sm" variant="light">
            <AiOutlineWarning className="text-lg" />
          </Chip>
        );
    }
  }, []);

  const renderOperations = useCallback(
    (
      decompressToNewFolder: boolean,
      setDecompressToNewFolder: (value: boolean) => void,
      deleteAfterDecompression: boolean,
      setDeleteAfterDecompression: (value: boolean) => void,
      moveToParent: boolean,
      setMoveToParent: (value: boolean) => void,
      overwriteExistFiles: boolean,
      setOverwriteExistFiles: (value: boolean) => void,
      showTooltip: boolean,
    ) => {
      const decompressToNewFolderBtn = (
        <Button
          isIconOnly
          className={decompressToNewFolder ? "" : "opacity-30"}
          color={decompressToNewFolder ? "primary" : "default"}
          size="sm"
          variant="light"
          onPress={() => setDecompressToNewFolder(!decompressToNewFolder)}
        >
          <AiOutlineFolderAdd className="text-xl" />
        </Button>
      );
      const deleteAfterDecompressionBtn = (
        <Button
          isIconOnly
          className={deleteAfterDecompression ? "" : "opacity-30"}
          color={deleteAfterDecompression ? "danger" : "default"}
          size="sm"
          variant="light"
          onPress={() => setDeleteAfterDecompression(!deleteAfterDecompression)}
        >
          <AiOutlineDelete className="text-xl" />
        </Button>
      );
      const moveToParentBtn = (
        <Button
          isIconOnly
          className={moveToParent ? "" : "opacity-30"}
          color={moveToParent ? "success" : "default"}
          size="sm"
          variant="light"
          onPress={() => setMoveToParent(!moveToParent)}
        >
          <ImMoveUp className="text-xl" />
        </Button>
      );
      const overwriteExistFilesBtn = (
        <Button
          isIconOnly
          className={overwriteExistFiles ? "" : "opacity-30"}
          color={overwriteExistFiles ? "warning" : "default"}
          size="sm"
          variant="light"
          onPress={() => setOverwriteExistFiles(!overwriteExistFiles)}
        >
          <AiOutlineFileSync className="text-xl" />
        </Button>
      );

      return (
        <div className="flex items-center gap-0">
          {showTooltip ? (
            <>
              <Tooltip className="max-w-[320px]" color="primary" content={t("Extract to new directory(we will use file name without extension as the new directory name)")}>
                {decompressToNewFolderBtn}
              </Tooltip>
              <Tooltip className="max-w-[320px]" color="danger" content={t("Delete the source file after decompression")}>
                {deleteAfterDecompressionBtn}
              </Tooltip>
              <Tooltip className="max-w-[320px]" color="success" content={t("Move all files and folders located in the extraction target directory to the parent directory of the compressed file. Note that any files or folders not contained in the compressed file, but present in the directory where the compressed file is being extracted, will also be moved to the parent directory.")}>
                {moveToParentBtn}
              </Tooltip>
              <Tooltip className="max-w-[320px]" color="warning" content={t("Overwrite existing files")}>
                {overwriteExistFilesBtn}
              </Tooltip>
            </>
          ) : (
            [decompressToNewFolderBtn, deleteAfterDecompressionBtn, moveToParentBtn, overwriteExistFilesBtn]
          )}
        </div>
      );
    },
    [],
  );

  console.log(results);

  const detectedItems = results.filter(
    (r) => r.detectionStatus == CompressedFileDetectionResultStatus.Complete,
  );

  const someNotDecompressToNewFolder = _.values(operationMap).some(a => !a.decompressToNewFolder);
  const someNotDeleteAfterDecompression = _.values(operationMap).some(a => !a.deleteAfterDecompression);
  const someNotMoveToParent = _.values(operationMap).some(a => !a.moveToParent);
  const someNotOverwriteExistFiles = _.values(operationMap).some(a => !a.overwriteExistFiles);

  return (
    <Modal
      className={"max-w-[95vw] max-h-[90vh]"}
      footer={false}
      size={"xl"}
      title={(
        <div className="flex items-center gap-1">
          {t<string>("Compressed files handling")}
          <BetaChip />
        </div>
      )}
      visible={visible}
      onClose={() => {
        if (detecting || abortDecompressingRef.current) {
          if (!confirm(t<string>("A task is running. Close and cancel it?"))) return;
          stopDetecting();
          abortDecompressingRef.current?.abort();
          abortDecompressingRef.current = null;
        }
        close();
      }}
      onDestroyed={onDestroyed}
    >
      <div className="flex flex-col gap-3">
        <div className="flex items-center gap-2">
          <div className="flex items-center gap-1">
            <Checkbox
              isSelected={includeUnknownFiles}
              size="sm"
              onValueChange={(selected) => {
                includeUnknownFilesRef.current = selected;
                setIncludeUnknownFiles(selected);

                if (!selected) {
                  unknownFilesMinMbRef.current = undefined;
                  setUnknownFilesMinMb(undefined);
                } else if (unknownFilesMinMbRef.current === undefined) {
                  const defaultValue = 10;

                  unknownFilesMinMbRef.current = defaultValue;
                  setUnknownFilesMinMb(defaultValue);
                }
              }}
            >
              {t<string>("Include unknown extensions")}
            </Checkbox>
            <NumberInput
              isClearable
              className={"w-[400px]"}
              isDisabled={!includeUnknownFiles}
              label={t<string>("Unknown extensions min size (MB)")}
              placeholder={t<string>("Optional")}
              size="sm"
              value={unknownFilesMinMb}
              onValueChange={(v) => {
                unknownFilesMinMbRef.current = v;
                setUnknownFilesMinMb(v);

                if (v === undefined) {
                  includeUnknownFilesRef.current = false;
                  setIncludeUnknownFiles(false);
                } else if (!includeUnknownFilesRef.current) {
                  includeUnknownFilesRef.current = true;
                  setIncludeUnknownFiles(true);
                }
              }}
            />
          </div>
          <div>
            {!detecting ? (
              <Button
                color="primary"
                onPress={() => {
                  startDetecting();
                }}
              >
                {t("Detect compressed files")}
              </Button>
            ) : (
              <Button color="danger" variant="light" onPress={stopDetecting}>
                <LoadingOutlined className="text-lg" />
                {t("Stop")}
              </Button>
            )}
          </div>
        </div>

        <div className="grow h-[65vh] max-h-[65vh] border border-gray-200 dark:border-gray-700 rounded-lg overflow-hidden">
          <AutoSizer>
            {({ height, width }) => (
              <Table
                ref={tableRef}
                deferredMeasurementCache={cache}
                headerClassName="bg-default-100 dark:bg-default-50/10 font-medium border-b border-divider text-sm"
                headerHeight={40}
                height={height}
                noRowsRenderer={() => (
                  <div className="flex items-center justify-center h-full text-gray-500">
                    {t<string>("No compressed files detected")}
                  </div>
                )}
                rowClassName={({ index }) =>
                  index >= 0
                    ? "transition-colors hover:bg-primary-50/30 dark:hover:bg-primary-900/20 border-b border-divider/50"
                    : ""
                }
                rowCount={results.length}
                rowGetter={({ index }) => results[index]}
                rowHeight={cache.rowHeight}
                width={width}
              >
                <Column
                  cellRenderer={({ rowData }) => (
                    <div className="flex items-center justify-center px-2 py-2">
                      {rowData.index + 1}
                    </div>
                  )}
                  dataKey="index"
                  headerClassName="flex items-center justify-center"
                  label={
                    <div className="h-8 flex items-center justify-center">{t<string>("#")}</div>
                  }
                  width={60}
                />
                <Column
                  cellRenderer={({ rowData: result, parent, rowIndex }) => (
                    <CellMeasurer
                      key={result.key}
                      cache={cache}
                      columnIndex={0}
                      parent={parent}
                      rowIndex={rowIndex}
                    >
                      <div
                        className="flex flex-col gap-0.5 py-2 px-2 overflow-hidden"
                        style={{ width: "100%" }}
                      >
                        <Chip size="sm">
                          <div className="flex items-center gap-1">
                            <AiOutlineFolder className="text-lg" />
                            {result.directory}
                          </div>
                        </Chip>
                        <div className="flex items-start">
                          <div>
                            <Chip size="sm" variant="light">
                              <AiOutlineEnter className="text-lg scale-x-[-1]" />
                            </Chip>
                          </div>
                          <div className="flex items-center gap-1">
                            <div className="flex flex-wrap gap-1 max-h-[100px] overflow-y-auto">
                              {result.files?.map((f: string, fileIdx: number) => (
                                <Chip
                                  key={f}
                                  color={
                                    result.status === CompressedFileDetectionResultStatus.Error
                                      ? "danger"
                                      : "default"
                                  }
                                  size="sm"
                                  variant="light"
                                >
                                  <div className="flex items-center gap-1">
                                    <FileSystemEntryIcon
                                      path={`${result.directory}/${f}`}
                                      size={18}
                                      type={IconType.Dynamic}
                                    />
                                    <span>{f}</span>
                                    {typeof result.fileSizes?.[fileIdx] === "number" && (
                                      <Chip size="sm" variant="light">
                                        {humanFileSize(result.fileSizes[fileIdx] ?? 0, false, 2)}
                                      </Chip>
                                    )}
                                  </div>
                                </Chip>
                              ))}
                            </div>
                            {result.status === CompressedFileDetectionResultStatus.Complete && (
                              <>
                                <div>
                                  <AiOutlineRightCircle className="text-lg" />
                                </div>
                                {(operationMap[result.key] ?? globalOperation)
                                  ?.decompressToNewFolder && (
                                    <div>
                                      <Chip color="success" size="sm" variant="light">
                                        <div className="flex items-center gap-1">
                                          <MdOutlineCreateNewFolder className="text-lg" />
                                          {result.decompressToDirName}
                                        </div>
                                      </Chip>
                                    </div>
                                  )}
                                {result.contentSampleGroups &&
                                  result.contentSampleGroups.length > 0 && (
                                    <div className="flex flex-wrap gap-1 items-center">
                                      /
                                      {result.contentSampleGroups.map(
                                        (
                                          g: { isFile: boolean; count: number; samples: string[] },
                                          gi: number,
                                        ) => {
                                          const restFileCount = g.count - (g.samples?.length ?? 0);
                                          const sampleFilesText = g.samples?.join(", ");

                                          return (
                                            <Chip
                                              key={g.samples?.[0] ?? gi}
                                              size="sm"
                                              variant="flat"
                                            >
                                              <div className="flex items-center gap-1">
                                                <FileSystemEntryIcon
                                                  size={18}
                                                  type={
                                                    g.isFile ? IconType.Dynamic : IconType.Directory
                                                  }
                                                />
                                                {restFileCount > 0
                                                  ? t<string>(
                                                    "{{sampleFilesText}} and {{restFileCount}} more",
                                                  )
                                                  : sampleFilesText}
                                              </div>
                                            </Chip>
                                          );
                                        },
                                      )}
                                    </div>
                                  )}
                              </>
                            )}
                          </div>
                        </div>
                      </div>
                    </CellMeasurer>
                  )}
                  dataKey="files"
                  flexGrow={1}
                  headerClassName="flex items-center justify-start"
                  label={<div className="h-8 flex items-center">{t<string>("Files")}</div>}
                  width={width * 0.4}
                />
                <Column
                  cellRenderer={({ rowData: result }) => (
                    <div className="flex items-center px-2 py-2">
                      {result.password && (
                        <Chip size="sm" startContent={<AiOutlineCheckCircle className="text-lg" />}>
                          {result.password}
                        </Chip>
                      )}
                      {result.candidatePasswords?.map((cp: string) => (
                        <Chip key={cp} size="sm">
                          {cp}
                        </Chip>
                      ))}
                      {result.wrongPasswords?.map((wp: string) => (
                        <Chip key={wp} className="line-through" size="sm" variant="light">
                          {wp}
                        </Chip>
                      ))}
                    </div>
                  )}
                  dataKey="password"
                  headerClassName="flex items-center justify-start"
                  label={<div className="h-8 flex items-center">{t<string>("Password")}</div>}
                  width={150}
                />
                <Column
                  cellRenderer={({ rowData: result }) => (
                    <div className="flex items-center justify-center px-2 py-2">
                      {renderDetectionStatus(result)}
                      {(result.detectionMessage) && (
                        <Button
                          isIconOnly
                          size="sm"
                          variant="light"
                          onPress={() => {
                            createPortal(Modal, {
                              defaultVisible: true,
                              size: "xl",
                              title: t("Message"),
                              children: <pre className="whitespace-pre-wrap">{result.detectionMessage}</pre>,
                            });
                          }}
                        >
                          <CgNotes className="text-lg" />
                        </Button>
                      )}
                    </div>
                  )}
                  dataKey="detectionStatus"
                  headerClassName="flex items-center justify-center"
                  label={
                    <div className="h-8 flex items-center justify-center">
                      {t<string>("Detection")}
                    </div>
                  }
                  width={120}
                />
                <Column
                  cellRenderer={({ rowData: result }) => (
                    <div className="flex items-center justify-center px-2 py-2">
                      {renderDecompressionStatus(result)}
                      {(result.decompressionMessage) && (
                        <Button
                          isIconOnly
                          size="sm"
                          variant="light"
                          onPress={() => {
                            createPortal(Modal, {
                              defaultVisible: true,
                              size: "xl",
                              title: t("Message"),
                              children: <pre className="whitespace-pre-wrap">{result.decompressionMessage}</pre>,
                            });
                          }}
                        >
                          <CgNotes className="text-lg" />
                        </Button>
                      )}
                    </div>
                  )}
                  dataKey="decompressionStatus"
                  headerClassName="flex items-center justify-center"
                  label={
                    <div className="h-8 flex items-center justify-center">
                      {t<string>("Decompression")}
                    </div>
                  }
                  width={120}
                />
                <Column
                  cellRenderer={({ rowData: result }) => (
                    <div className="flex items-center gap-2 px-2 py-2">
                      {result.detectionStatus === CompressedFileDetectionResultStatus.Complete &&
                        renderOperations(
                          operationMap[result.key]?.decompressToNewFolder ??
                          globalOperation.decompressToNewFolder,
                          (v) => {
                            setOperationMap((prev) => ({
                              ...prev,
                              [result.key]: { ...prev[result.key], decompressToNewFolder: v },
                            }));
                          },
                          operationMap[result.key]?.deleteAfterDecompression ??
                          globalOperation.deleteAfterDecompression,
                          (v) => {
                            setOperationMap((prev) => ({
                              ...prev,
                              [result.key]: { ...prev[result.key], deleteAfterDecompression: v },
                            }));
                          },
                          operationMap[result.key]?.moveToParent ?? globalOperation.moveToParent,
                          (v) => {
                            setOperationMap((prev) => ({
                              ...prev,
                              [result.key]: { ...prev[result.key], moveToParent: v },
                            }));
                          },
                          operationMap[result.key]?.overwriteExistFiles ?? globalOperation.overwriteExistFiles,
                          (v) => {
                            setOperationMap((prev) => ({
                              ...prev,
                              [result.key]: { ...prev[result.key], overwriteExistFiles: v },
                            }));
                          },
                          false,
                        )}
                      <Button
                        isIconOnly
                        size="sm"
                        variant="light"
                        onPress={() => {
                          BApi.tool.openFileOrDirectory({
                            path: result.directory,
                          });
                        }}
                      >
                        <AiOutlineFolderOpen className="text-lg" />
                      </Button>
                    </div>
                  )}
                  dataKey="operations"
                  headerClassName="flex items-center justify-start"
                  label={
                    <div className="h-8 flex items-center gap-2 px-2">
                      {renderOperations(
                        someNotDecompressToNewFolder ? false : globalOperation.decompressToNewFolder,
                        (v) => {
                          setGlobalOperation((prev) => ({ ...prev, decompressToNewFolder: v }));
                          setOperationMap((prev) => {
                            for (const key in prev) {
                              prev[key] = { ...prev[key], decompressToNewFolder: v };
                            }

                            return prev;
                          });
                        },
                        someNotDeleteAfterDecompression ? false : globalOperation.deleteAfterDecompression,
                        (v) => {
                          setGlobalOperation((prev) => ({ ...prev, deleteAfterDecompression: v }));
                          setOperationMap((prev) => {
                            for (const key in prev) {
                              prev[key] = { ...prev[key], deleteAfterDecompression: v };
                            }

                            return prev;
                          });
                        },
                        someNotMoveToParent ? false : globalOperation.moveToParent,
                        (v) => {
                          setGlobalOperation((prev) => ({ ...prev, moveToParent: v }));
                          setOperationMap((prev) => {
                            for (const key in prev) {
                              prev[key] = { ...prev[key], moveToParent: v };
                            }

                            return prev;
                          });
                        },
                        someNotOverwriteExistFiles ? false : globalOperation.overwriteExistFiles,
                        (v) => {
                          setGlobalOperation((prev) => ({ ...prev, overwriteExistFiles: v }));
                          setOperationMap((prev) => {
                            for (const key in prev) {
                              prev[key] = { ...prev[key], overwriteExistFiles: v };
                            }

                            return prev;
                          });
                        },
                        true,
                      )}
                    </div>
                  }
                  width={260}
                />
              </Table>
            )}
          </AutoSizer>
        </div>

        <div className="flex items-center gap-2 ">
          <div className="flex flex-col gap-1">
            <Checkbox
              checked={onFailureContinue}
              size="sm"
              type="checkbox"
              onChange={(e) => setOnFailureContinue(e.currentTarget.checked)}
            >
              {t<string>("Continue on failure")}
            </Checkbox>
          </div>
          <div>
            <Button
              className={detecting || detectedItems.length == 0 ? "cursor-not-allowed" : ""}
              color="primary"
              isDisabled={detecting || detectedItems.length == 0}
              onPress={() => {
                startDecompressing();
              }}
            >
              {detecting
                ? t("Waiting for detection to complete...")
                : detectedItems.length == 0
                  ? t("No compressed files detected")
                  : t("Decompress all successfully detected")}
            </Button>
          </div>
        </div>
      </div>
    </Modal>
  );
};

DetectCompressedFilesModal.displayName = "DetectCompressedFilesModal";

export default DetectCompressedFilesModal;
