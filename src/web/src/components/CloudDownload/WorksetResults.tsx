import { useTranslation } from "react-i18next";
import { useState } from "react";
import { Accordion, AccordionItem, Button } from "@/components/bakaui";
import { useDetailAccordion, type DetailExpansionRequest } from "@/hooks/useDetailExpansion";
import type { CleanupRestoreStatus } from "./artifactCleanupApi";
import { downloadProblemLabel } from "./problemLabels";

interface Target {
  id: string;
  title?: string;
  downloadTaskId?: number;
  transportTaskId?: string;
  problem?: string;
  excluded?: boolean;
  files?: string[];
  fileCount?: number;
  processedFiles?: string[];
  processedFileCount?: number;
  processedDirectory?: string;
  downloadOperationId?: string;
  downloadArtifactId?: string;
  processingOperationId?: string;
  processedArtifactId?: string;
  executionStatus?: string;
  cleanupReceiptId?: string;
  cleanupStatus?: string;
  publicationOperationId?: string;
  publishedArtifactId?: string;
  publishedDirectory?: string;
  publishedFiles?: string[];
  publishedFileCount?: number;
}

export function CleanupReceipt({
  receiptId,
  status,
  readOnly = false,
}: {
  receiptId: string;
  status?: string;
  readOnly?: boolean;
}) {
  const { t } = useTranslation();
  const [busy, setBusy] = useState(false);
  const [restoreStatus, setRestoreStatus] = useState<CleanupRestoreStatus>();
  const [error, setError] = useState<string>();
  const restore = async () => {
    if (readOnly || busy || restoreStatus === "Restored") return;
    setBusy(true);
    setError(undefined);
    setRestoreStatus(undefined);
    try {
      const { restoreCleanupReceipt } = await import("./artifactCleanupApi");
      setRestoreStatus(await restoreCleanupReceipt(receiptId));
    } catch (error) {
      setError(error instanceof Error ? error.message : t("cloudDownload.cleanup.restoreFailed"));
    } finally {
      setBusy(false);
    }
  };
  return (
    <div className="space-y-1">
      <p className="break-all text-xs">
        {t("cloudDownload.cleanup.receipt")}: {receiptId}
      </p>
      {restoreStatus === "Restored" ? (
        <p className="text-sm text-success" role="status">
          {t("cloudDownload.cleanup.restored")}
        </p>
      ) : (
        !readOnly &&
        status === "quarantined" && (
          <Button
            size="sm"
            variant="flat"
            isLoading={busy}
            isDisabled={busy}
            onPress={() => void restore()}
          >
            {t("cloudDownload.cleanup.restore")}
          </Button>
        )
      )}
      {restoreStatus === "RecoveryLocationUnavailable" && (
        <p className="break-words text-sm text-warning-600" role="alert">
          {t("cloudDownload.cleanup.recoveryLocationUnavailable")}
        </p>
      )}
      {error && (
        <p className="break-words text-sm text-danger" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}

export default function WorksetResults({
  json,
  expansion,
  readOnly = false,
}: {
  json?: string | null;
  expansion?: DetailExpansionRequest;
  readOnly?: boolean;
}) {
  const { t } = useTranslation();
  let targets: Target[] = [];
  try {
    const items: unknown = JSON.parse(json ?? "[]");
    if (Array.isArray(items))
      targets = items.flatMap((item: unknown) => {
        if (
          !item ||
          typeof item !== "object" ||
          !("targets" in item) ||
          !Array.isArray(item.targets)
        )
          return [];
        return item.targets.filter(
          (target: unknown): target is Target =>
            !!target &&
            typeof target === "object" &&
            "id" in target &&
            typeof target.id === "string",
        );
      });
  } catch {
    /* Other workflow output is rendered by the generic preview. */
  }
  if (!targets.length) return null;
  return (
    <div className="space-y-2" aria-label={t("cloudDownload.results")}>
      {targets.some(
        (target) =>
          target.publishedDirectory ||
          target.processedDirectory ||
          target.files?.length ||
          target.processedFiles?.length,
      ) && <p className="text-xs text-default-500">{t("cloudDownload.results.historicalPaths")}</p>}
      {targets.map((target) => (
        <TargetResult key={target.id} target={target} expansion={expansion} readOnly={readOnly} />
      ))}
    </div>
  );
}

function TargetResult({
  target,
  expansion,
  readOnly,
}: {
  target: Target;
  expansion?: DetailExpansionRequest;
  readOnly: boolean;
}) {
  const { t } = useTranslation();
  const artifactsAccordion = useDetailAccordion(["artifacts"], expansion);
  const filesAccordion = useDetailAccordion(["files"], expansion);
  const processedAccordion = useDetailAccordion(["processed"], expansion);
  return (
    <div className="space-y-1 rounded-lg bg-default-50 p-3 text-sm">
      <p className="font-medium">{target.title || target.id}</p>
      {target.executionStatus && (
        <p className="text-xs text-secondary">
          {t(`cloudDownload.execution.${target.executionStatus}`, {
            defaultValue: target.executionStatus,
          })}
        </p>
      )}
      <p className={target.problem ? "text-warning-600" : "text-default-500"}>
        {target.problem
          ? downloadProblemLabel(target.problem, t)
          : target.excluded
            ? t("cloudDownload.skipped")
            : t("cloudDownload.fileCount", {
                count: target.files?.length ?? target.fileCount ?? 0,
              })}
      </p>
      {target.cleanupStatus && (
        <p className="text-xs text-default-500">
          {t(`cloudDownload.cleanup.status.${target.cleanupStatus}`, {
            defaultValue: target.cleanupStatus,
          })}
        </p>
      )}
      {target.cleanupReceiptId && (
        <CleanupReceipt
          readOnly={readOnly}
          key={target.cleanupReceiptId}
          receiptId={target.cleanupReceiptId}
          status={target.cleanupStatus}
        />
      )}
      {target.publishedDirectory && (
        <div className="space-y-1 rounded-lg bg-success/10 p-2">
          <p className="text-success">
            {t("cloudDownload.publication.delivered", {
              count: target.publishedFiles?.length ?? target.publishedFileCount ?? 0,
            })}
          </p>
          <p className="break-all text-xs">
            {t("cloudDownload.publication.directoryAtExecution")}: {target.publishedDirectory}
          </p>
        </div>
      )}
      {(target.downloadOperationId ||
        target.downloadArtifactId ||
        target.processingOperationId ||
        target.processedArtifactId ||
        target.publicationOperationId ||
        target.publishedArtifactId) && (
        <Accordion isCompact className="px-0" {...artifactsAccordion}>
          <AccordionItem
            key="artifacts"
            title={t("cloudDownload.artifacts.details")}
            classNames={{ title: "text-xs" }}
          >
            <dl className="mt-1 space-y-1">
              {(
                [
                  "downloadOperationId",
                  "downloadArtifactId",
                  "processingOperationId",
                  "processedArtifactId",
                  "publicationOperationId",
                  "publishedArtifactId",
                ] as const
              ).map(
                (field) =>
                  target[field] && (
                    <div key={field} className="break-all">
                      <dt className="text-default-500">{t(`cloudDownload.artifacts.${field}`)}</dt>
                      <dd className="select-text">{target[field]}</dd>
                    </div>
                  ),
              )}
            </dl>
          </AccordionItem>
        </Accordion>
      )}
      {target.processedDirectory && target.processedDirectory !== target.publishedDirectory && (
        <div className="space-y-1">
          <p className="text-success">
            {t("cloudDownload.processing.fileCount", {
              count: target.processedFiles?.length ?? target.processedFileCount ?? 0,
            })}
          </p>
          <p className="break-all text-xs">
            {t("cloudDownload.processing.directoryAtExecution")}: {target.processedDirectory}
          </p>
        </div>
      )}
      {(target.downloadTaskId != null || target.transportTaskId) && (
        <a
          className="text-primary"
          href={`#/downloader?id=${encodeURIComponent(target.transportTaskId ?? `site:${target.downloadTaskId}`)}`}
        >
          {t("cloudDownload.viewTask")}
        </a>
      )}
      {!!target.files?.length && (
        <Accordion isCompact className="px-0" {...filesAccordion}>
          <AccordionItem
            key="files"
            title={t("cloudDownload.files")}
            classNames={{ title: "text-xs" }}
          >
            <ul className="mt-1 space-y-1">
              {target.files.map((file) => (
                <li className="break-all" key={file}>
                  {file}
                </li>
              ))}
            </ul>
          </AccordionItem>
        </Accordion>
      )}
      {!!target.processedFiles?.length && (
        <Accordion isCompact className="px-0" {...processedAccordion}>
          <AccordionItem
            key="processed"
            title={t("cloudDownload.processing.files")}
            classNames={{ title: "text-xs" }}
          >
            <ul className="mt-1 space-y-1">
              {target.processedFiles.map((file) => (
                <li className="break-all" key={file}>
                  {file}
                </li>
              ))}
            </ul>
          </AccordionItem>
        </Accordion>
      )}
    </div>
  );
}
