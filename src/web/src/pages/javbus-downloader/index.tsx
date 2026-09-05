"use client";

import type { BakabaseModulesThirdPartyThirdPartiesJavbusModelsJavbusBatchDownloadState as BatchState } from "@/sdk/Api";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  AiOutlineCloudDownload,
  AiOutlineCopy,
  AiOutlineFolderOpen,
  AiOutlineStop,
} from "react-icons/ai";

import { extractCodes, splitPending, splitVerbatim } from "./codes";

import {
  Button,
  Chip,
  NumberInput,
  Progress,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  Textarea,
  Tooltip,
  toast,
} from "@/components/bakaui";
import { FileSystemSelectorButton } from "@/components/FileSystemSelector";
import BApi from "@/sdk/BApi";
import { BTaskStatus, JavbusBatchItemStatus, JavbusMagnetTag } from "@/sdk/constants";
import { useBTasksStore } from "@/stores/bTasks";
import { useJavbusDownloaderOptionsStore } from "@/stores/options";
import { getEnumKey } from "@/i18n";

const TASK_ID = "JavbusBatchDownload";
/** Long enough that a slow typist isn't interrupted mid-code. */
const EXTRACT_DEBOUNCE_MS = 500;
const POLL_INTERVAL_MS = 1000;

export default function JavbusDownloaderPage() {
  const { t } = useTranslation();
  const options = useJavbusDownloaderOptionsStore((s) => s.data);
  const patchOptions = useJavbusDownloaderOptionsStore((s) => s.patch);
  const task = useBTasksStore((s) => s.tasks.find((x) => x.id === TASK_ID));
  const isRunning = task?.status === BTaskStatus.Running;

  const [codes, setCodes] = useState<string[]>([]);
  const [draft, setDraft] = useState("");
  const [state, setState] = useState<BatchState | null>(null);
  const pastedRef = useRef(false);

  const concurrency = options?.concurrency ?? 2;
  const delayMs = options?.delayMs ?? 600;
  const tolerance = options?.sizeTolerancePercentage ?? 30;
  const saveCovers = options?.saveCovers ?? false;
  const coverDirectory = options?.coverDirectory ?? "";

  const loadState = useCallback(async () => {
    const rsp = await BApi.javbus.getJavbusBatchDownloadState();

    if (!rsp.code) setState(rsp.data ?? null);
  }, []);

  // The table streams in while the task runs. Re-running on isRunning also
  // gives the final poll once it stops, catching rows the interval missed.
  useEffect(() => {
    loadState();
    if (!isRunning) return;

    const timer = setInterval(loadState, POLL_INTERVAL_MS);

    return () => clearInterval(timer);
  }, [isRunning, loadState]);

  const addCodes = useCallback((incoming: string[]) => {
    setCodes((prev) => {
      const seen = new Set(prev);

      return prev.concat(incoming.filter((c) => (seen.has(c) ? false : (seen.add(c), true))));
    });
  }, []);

  // Auto-extract on a debounce. While typing only the text before the last
  // separator is consumed; a paste is consumed whole.
  useEffect(() => {
    if (!draft.trim()) return;
    const timer = setTimeout(() => {
      const whole = pastedRef.current;

      pastedRef.current = false;
      const [head, pending] = whole ? [draft, ""] : splitPending(draft);

      if (!head) return;
      const { codes: found, rest, ignored } = extractCodes(head);

      if (!found.length) return;
      addCodes(found);
      setDraft([rest, pending].filter(Boolean).join("\n"));
      if (ignored) {
        toast.success(t("javbusDownloader.hint.extracted", { count: found.length, ignored }));
      }
    }, EXTRACT_DEBOUNCE_MS);

    return () => clearTimeout(timer);
  }, [draft, addCodes, t]);

  const items = state?.items ?? [];
  const magnets = useMemo(
    () => items.map((i) => i.magnet?.link).filter((l): l is string => !!l),
    [items],
  );

  // NumberInput hands back NaN while the box is empty mid-edit; saving that
  // would wipe the setting.
  const patchNumber = (
    key: "concurrency" | "delayMs" | "sizeTolerancePercentage",
    value: number,
  ) => {
    if (Number.isFinite(value)) patchOptions({ [key]: value });
  };

  const copy = async (text: string, message: string) => {
    try {
      await navigator.clipboard.writeText(text);
      toast.success(message);
    } catch {
      toast.danger(t("javbusDownloader.hint.copyFailed"));
    }
  };

  const start = async () => {
    const rsp = await BApi.javbus.startJavbusBatchDownload({ codes });

    if (rsp.code) return;
    setState(null);
    toast.success(t("javbusDownloader.hint.started", { count: codes.length }));
  };

  const stop = async () => {
    await BApi.backgroundTask.stopBackgroundTask(TASK_ID);
  };

  const renderMagnetCell = (item: BatchState["items"][number]) => {
    if (item.status !== JavbusBatchItemStatus.Succeeded || !item.magnet) {
      return (
        <div className="flex flex-col gap-0.5">
          <span className="text-danger text-xs">
            {t(getEnumKey("JavbusBatchItemStatus", JavbusBatchItemStatus[item.status]))}
          </span>
          {item.error && <span className="text-xs text-default-400">{item.error}</span>}
        </div>
      );
    }

    return (
      <div className="flex flex-col gap-0.5">
        <div className="truncate text-xs">{item.magnet.name}</div>
        <div className="text-xs text-default-400">
          {t(getEnumKey("JavbusMagnetTag", JavbusMagnetTag[item.magnet.tag]))}
          {item.candidateCount
            ? ` · ${t("javbusDownloader.label.candidates", { count: item.candidateCount })}`
            : ""}
          {item.coverError ? (
            <span className="text-warning"> · {t("javbusDownloader.label.coverFailed")}</span>
          ) : null}
        </div>
      </div>
    );
  };

  return (
    <div className="flex flex-col gap-3 p-2">
      <div className="text-sm text-default-500">{t("javbusDownloader.description")}</div>

      {/* ---- codes ---- */}
      <div className="flex flex-col gap-2">
        <div className="flex items-center gap-2 flex-wrap">
          <span className="text-sm text-default-500">
            {t("javbusDownloader.label.recognized", { count: codes.length })}
          </span>
          {codes.length > 0 && (
            <Button size="sm" variant="light" onPress={() => setCodes([])}>
              {t("common.action.clear")}
            </Button>
          )}
        </div>
        {codes.length > 0 && (
          <div className="flex flex-wrap gap-1.5">
            {codes.map((code) => (
              <Chip
                key={code}
                size="sm"
                variant="flat"
                onClose={() => setCodes((prev) => prev.filter((c) => c !== code))}
              >
                {code}
              </Chip>
            ))}
          </div>
        )}
        <Textarea
          minRows={3}
          placeholder={t<string>("javbusDownloader.placeholder.codes")}
          value={draft}
          onPaste={() => {
            pastedRef.current = true;
          }}
          onValueChange={setDraft}
        />
        <div className="flex gap-2 flex-wrap">
          <Button
            size="sm"
            variant="flat"
            onPress={() => {
              const { codes: found, rest, ignored } = extractCodes(draft);

              if (!found.length) {
                // Nothing matched — leave the box alone so nothing is lost.
                if (draft.trim()) toast.warning(t("javbusDownloader.hint.nothingRecognized"));

                return;
              }
              addCodes(found);
              setDraft(rest);
              toast.success(t("javbusDownloader.hint.extracted", { count: found.length, ignored }));
            }}
          >
            {t("javbusDownloader.action.extract")}
          </Button>
          <Button
            size="sm"
            variant="flat"
            onPress={() => {
              const values = splitVerbatim(draft);

              if (!values.length) return;
              addCodes(values);
              setDraft("");
            }}
          >
            {t("javbusDownloader.action.addVerbatim")}
          </Button>
        </div>
      </div>

      {/* ---- settings ---- */}
      <div className="flex gap-3 flex-wrap items-end">
        <NumberInput
          className="w-28"
          label={t<string>("javbusDownloader.label.concurrency")}
          maxValue={8}
          minValue={1}
          size="sm"
          value={concurrency}
          onValueChange={(v) => patchNumber("concurrency", v)}
        />
        <NumberInput
          className="w-32"
          label={t<string>("javbusDownloader.label.delayMs")}
          minValue={0}
          size="sm"
          value={delayMs}
          onValueChange={(v) => patchNumber("delayMs", v)}
        />
        <Tooltip content={t<string>("javbusDownloader.tip.sizeTolerance")}>
          <NumberInput
            className="w-36"
            label={t<string>("javbusDownloader.label.sizeTolerance")}
            maxValue={90}
            minValue={0}
            size="sm"
            value={tolerance}
            onValueChange={(v) => patchNumber("sizeTolerancePercentage", v)}
          />
        </Tooltip>
        <Switch
          isSelected={saveCovers}
          size="sm"
          onValueChange={(v) => patchOptions({ saveCovers: v })}
        >
          {t("javbusDownloader.label.saveCovers")}
        </Switch>
        {saveCovers && (
          // The button renders the chosen path as its own label.
          <FileSystemSelectorButton
            key={coverDirectory}
            fileSystemSelectorProps={{
              targetType: "folder",
              defaultSelectedPath: coverDirectory || undefined,
              onSelected: (e) => patchOptions({ coverDirectory: e.path }),
            }}
            size="sm"
            variant="flat"
          />
        )}
      </div>

      {/* ---- run ---- */}
      <div className="flex gap-2 flex-wrap items-center">
        <Button color="primary" isDisabled={!codes.length || isRunning} size="sm" onPress={start}>
          <AiOutlineCloudDownload className="text-base" />
          {codes.length
            ? t("javbusDownloader.action.startWithCount", { count: codes.length })
            : t("javbusDownloader.action.start")}
        </Button>
        {isRunning && (
          <Button color="danger" size="sm" variant="light" onPress={stop}>
            <AiOutlineStop className="text-base" />
            {t("common.action.stop")}
          </Button>
        )}
        {state?.coverDirectory && (
          <Button
            size="sm"
            variant="light"
            onPress={() =>
              BApi.tool.openFileOrDirectory({
                path: state.coverDirectory!,
                openInDirectory: false,
              })
            }
          >
            <AiOutlineFolderOpen className="text-base" />
            {t("javbusDownloader.action.openCoverDirectory")}
          </Button>
        )}
      </div>

      {state && state.total > 0 && (
        <div className="flex flex-col gap-1">
          <Progress aria-label="progress" size="sm" value={(state.done / state.total) * 100} />
          <div className="text-sm text-default-500">
            {t("javbusDownloader.label.progress", { done: state.done, total: state.total })}
          </div>
        </div>
      )}

      {/* ---- results ---- */}
      {items.length > 0 && (
        <div className="flex flex-col gap-2">
          <div>
            <Button
              isDisabled={!magnets.length}
              size="sm"
              variant="flat"
              onPress={() =>
                copy(
                  magnets.join("\n"),
                  t("javbusDownloader.hint.copiedAll", { count: magnets.length }),
                )
              }
            >
              <AiOutlineCopy className="text-base" />
              {t("javbusDownloader.action.copyAll", { count: magnets.length })}
            </Button>
          </div>
          <Table aria-label="javbus results">
            <TableHeader>
              <TableColumn>{t("javbusDownloader.column.code")}</TableColumn>
              <TableColumn>{t("javbusDownloader.column.title")}</TableColumn>
              <TableColumn>{t("javbusDownloader.column.magnet")}</TableColumn>
              <TableColumn>{t("javbusDownloader.column.size")}</TableColumn>
              <TableColumn>{t("javbusDownloader.column.date")}</TableColumn>
              <TableColumn>{t("javbusDownloader.column.actions")}</TableColumn>
            </TableHeader>
            <TableBody>
              {items.map((item) => (
                <TableRow key={item.code}>
                  <TableCell className="whitespace-nowrap font-medium">
                    {item.detailUrl ? (
                      <a
                        className="hover:underline"
                        href={item.detailUrl}
                        rel="noreferrer"
                        target="_blank"
                      >
                        {item.code}
                      </a>
                    ) : (
                      item.code
                    )}
                  </TableCell>
                  <TableCell className="max-w-xs">
                    <div className="truncate text-xs text-default-500">{item.title ?? ""}</div>
                  </TableCell>
                  <TableCell className="max-w-md">{renderMagnetCell(item)}</TableCell>
                  <TableCell className="whitespace-nowrap">{item.magnet?.size ?? "—"}</TableCell>
                  <TableCell className="whitespace-nowrap text-xs text-default-500">
                    {item.magnet?.date ?? "—"}
                  </TableCell>
                  <TableCell>
                    <Button
                      isDisabled={!item.magnet}
                      size="sm"
                      variant="light"
                      onPress={() =>
                        copy(
                          item.magnet!.link,
                          t("javbusDownloader.hint.copiedOne", { code: item.code }),
                        )
                      }
                    >
                      {t("common.action.copy")}
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </div>
  );
}
