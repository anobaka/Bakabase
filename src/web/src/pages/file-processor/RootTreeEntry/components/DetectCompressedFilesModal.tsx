"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { Entry } from "@/core/models/FileExplorer/Entry";

import React, { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

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
  Select
} from "@/components/bakaui";
import envConfig from "@/config/env";
import FileSystemEntryIcon from "@/components/FileSystemEntryIcon";
import { IconType } from "@/sdk/constants";
import { AiOutlineCheckCircle, AiOutlineCloseCircle, AiOutlineFieldTime, AiOutlineFolder, AiOutlineInfoCircle, AiOutlineRightCircle } from "react-icons/ai";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { MdOutlineCreateNewFolder } from "react-icons/md";
import { TbBracketsContainStart } from "react-icons/tb";
import { LoadingOutlined } from "@ant-design/icons";

enum ExtractActionAfterDecompression {
  None = 0,
  InnerSecondLayer = 1,
  OuterFirstLayerRemoveParent = 2
}

enum CompressedFileDetectionResultStatus {
  Init = 1,
  Inprogress,
  Complete,
  Error,
}

type CompressedFileDetectionResult = {
  key?: string;
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

type Props = { entries: Entry[] } & DestroyableProps;

const DetectCompressedFilesModal = ({ entries = [], onDestroyed }: Props) => {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(true);
  const [detecting, setDetecting] = useState(false);
  const [sizeThresholdMb, setSizeThresholdMb] = useState<number | undefined>(undefined);
  const [onFailureContinue, setOnFailureContinue] = useState(true);
  const abortDetectingRef = useRef<AbortController | null>(null);
  const abortDecompressingRef = useRef<AbortController | null>(null);
  const { createPortal } = useBakabaseContext();

  const [results, setResults] = useState<CompressedFileDetectionResult[]>([]);

  const startDetecting = useCallback(async () => {
    if (!entries || entries.length === 0) return;
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
          paths: entries.map((e) => e.path),
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
              next.push(result);
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
  }, [entries]);

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
    startDetecting();

    return () => stopDetecting();
  }, []);

  const renderStatus = useCallback((result: CompressedFileDetectionResult) => {
    switch (result.status) {
      case CompressedFileDetectionResultStatus.Init:
        return (
          <Chip size="sm" color='default' variant='light'>
            <AiOutlineFieldTime className="text-lg" />
          </Chip>
        )
      case CompressedFileDetectionResultStatus.Inprogress:
        return (
          <Chip color='default' variant='light'>
            <Spinner color="default" size='sm' labelColor="foreground" />
          </Chip>
        )
      case CompressedFileDetectionResultStatus.Complete:
        return (
          <Chip size="sm" color='success' variant='light' >
            <AiOutlineCheckCircle className="text-lg" />
          </Chip>
        )
      case CompressedFileDetectionResultStatus.Error:
        return (
          <Button size="sm" variant="light" isIconOnly color='danger' onPress={() => {
            createPortal(Modal, {
              defaultVisible: true,
              size: "xl",
              children: <pre>{result.message}</pre>,
            });
          }}>
            <AiOutlineCloseCircle className="text-lg" />
          </Button>
        )
    }
  }, []);

  console.log(results);

  return (
    <Modal
      className={"max-w-[90vw]"}
      footer={false}
      size={"xl"}
      title={t<string>("Detect compressed files")}
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
              className={'w-[400px]'}
              isClearable
              label={t<string>("Include unknown extensions if size >= (MB)")}
              placeholder={t<string>("Optional")}
              size="sm"
              value={sizeThresholdMb}
              onValueChange={(v) => setSizeThresholdMb(v)}
            />
          </div>
          <div>
            {!detecting ? (
              <Button color="primary" onPress={() => {
                startDetecting();
              }} >{t("Detect compressed files")}</Button>
            ) : (
              <Button color="danger" variant="light" onPress={stopDetecting}>
                <LoadingOutlined className="text-lg" />
                {t("Stop")}
              </Button>
            )}
          </div>
        </div>

        <Table isStriped removeWrapper isCompact className="overflow-y-auto">
          <TableHeader>
            <TableColumn>{t<string>("Files")}</TableColumn>
            <TableColumn>{t<string>("Password")}</TableColumn>
            <TableColumn>{t<string>("Password candidates")}</TableColumn>
            <TableColumn>{t<string>("Status")}</TableColumn>
            <TableColumn className="min-w-[260px] w-[260px]">{t<string>("Operations")}</TableColumn>
          </TableHeader>
          <TableBody>
            {results.map((result) => {
              return (
                <TableRow key={result.key}>
                  <TableCell>
                    <div>
                      <Chip size="sm">
                        <div className="flex items-center gap-1">
                          <AiOutlineFolder className="text-lg" />
                          {result.directory}
                        </div>
                      </Chip>
                      <div className="flex items-center gap-1">
                        <div className="flex flex-wrap gap-1 max-h-[100px] overflow-y-auto">
                          {result.files?.map(f => <Chip size="sm" variant="light" key={f}>
                            <div className="flex items-center gap-1">
                              <FileSystemEntryIcon type={IconType.Dynamic} size={18} path={`${result.directory}/${f}`} />
                              {f}
                            </div>
                          </Chip>)}
                        </div>
                        <div>
                          <AiOutlineRightCircle className="text-lg" />
                        </div>
                        <div>
                          <Chip size="sm" color='success' variant='light'>
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
                                <Chip variant="flat" size="sm" key={g.samples?.[0] ?? gi}>
                                  <div className="flex items-center gap-1">
                                    <FileSystemEntryIcon type={g.isFile ? IconType.Dynamic : IconType.Directory} size={18} />
                                    {restFileCount > 0 ? t<string>("{{sampleFilesText}} and {{restFileCount}} more") : sampleFilesText}
                                  </div>
                                </Chip>
                              )
                            })}
                          </div>
                        )}
                      </div>
                    </div>
                  </TableCell>
                  <TableCell>{result.password}</TableCell>
                  <TableCell>
                    <div>
                      {result.candidatePasswords?.map(cp => (
                        <Chip key={cp} size='sm'>{cp}</Chip>
                      ))}
                      {result.wrongPasswords?.map(wp => (
                        <Chip variant="light" className="line-through" key={wp} size='sm'>{wp}</Chip>
                      ))}
                    </div>
                  </TableCell>
                  <TableCell>
                    {renderStatus(result)}
                  </TableCell>
                  <TableCell>
                    <div>
                      {result.message && (
                        <Button size="sm" variant="light" isIconOnly onPress={() => {
                          createPortal(Modal, {
                            defaultVisible: true,
                            size: "xl",
                            children: <pre>{result.message}</pre>,
                          });
                        }}>
                          <AiOutlineInfoCircle className="text-lg" />
                        </Button>
                      )}
                      {result.status == CompressedFileDetectionResultStatus.Complete && (
                        <>
                          <div>
                            <Checkbox size="sm">{t('Delete after decompression')}</Checkbox>
                          </div>
                          <div>
                            <Select size='sm' label={t('Extract after decompression')} dataSource={[{ label: t('Extract to current directory'), value: ExtractActionAfterDecompression.InnerSecondLayer }, { label: t('Extract to new directory'), value: ExtractActionAfterDecompression.OuterFirstLayerRemoveParent }]} isClearable>
                            </Select>
                          </div>
                        </>
                      )}
                    </div>
                  </TableCell>
                </TableRow>
              )
            })}
          </TableBody>
        </Table>
        {results.some(r => r.status == CompressedFileDetectionResultStatus.Complete) && (
          <div className="flex items-center gap-2" style={{}}>
            <div>
              <Checkbox
                checked={onFailureContinue}
                type="checkbox"
                onChange={(e) => setOnFailureContinue(e.currentTarget.checked)}
              >
                {t<string>("Continue on failure")}
              </Checkbox>
            </div>
            <div>
              <Button color="primary" onPress={() => {
                startDecompressing();
              }} >{t("Decompress")}</Button>
            </div>
          </div>
        )}
      </div>
    </Modal>
  );
};

DetectCompressedFilesModal.displayName = "DetectCompressedFilesModal";

export default DetectCompressedFilesModal;
