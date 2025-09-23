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
import { AiOutlineCheckCircle, AiOutlineCloseCircle, AiOutlineFieldTime, AiOutlineInfoCircle } from "react-icons/ai";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

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
  const abortRef = useRef<AbortController | null>(null);
  const runAbortRef = useRef<AbortController | null>(null);
  const { createPortal } = useBakabaseContext();

  const [results, setResults] = useState<CompressedFileDetectionResult[]>([]);

  const start = useCallback(async () => {
    if (!entries || entries.length === 0) return;
    setDetecting(true);
    setResults([]);
    const controller = new AbortController();

    abortRef.current = controller;

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
      abortRef.current = null;
    }
  }, [entries]);

  const stop = useCallback(() => {
    abortRef.current?.abort();
    abortRef.current = null;
    setDetecting(false);
  }, []);

  const close = useCallback(() => {
    stop();
    setVisible(false);
  }, [stop]);

  useEffect(() => {
    // auto start
    start();

    return () => stop();
  }, []);

  const renderStatus = useCallback((result: CompressedFileDetectionResult) => {
    switch (result.status) {
      case CompressedFileDetectionResultStatus.Init:
        return (
          <Chip size="sm">
            <AiOutlineFieldTime className="text-lg" />
          </Chip>
        )
      case CompressedFileDetectionResultStatus.Inprogress:
        return (
          <Chip>
            <Spinner color="default" label="Default" labelColor="foreground" />
          </Chip>
        )
      case CompressedFileDetectionResultStatus.Complete:
        return (
          <Chip size="sm">
            <AiOutlineCheckCircle className="text-lg" />
          </Chip>
        )
      case CompressedFileDetectionResultStatus.Error:
        return (
          <Button size="sm" variant="light" isIconOnly onPress={() => {
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

  return (
    <Modal
      className={"max-w-[90vw]"}
      footer={false}
      size={"xl"}
      title={t<string>("Detect compressed files")}
      visible={visible}
      onClose={() => {
        if (detecting || runAbortRef.current) {
          if (!confirm(t<string>("A task is running. Close and cancel it?"))) return;
          stop();
          runAbortRef.current?.abort();
          runAbortRef.current = null;
        }
        close();
      }}
      onDestroyed={onDestroyed}
    >
      <div className="flex flex-col gap-3">
        <div>
          <div>{t("Detect compressed files")}</div>
          <div>
            <NumberInput
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
              <Button size={"sm"}>{t("Detect compressed files")}</Button>
            ) : (
              <Button isLoading size={"sm"} color="danger" variant="light" onPress={stop}>
                {t("Stop")}
              </Button>
            )}
          </div>
        </div>

        <Table isStriped removeWrapper isCompact>
          <TableHeader>
            <TableColumn>{t<string>("Files")}</TableColumn>
            <TableColumn>{t<string>("Password")}</TableColumn>
            <TableColumn>{t<string>("Password candidates")}</TableColumn>
            <TableColumn>{t<string>("Status")}</TableColumn>
            <TableColumn>{t<string>("Operations")}</TableColumn>
          </TableHeader>
          <TableBody>
            {results.map((result) => {
              return (
                <TableRow key={result.key}>
                  <TableCell>
                    <div>
                      <div>
                        {result.directory}
                      </div>
                      <div>
                        {result.files?.map(f => <Chip size="sm" key={f}>{f}</Chip>)}
                      </div>
                      <div>
                        {result.decompressToDirName}
                      </div>
                      <div>
                        {result.contentSampleGroups?.map((g, gi) => {
                          const restFileCount = g.count - (g.samples?.length ?? 0);
                          const sampleFilesText = g.samples?.join(", ");
                          return (
                            <Chip variant="flat" size="sm" key={g.samples?.[0] ?? gi}>
                              <FileSystemEntryIcon type={g.isFile ? IconType.Dynamic : IconType.Directory} size="sm" />
                              {restFileCount > 0 ? t<string>("{{sampleFilesText}} and {{restFileCount}} more") : sampleFilesText}
                            </Chip>
                          )
                        })}
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
                        <Button size="sm" variant="light" isIconOnly>
                          <AiOutlineInfoCircle className="text-lg" />
                        </Button>
                      )}
                      {result.status == CompressedFileDetectionResultStatus.Complete && (
                        <>
                          <div>
                            <Checkbox>{t('Delete after decompression')}</Checkbox>
                          </div>
                          <div>
                            <Select label={t('Extract after decompression')} dataSource={[{ label: t('Extract to current directory'), value: ExtractActionAfterDecompression.InnerSecondLayer }, { label: t('Extract to new directory'), value: ExtractActionAfterDecompression.OuterFirstLayerRemoveParent }]} isClearable>
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
        <div className="grid grid-cols-2 gap-2" style={{}}>
          <div />
          <div>
            <div className="self-center text-sm opacity-80">{t<string>("On failure continue")}</div>
            <div className="flex items-center gap-2">
              <input
                checked={onFailureContinue}
                type="checkbox"
                onChange={(e) => setOnFailureContinue(e.currentTarget.checked)}
              />
              <span className="text-xs opacity-70">{t<string>("Continue on next file")}</span>
            </div>
          </div>
        </div>
      </div>
    </Modal>
  );
};

DetectCompressedFilesModal.displayName = "DetectCompressedFilesModal";

export default DetectCompressedFilesModal;
