"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { Entry } from "@/core/models/FileExplorer/Entry";

import React, { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import { AiOutlineCheckCircle, AiOutlineCloseCircle, AiOutlineDelete, AiOutlineEnter, AiOutlineFieldTime, AiOutlineFolder, AiOutlineFolderAdd, AiOutlineInfoCircle, AiOutlineRightCircle, AiOutlineWarning } from "react-icons/ai";
import {
  Autocomplete,
  AutocompleteItem,
  Button,
  Chip,
  Input,
  Modal,
  Spinner,
  toast,
  NumberInput, Table, TableColumn, TableRow, TableCell,
  TableHeader,
  TableBody,
  Checkbox,
  Select,
  RadioGroup,
  Radio,
  Tooltip
} from "@/components/bakaui";
import { CgNotes } from "react-icons/cg";
import envConfig from "@/config/env";
import FileSystemEntryIcon from "@/components/FileSystemEntryIcon";
import { IconType } from "@/sdk/constants";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

import { MdOutlineCreateNewFolder } from "react-icons/md";
import { LoadingOutlined } from "@ant-design/icons";
import { ImMoveUp } from "react-icons/im";

enum ExtractActionAfterDecompression {
  None = 0,
  InnerSecondLayer = 1,
  OuterFirstLayerRemoveParent = 2,
}

enum CompressedFileDetectionResultStatus {
  Init = 1,
  Inprogress,
  Complete,
  Error,
}

type CompressedFileDetectionResult = {
  key: string;
  status?: CompressedFileDetectionResultStatus;
  message?: string;
  directory?: string;
  groupKey?: string;
  files?: string[];
  password?: string | null;
  candidatePasswords?: string[];
  wrongPasswords?: string[];
  passwordCandidates?: string[];
  decompressToDirName?: string;
  contentSampleGroups?: { isFile: boolean; count: number; samples: string[] }[];
};

type TableItem = CompressedFileDetectionResult & {
  index: number;
};

type Props = { paths: string[] } & DestroyableProps;

type Operation = {
  decompressToNewFolder: boolean;
  deleteAfterDecompression: boolean;
  moveToParent: boolean;
};

const DetectCompressedFilesModal = ({ paths = [], onDestroyed }: Props) => {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(true);
  const [detecting, setDetecting] = useState(false);
  const [sizeThresholdMb, setSizeThresholdMb] = useState<number | undefined>(undefined);
  const [onFailureContinue, setOnFailureContinue] = useState(true);
  const abortDetectingRef = useRef<AbortController | null>(null);
  const abortDecompressingRef = useRef<AbortController | null>(null);
  const [globalOperation, setGlobalOperation] = useState<Operation>({
    decompressToNewFolder: true,
    deleteAfterDecompression: false,
    moveToParent: false,
  });
  const [operationMap, setOperationMap] = useState<Record<string, Operation>>({});
  const { createPortal } = useBakabaseContext();

  const [results, setResults] = useState<TableItem[]>([]);

  const startDetecting = useCallback(async () => {
    if (!paths || paths.length === 0) return;
    setDetecting(true);
    setResults([]);
    const controller = new AbortController();

    abortDetectingRef.current = controller;

    try {
      const url = `${envConfig.apiEndpoint}/file/decompression/detect`;
      const resp = await fetch(url, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          paths: paths,
          includeUnknownFilesLargerThanMb: sizeThresholdMb,
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
          const result = JSON.parse(line) as CompressedFileDetectionResult;

          setResults((prev) => {
            const idx = prev.findIndex((r) => r.key === result.key);
            const next = [...prev];

            if (idx >= 0) {
              // preserve local UI overrides
              const existed: any = next[idx] || {};

              next[idx] = {
                ...existed,
                ...Object.fromEntries(
                  Object.entries(result).filter(
                    ([_, v]) => v !== null && v !== undefined && v !== "",
                  ),
                ),
              };
            } else {
              next.push({ ...result, index: next.length });
            }

            return next;
          });
        }
      }
    } catch (e) {
      // silently end on abort or error
    } finally {
      setDetecting(false);
      abortDetectingRef.current = null;
    }
  }, [paths]);

  const stopDetecting = useCallback(() => {
    abortDetectingRef.current?.abort();
    abortDetectingRef.current = null;
    setDetecting(false);
  }, []);

  const close = useCallback(() => {
    stopDetecting();
    setVisible(false);
  }, [stopDetecting]);

  useEffect(() => {
    // auto start
    // startDetecting();

    return () => stopDetecting();
  }, []);

  const renderStatus = useCallback((result: CompressedFileDetectionResult) => {
    switch (result.status) {
      case CompressedFileDetectionResultStatus.Init:
        return (
          <Chip color='default' size="sm" variant='light'>
            <AiOutlineFieldTime className="text-lg" />
          </Chip>
        );
      case CompressedFileDetectionResultStatus.Inprogress:
        return (
          <Chip color="default" variant="light">
            <Spinner color="default" labelColor="foreground" size='sm' />
          </Chip>
        );
      case CompressedFileDetectionResultStatus.Complete:
        return (
          <Chip color='success' size="sm" variant='light'>
            <AiOutlineCheckCircle className="text-lg" />
          </Chip>
        );
      case CompressedFileDetectionResultStatus.Error:
        return (
          <Button
            size="sm"
            variant="light"
            isIconOnly
            color="danger"
            onPress={() => {
              createPortal(Modal, {
                defaultVisible: true,
                size: "xl",
                children: <pre>{result.message}</pre>,
              });
            }}
          >
            <AiOutlineWarning className="text-lg" />
          </Button>
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
      showTooltip: boolean,
    ) => {
      const decompressToNewFolderBtn = <Button
        variant="light"
        size="sm"
        isIconOnly
        color={decompressToNewFolder ? "primary" : "default"}
        className={decompressToNewFolder ? "" : "opacity-30"}
        onPress={() => setDecompressToNewFolder(!decompressToNewFolder)}
      >
        <AiOutlineFolderAdd className="text-xl" />
      </Button>;
      const deleteAfterDecompressionBtn = <Button
        variant="light"
        size="sm"
        isIconOnly
        color={deleteAfterDecompression ? "danger" : "default"}
        className={deleteAfterDecompression ? "" : "opacity-30"}
        onPress={() => setDeleteAfterDecompression(!deleteAfterDecompression)}
      >
        <AiOutlineDelete className="text-xl" />
      </Button>;
      const moveToParentBtn = <Button
        variant="light"
        size="sm"
        isIconOnly
        color={moveToParent ? "success" : "default"}
        className={moveToParent ? "" : "opacity-30"}
        onPress={() => setMoveToParent(!moveToParent)}
      >
        <ImMoveUp className="text-xl" />
      </Button>;

      return (
        <div className="flex items-center gap-0">
          {showTooltip ? (
            <>
              <Tooltip content={t('Extract to new directory')} color='primary'>
                {decompressToNewFolderBtn}
              </Tooltip>
              <Tooltip content={t('Delete the source file after decompression')} color='danger'>
                {deleteAfterDecompressionBtn}
              </Tooltip>
              <Tooltip content={t('Move to parent directory')} color='success'>
                {moveToParentBtn}
              </Tooltip>
            </>

          ) : [decompressToNewFolderBtn, deleteAfterDecompressionBtn, moveToParentBtn]}
        </div>
      );
    },
    [],
  );

  // console.log(results);

  return (
    <Modal
      className={"max-w-[90vw]"}
      footer={false}
      size={"xl"}
      title={t<string>("Compressed files handling")}
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
          <div>
            <NumberInput
              isClearable
              className={'w-[400px]'}
              label={t<string>("Include unknown extensions if size >= (MB)")}
              placeholder={t<string>("Optional")}
              size="sm"
              value={sizeThresholdMb}
              onValueChange={(v) => setSizeThresholdMb(v)}
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

        <Table
          removeWrapper
          isCompact
          className="overflow-y-auto max-w-full"
          selectionMode="multiple"
          selectedKeys={results
            .filter((r) => r.status == CompressedFileDetectionResultStatus.Complete)
            .map((r) => r.key)}
        >
          <TableHeader>
            <TableColumn>{t<string>("#")}</TableColumn>
            <TableColumn>{t<string>("Files")}</TableColumn>
            <TableColumn>{t<string>("Password")}</TableColumn>
            <TableColumn>{t<string>("Status")}</TableColumn>
            <TableColumn className="min-w-[260px] w-[260px]">
              <div className="flex items-center gap-2">
                {renderOperations(
                  globalOperation.decompressToNewFolder,
                  (v) => {
                    setGlobalOperation((prev) => ({ ...prev, decompressToNewFolder: v }));
                    setOperationMap((prev) => {
                      for (const key in prev) {
                        prev[key] = { ...prev[key], decompressToNewFolder: v };
                      }

                      return prev;
                    });
                  },
                  globalOperation.deleteAfterDecompression,
                  (v) => {
                    setGlobalOperation((prev) => ({ ...prev, deleteAfterDecompression: v }));
                    setOperationMap((prev) => {
                      for (const key in prev) {
                        prev[key] = { ...prev[key], deleteAfterDecompression: v };
                      }

                      return prev;
                    });
                  },
                  globalOperation.moveToParent,
                  (v) => {
                    setGlobalOperation((prev) => ({ ...prev, moveToParent: v }));
                    setOperationMap((prev) => {
                      for (const key in prev) {
                        prev[key] = { ...prev[key], moveToParent: v };
                      }

                      return prev;
                    });
                  },
                  true,
                )}
              </div>
            </TableColumn>
          </TableHeader>
          <TableBody emptyContent={t<string>("No compressed files detected")} items={results}>
            {(result) => {
              return (
                <TableRow
                  key={result.key}
                  // className={`${result.status == CompressedFileDetectionResultStatus.Error ? "opacity-60" : ""}`}
                >
                  <TableCell>
                    {result.index + 1}
                  </TableCell>
                  <TableCell>
                    <div className="flex flex-col gap-0.5">
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
                            {result.files?.map((f) => (
                              <Chip key={f} size="sm" variant="light">
                                <div className="flex items-center gap-1">
                                  <FileSystemEntryIcon
                                    type={IconType.Dynamic}
                                    size={18}
                                    path={`${result.directory}/${f}`}
                                  />
                                  {f}
                                </div>
                              </Chip>
                            ))}
                          </div>
                          <div>
                            <AiOutlineRightCircle className="text-lg" />
                          </div>
                          <div>
                            <Chip color='success' size="sm" variant='light'>
                              <div className="flex items-center gap-1">
                                <MdOutlineCreateNewFolder className="text-lg" />
                                {result.decompressToDirName}
                              </div>
                            </Chip>
                          </div>
                          {result.contentSampleGroups && result.contentSampleGroups.length > 0 && (
                            <div className="flex flex-wrap gap-1 items-center">
                              /
                              {result.contentSampleGroups.map((g, gi) => {
                                const restFileCount = g.count - (g.samples?.length ?? 0);
                                const sampleFilesText = g.samples?.join(", ");

                                return (
                                  <Chip key={g.samples?.[0] ?? gi} size="sm" variant="flat">
                                    <div className="flex items-center gap-1">
                                      <FileSystemEntryIcon
                                        size={18}
                                        type={g.isFile ? IconType.Dynamic : IconType.Directory} />
                                      {restFileCount > 0
                                        ? t<string>(
                                          "{{sampleFilesText}} and {{restFileCount}} more",
                                        )
                                        : sampleFilesText}
                                    </div>
                                  </Chip>
                                );
                              })}
                            </div>
                          )}
                        </div>
                      </div>
                    </div>
                  </TableCell>
                  <TableCell>
                    <div>
                      {result.password && (
                        <Chip size="sm" startContent={<AiOutlineCheckCircle className="text-lg" />}>{result.password}</Chip>
                      )}
                      {result.candidatePasswords?.map((cp) => (
                        <Chip key={cp} size="sm">
                          {cp}
                        </Chip>
                      ))}
                      {result.wrongPasswords?.map((wp) => (
                        <Chip key={wp} className="line-through" size='sm' variant="light">{wp}</Chip>
                      ))}
                    </div>
                  </TableCell>
                  <TableCell>{renderStatus(result)}</TableCell>
                  <TableCell>
                    <div className="flex items-center gap-2">
                      {result.status == CompressedFileDetectionResultStatus.Complete &&
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
                          false,
                        )}
                      {result.message && (
                        <Button
                          size="sm"
                          variant="light"
                          isIconOnly
                          onPress={() => {
                            createPortal(Modal, {
                              defaultVisible: true,
                              size: "xl",
                              children: <pre>{result.message}</pre>,
                            });
                          }}
                        >
                          <CgNotes className="text-lg" />
                        </Button>
                      )}
                    </div>
                  </TableCell>
                </TableRow>
              );
            }}
          </TableBody>
        </Table>
        {results.some((r) => r.status == CompressedFileDetectionResultStatus.Complete) && !detecting && (
          <div className="flex items-center gap-2" style={{}}>
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
                color="primary"
                onPress={() => {
                  startDecompressing();
                }}
              >
                {t("Decompress all selected")}
              </Button>
            </div>
          </div>
        )}
      </div>
    </Modal>
  );
};

DetectCompressedFilesModal.displayName = "DetectCompressedFilesModal";

export default DetectCompressedFilesModal;
