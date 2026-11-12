import type { DestroyableProps } from "@/components/bakaui/types";
import type { SourceHealth } from "@/components/SourceAvailability/models";

import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { SelectItem } from "@heroui/react";
import { AiOutlineDownload } from "react-icons/ai";

import AcquisitionSourceDetails from "../AcquisitionSourceDetails";
import { downloadMethodLabel } from "../methodLabels";
import MethodPreparation from "../MethodPreparation";

import {
  sourceRevisionApi,
  type SourceRevisionOptions,
  type SourceRevisionPreview,
} from "./sourceRevisionApi";
import { sourceRevisionReasonCodes, sourceRevisionReasonKey } from "./sourceRevisionReasons";
import SourceRevisionEvidence from "./SourceRevisionEvidence";

import { Button, Chip, Modal, Select, Spinner } from "@/components/bakaui";
import ThirdPartyIcon from "@/components/ThirdPartyIcon";
import { CloudDownloadSources, ThirdPartyId } from "@/sdk/constants";
import { getEnumKey } from "@/i18n";
import { millisecondsUntil, parseServerTime } from "@/core/serverTime";
import PlanSummary from "@/components/ProcessingPlanRevisions/PlanSummary";

type Candidate = SourceRevisionOptions["candidates"][number];
type Source = SourceRevisionPreview["currentSource"];
const candidateKey = (candidate: Candidate) => JSON.stringify([candidate.partId, candidate.id]);

function sourceHost(source: Source): string {
  try {
    return new URL(source.url).hostname;
  } catch {
    return "";
  }
}

function useSourcePresentation(source?: Source | null) {
  const { t } = useTranslation();
  const provider = CloudDownloadSources.find((item) => item.providerId === source?.providerId);
  const thirdPartyId =
    provider?.thirdPartyId ??
    (source?.providerId === "exHentai" ? ThirdPartyId.ExHentai : undefined);
  const name =
    source?.providerId === "oneDrive"
      ? t("sourceAvailability.provider.oneDrive")
      : thirdPartyId != null
        ? t(getEnumKey("ThirdPartyId", ThirdPartyId[thirdPartyId]))
        : (source && sourceHost(source)) || t("acquisition.sourceRevision.otherSource");
  const icon =
    thirdPartyId != null ? (
      <ThirdPartyIcon thirdPartyId={thirdPartyId} />
    ) : (
      <AiOutlineDownload aria-hidden className="shrink-0" />
    );

  return { name, icon };
}

function SourceOption({ source }: { source: Source }) {
  const { name, icon } = useSourcePresentation(source);

  return (
    <span className="flex min-w-0 items-center gap-2">
      {icon}
      <span>{name}</span>
    </span>
  );
}

function SourceCard({
  title,
  source,
  health,
}: {
  title: string;
  source?: Source | null;
  health?: SourceHealth | null;
}) {
  const { t } = useTranslation();

  return (
    <section
      aria-label={title}
      className="min-w-0 space-y-2 rounded-lg border border-default-200 p-3"
    >
      <h3 className="text-xs text-default-500">{title}</h3>
      {source ? (
        <>
          <div className="text-sm font-medium">
            <SourceOption source={source} />
          </div>
          <AcquisitionSourceDetails health={health} source={source} />
        </>
      ) : (
        <p className="text-sm text-default-500">{t("acquisition.sourceRevision.chooseHint")}</p>
      )}
    </section>
  );
}

/** Review one frozen replacement. Changing selection or task invalidates every previous preview. */
export default function SourceRevisionModal({
  taskId,
  onChanged,
  onDestroyed,
}: DestroyableProps & {
  taskId: number;
  onChanged?: () => void;
}) {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(true);
  const [options, setOptions] = useState<SourceRevisionOptions>();
  const [selectedKey, setSelectedKey] = useState("");
  const [preview, setPreview] = useState<SourceRevisionPreview>();
  const [loading, setLoading] = useState(true);
  const [checking, setChecking] = useState(false);
  const [applying, setApplying] = useState(false);
  const [error, setError] = useState<"load" | "preview" | "unknown" | "rejected">();
  const [reason, setReason] = useState<string>();
  const [expired, setExpired] = useState(false);
  const generation = useRef(0);
  const controller = useRef<AbortController>();
  const applyingRef = useRef(false);
  const consumed = useRef(new Set<string>());
  const mounted = useRef(true);
  const currentOptions = options?.taskId === taskId ? options : undefined;
  const candidates = currentOptions?.candidates ?? [];
  const selected = candidates.find((item) => candidateKey(item) === selectedKey);

  const invalidate = useCallback(() => {
    generation.current++;
    controller.current?.abort();
    setPreview(undefined);
    setChecking(false);
    setExpired(false);
  }, []);
  const load = useCallback(async () => {
    if (applyingRef.current) return;
    invalidate();
    const request = generation.current;
    const abort = new AbortController();

    controller.current = abort;
    setOptions(undefined);
    setSelectedKey("");
    setLoading(true);
    setError(undefined);
    setReason(undefined);
    try {
      const value = await sourceRevisionApi.options(taskId, abort.signal);

      if (request !== generation.current || !mounted.current) return;
      if (value.taskId !== taskId) throw new Error("SOURCE_REVISION_TASK_CHANGED");
      setOptions(value);
    } catch {
      if (request === generation.current && mounted.current) setError("load");
    } finally {
      if (request === generation.current && mounted.current) setLoading(false);
    }
  }, [invalidate, taskId]);

  useEffect(() => {
    mounted.current = true;
    void load();

    return () => {
      mounted.current = false;
      generation.current++;
      controller.current?.abort();
    };
  }, [load]);
  useEffect(() => {
    if (!preview) return;
    const timer = setTimeout(() => {
      setPreview(undefined);
      setExpired(true);
    }, millisecondsUntil(preview.expiresAt));

    return () => clearTimeout(timer);
  }, [preview]);

  const check = async () => {
    if (
      applyingRef.current ||
      loading ||
      checking ||
      error === "unknown" ||
      !selected ||
      !currentOptions?.canRevise ||
      !currentOptions.expectedRunId
    )
      return;
    invalidate();
    const request = generation.current;
    const abort = new AbortController();

    controller.current = abort;
    setChecking(true);
    setError(undefined);
    setReason(undefined);
    try {
      const value = await sourceRevisionApi.preview(
        taskId,
        {
          expectedRevision: currentOptions.expectedRevision,
          expectedRunId: currentOptions.expectedRunId,
          partId: selected.partId,
          candidateId: selected.id,
        },
        abort.signal,
      );

      if (request !== generation.current || !mounted.current) return;
      if (
        value.taskId !== taskId ||
        value.expectedRevision !== currentOptions.expectedRevision ||
        value.expectedRunId !== currentOptions.expectedRunId ||
        value.partId !== selected.partId ||
        !value.previewToken
      )
        throw new Error("SOURCE_REVISION_TASK_CHANGED");
      if (millisecondsUntil(value.expiresAt) <= 0) setExpired(true);
      else setPreview(value);
    } catch (failure) {
      if (request !== generation.current || !mounted.current) return;
      setError("preview");
      if (failure instanceof Error && sourceRevisionReasonCodes.has(failure.message))
        setReason(failure.message);
    } finally {
      if (request === generation.current && mounted.current) setChecking(false);
    }
  };

  const apply = async () => {
    if (
      applyingRef.current ||
      !preview?.canApply ||
      !currentOptions?.canRevise ||
      millisecondsUntil(preview.expiresAt) <= 0 ||
      consumed.current.has(preview.previewToken) ||
      error === "unknown"
    )
      return;
    applyingRef.current = true;
    consumed.current.add(preview.previewToken);
    setApplying(true);
    setError(undefined);
    setReason(undefined);
    try {
      const result = await sourceRevisionApi.apply(taskId, preview.previewToken);

      if (result.taskId !== taskId || !result.applied)
        throw new Error("SOURCE_REVISION_TASK_CHANGED");
      onChanged?.();
      if (mounted.current) setVisible(false);
    } catch (failure) {
      if (!mounted.current) return;
      const code = failure instanceof Error ? failure.message : undefined;

      setError(
        code && code !== "SOURCE_REVISION_OUTCOME_UNKNOWN" && sourceRevisionReasonCodes.has(code)
          ? "rejected"
          : "unknown",
      );
      setReason(code && sourceRevisionReasonCodes.has(code) ? code : undefined);
      onChanged?.();
    } finally {
      applyingRef.current = false;
      if (mounted.current) setApplying(false);
    }
  };
  const close = () => {
    if (applyingRef.current) return;
    invalidate();
    setVisible(false);
  };
  const requirements = preview
    ? [
        ...preview.preflight.requirements,
        ...preview.preflight.candidates.flatMap((item) => item.requirements),
      ].filter((item, index, list) => list.findIndex((other) => other.code === item.code) === index)
    : [];
  const prepared =
    preview?.preflight.candidates.find((item) => item.candidate.id === preview.proposedSource.id) ??
    preview?.preflight.candidates[0];
  const canCheck =
    !!currentOptions?.canRevise && !!selected && !loading && !applying && error !== "unknown";
  const canApply =
    !!preview?.canApply &&
    !!currentOptions?.canRevise &&
    !checking &&
    !loading &&
    !consumed.current.has(preview.previewToken) &&
    millisecondsUntil(preview.expiresAt) > 0;

  return (
    <Modal
      footer={
        <div className="flex w-full flex-wrap items-center justify-end gap-2">
          <Button isDisabled={applying} variant="light" onPress={close}>
            {t("acquisition.plan.close")}
          </Button>
          <Button
            isDisabled={!canCheck}
            isLoading={checking}
            variant="flat"
            onPress={() => void check()}
          >
            {t("acquisition.sourceRevision.preview")}
          </Button>
          <Button
            color="primary"
            isDisabled={!canApply}
            isLoading={applying}
            onPress={() => void apply()}
          >
            {t("acquisition.sourceRevision.apply")}
          </Button>
        </div>
      }
      hideCloseButton={applying}
      isDismissable={!applying}
      isKeyboardDismissDisabled={applying}
      size="5xl"
      title={t("acquisition.sourceRevision.title")}
      visible={visible}
      onClose={close}
      onDestroyed={onDestroyed}
    >
      <div className="space-y-3">
        <div className="flex flex-wrap items-start justify-between gap-2">
          <p className="min-w-0 flex-1 text-xs leading-relaxed text-default-500">
            {t("acquisition.sourceRevision.description")}
          </p>
          <Button
            isDisabled={applying}
            isLoading={loading}
            size="sm"
            variant="light"
            onPress={() => void load()}
          >
            {t("acquisition.sourceRevision.refresh")}
          </Button>
        </div>
        {loading && (
          <div className="flex justify-center py-8">
            <Spinner aria-label={t("acquisition.sourceRevision.loading")} />
          </div>
        )}
        {error && (
          <p className="text-sm text-warning-600" role="alert">
            {t(
              reason
                ? sourceRevisionReasonKey(reason)
                : `acquisition.sourceRevision.error.${error}`,
            )}
          </p>
        )}
        {expired && (
          <p className="text-sm text-warning-600" role="status">
            {t("acquisition.sourceRevision.expired")}
          </p>
        )}
        {currentOptions && (
          <>
            {!currentOptions.canRevise && (
              <p className="rounded-lg bg-warning-50 p-3 text-sm text-warning-700" role="status">
                {t(sourceRevisionReasonKey(currentOptions.reasonCode))}
              </p>
            )}
            {candidates.length ? (
              <Select
                isDisabled={applying || error === "unknown"}
                label={t("acquisition.sourceRevision.candidate")}
                placeholder={t("acquisition.sourceRevision.chooseHint")}
                selectedKeys={selectedKey ? [selectedKey] : []}
                size="sm"
                onSelectionChange={(keys) => {
                  if (applyingRef.current || error === "unknown") return;
                  invalidate();
                  setError(undefined);
                  setReason(undefined);
                  setSelectedKey(Array.from(keys)[0]?.toString() ?? "");
                }}
              >
                {candidates.map((item) => (
                  <SelectItem
                    key={candidateKey(item)}
                    description={item.source.url}
                    textValue={sourceHost(item.source) || item.source.url}
                  >
                    <SourceOption source={item.source} />
                  </SelectItem>
                ))}
              </Select>
            ) : (
              <p className="text-sm text-default-500">
                {t("acquisition.sourceRevision.noCandidates")}
              </p>
            )}
            <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
              <SourceCard
                source={preview?.currentSource ?? currentOptions.currentSource}
                title={t("acquisition.sourceRevision.current")}
              />
              <SourceCard
                health={
                  prepared?.availability
                    ? {
                        ...prepared.availability,
                        code: prepared.availability.reasonCode,
                      }
                    : undefined
                }
                source={preview?.proposedSource ?? selected?.source}
                title={t("acquisition.sourceRevision.proposed")}
              />
            </div>
            {(preview?.evidence || selected?.evidence) && (
              <section className="space-y-1">
                <h3 className="text-xs font-medium">{t("acquisition.sourceRevision.evidence")}</h3>
                <SourceRevisionEvidence evidence={(preview?.evidence || selected?.evidence)!} />
              </section>
            )}
          </>
        )}
        {preview && (
          <>
            <div className="flex flex-wrap items-center justify-between gap-2">
              <Chip
                color={preview.processingChanged ? "warning" : "default"}
                size="sm"
                variant="flat"
              >
                {t(
                  preview.processingChanged
                    ? "acquisition.sourceRevision.processingChanged"
                    : "acquisition.sourceRevision.processingUnchanged",
                )}
              </Chip>
              <span className="text-xs text-default-500">
                {t("acquisition.sourceRevision.expiresAt", {
                  time: parseServerTime(preview.expiresAt)?.toLocaleTimeString() ?? "",
                })}
              </span>
            </div>
            <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
              <section
                aria-label={t("acquisition.sourceRevision.currentProcessing")}
                className="space-y-2 rounded-lg bg-default-50 p-3"
              >
                <h3 className="text-xs font-medium">
                  {t("acquisition.sourceRevision.currentProcessing")}
                </h3>
                <PlanSummary plan={preview.currentProcessing} />
              </section>
              <section
                aria-label={t("acquisition.sourceRevision.proposedProcessing")}
                className="space-y-2 rounded-lg bg-default-50 p-3"
              >
                <h3 className="text-xs font-medium">
                  {t("acquisition.sourceRevision.proposedProcessing")}
                </h3>
                <PlanSummary plan={preview.proposedProcessing} />
              </section>
            </div>
            <section aria-label={t("acquisition.sourceRevision.preflight")} className="space-y-2">
              <h3 className="text-sm font-medium">{t("acquisition.sourceRevision.preflight")}</h3>
              {!!prepared?.methods.length && (
                <div className="flex flex-wrap gap-2">
                  {prepared.methods.map((method) => (
                    <Chip
                      key={method.id}
                      color={method.ready ? "success" : "warning"}
                      size="sm"
                      variant="flat"
                    >
                      {downloadMethodLabel(method, t)} ·{" "}
                      {t(
                        method.ready
                          ? "acquisition.sourceRevision.ready"
                          : "acquisition.sourceRevision.notReady",
                      )}
                    </Chip>
                  ))}
                </div>
              )}
              {!preview.canApply && (
                <p className="text-sm text-warning-600" role="status">
                  {t(sourceRevisionReasonKey(preview.reasonCode))}
                </p>
              )}
              {requirements.map((requirement) => {
                const key = `acquire.requirement.${requirement.code}`;
                const label = t(key);

                return (
                  <p key={requirement.code} className="text-xs text-warning-600">
                    {label && label !== key
                      ? label
                      : t("acquisition.sourceRevision.requirementUnknown")}
                  </p>
                );
              })}
              {!preview.canApply && prepared && (
                <MethodPreparation
                  methods={prepared.methods}
                  requirements={requirements}
                  accountId={preview.proposedSource.accountId ?? undefined}
                  onAccountChange={() => undefined}
                  accountSelectionDisabled
                  isDisabled={applying || checking || error === "unknown"}
                  onRefresh={() => void check()}
                />
              )}
            </section>
          </>
        )}
      </div>
    </Modal>
  );
}
