import ArchivePasswordInput from "@/components/PasswordSelector/ArchivePasswordInput";
import type { DestroyableProps } from "@/components/bakaui/types";
import type { BakabaseModulesWorkflowAbstractionsModelsViewWorkflowDefinitionViewModel as Definition } from "@/sdk/Api";
import { lazy, Suspense, useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router-dom";
import { SelectItem } from "@heroui/react";
import { Button, Checkbox, Input, Modal, Select, Spinner } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { useDownloaderGlobalOptionsStore } from "@/stores/options";
import DownloadDirectoryField from "@/components/ThirdPartyConfig/base/DownloadDirectoryField";
import { workflowLabel } from "@/components/Workflow/builtinLabels";
import BApi from "@/sdk/BApi";
import MethodPreparation from "./MethodPreparation";
import { defaultAcquireAdapter } from "./api";
import { manualCandidate, manualInputMethods } from "./manualInputs";
import { useDownloadPreflight } from "./useDownloadPreflight";
import SourceResolution from "./SourceResolution";
import { downloadMethodLabel } from "./methodLabels";
import ProcessingPlanPreparation from "./ProcessingPlanPreparation";
import {
  toggleCandidateSelection,
  type AcquireAdapter,
  type AcquireMode,
  type AcquireRequest,
  type AcquireSubmission,
  type DownloadCandidate,
} from "./models";

const RunDetail = lazy(() => import("@/components/Workflow/WorkflowRunDetailModal"));
const PlanTaskDetail = lazy(() => import("./PlanTask/PlanTaskDetailModal"));
const EMPTY_CANDIDATES: DownloadCandidate[] = [];
export interface AcquireModalProps extends DestroyableProps {
  purpose?: "download" | "plan";
  planLabels?: { title: string; confirm: string; completed: string };
  initialOutputDirectory?: string;
  title?: string;
  candidates?: DownloadCandidate[];
  adapter?: AcquireAdapter;
  onCreated?: () => void;
}

/** Intake adapters provide candidates and authenticated API calls, never source-module objects. */
export default function AcquireModal({
  purpose = "download",
  planLabels,
  initialOutputDirectory,
  title: initialTitle = "",
  candidates: initialCandidates,
  adapter = defaultAcquireAdapter,
  onCreated,
  onDestroyed,
}: AcquireModalProps) {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const navigate = useNavigate();
  const defaultDirectory = useDownloaderGlobalOptionsStore(
    (state) => state.data?.defaultDownloadPath,
  );
  const [visible, setVisible] = useState(true);
  const [title, setTitle] = useState(initialTitle);
  const [mode, setMode] = useState<AcquireMode>("files");
  const [outputDirectory, setOutputDirectory] = useState(
    initialOutputDirectory ?? defaultDirectory ?? "",
  );
  const [candidates] = useState(initialCandidates ?? EMPTY_CANDIDATES);
  const [selected, setSelected] = useState<string[]>(initialCandidates ? [] : ["manual"]);
  const [url, setUrl] = useState("");
  const [kind, setKind] = useState("auto");
  const [accessCode, setAccessCode] = useState("");
  const [archivePassword, setArchivePassword] = useState("");
  const [accountIds, setAccountIds] = useState<Record<string, string | undefined>>({});
  const [methodIds, setMethodIds] = useState<Record<string, string>>({});
  const [processingPlans, setProcessingPlans] = useState<Record<string, string>>({});
  const [workflowId, setWorkflowId] = useState<string>();
  const [workflows, setWorkflows] = useState<Definition[]>([]);
  const [error, setError] = useState<string>();
  const [saving, setSaving] = useState(false);
  const [submission, setSubmission] = useState<AcquireSubmission>();
  const submitLock = useRef(false);
  const manual = initialCandidates === undefined;
  const request: AcquireRequest = useMemo(
    () => ({
      mode,
      title: title.trim() || undefined,
      workflowDefinitionId: workflowId ? Number(workflowId) : undefined,
      candidates: (manual
        ? [
            manualCandidate(kind, {
              id: "manual",
              url: url.trim(),
              accessCode: accessCode.trim() || undefined,
              archivePassword: archivePassword || undefined,
            }),
          ]
        : candidates
      ).map((candidate: DownloadCandidate) => ({
        ...candidate,
        methodId: methodIds[candidate.id] ?? candidate.methodId,
        accountId: accountIds[candidate.id] ?? candidate.accountId,
        extractionPlanJson: Object.prototype.hasOwnProperty.call(processingPlans, candidate.id)
          ? processingPlans[candidate.id]
          : candidate.extractionPlanJson,
      })),
      selectedCandidateIds: selected,
    }),
    [
      mode,
      title,
      accountIds,
      workflowId,
      manual,
      url,
      kind,
      accessCode,
      archivePassword,
      candidates,
      methodIds,
      processingPlans,
      selected,
    ],
  );
  const {
    result: checked,
    previousResult,
    error: preflightError,
    refreshing,
    refresh,
    revision,
  } = useDownloadPreflight(
    adapter,
    request,
    mode === "lead" ? "" : outputDirectory.trim(),
    !submission,
  );
  const preparedCandidates =
    (checked ?? previousResult)?.candidates.filter((item) =>
      request.candidates.some((candidate) => candidate.id === item.candidate.id),
    ) ?? [];

  useEffect(() => {
    let disposed = false;
    setWorkflows([]);
    if (mode === "lead" || purpose === "plan") return;
    void BApi.workflow
      .searchWorkflows({
        triggerKind: mode === "library" ? "acquisition.requested" : "download.sources",
        enabledOnly: true,
      })
      .then((response) => {
        if (!disposed && !response.code) setWorkflows(response.data ?? []);
      })
      .catch(() => {
        if (!disposed) setError(t("cloudDownload.workflow.loadFailed"));
      });
    return () => {
      disposed = true;
    };
  }, [mode, purpose]);

  const submit = async () => {
    if (submitLock.current || !checked?.canSubmit || selected.length === 0) return;
    submitLock.current = true;
    setSaving(true);
    setError(undefined);
    try {
      // Submission validates the current source revision and readiness again on the server.
      const result = await adapter.submit(request, mode === "lead" ? "" : outputDirectory.trim());
      setSubmission(result);
      onCreated?.();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : String(failure));
      refresh();
    } finally {
      submitLock.current = false;
      setSaving(false);
    }
  };

  return (
    <Modal
      visible={visible}
      onClose={() => setVisible(false)}
      onDestroyed={onDestroyed}
      isDismissable={!saving}
      size="3xl"
      title={
        purpose === "plan" && planLabels
          ? planLabels.title
          : t(purpose === "plan" ? "acquire.plan.title" : "acquire.title")
      }
      footer={
        <div className="flex justify-end gap-2">
          <Button variant="light" isDisabled={saving} onPress={() => setVisible(false)}>
            {t("common.action.close")}
          </Button>
          {!submission && (
            <Button
              color="primary"
              isLoading={saving}
              isDisabled={!checked?.canSubmit || !selected.length || saving}
              onPress={() => void submit()}
            >
              {purpose === "plan" && planLabels
                ? planLabels.confirm
                : t(purpose === "plan" ? "acquire.plan.create" : `acquire.confirm.${mode}`)}
            </Button>
          )}
        </div>
      }
    >
      {submission ? (
        <div className="space-y-3" role="status">
          <p className="text-success">
            {purpose === "plan" && planLabels
              ? planLabels.completed
              : t(
                  purpose === "plan"
                    ? "acquire.plan.created"
                    : `acquire.submitted.${submission.mode}`,
                )}
          </p>
          {purpose === "plan" &&
            submission.acquisitionTaskIds.map((taskId) => (
              <Button
                key={taskId}
                color="primary"
                variant="flat"
                onPress={() =>
                  createPortal(
                    ({ onDestroyed: close }: DestroyableProps) => (
                      <Suspense fallback={<Spinner />}>
                        <PlanTaskDetail taskId={taskId} onDestroyed={close} onChanged={onCreated} />
                      </Suspense>
                    ),
                    {},
                  )
                }
              >
                {t("acquire.plan.openTask", { id: taskId })}
              </Button>
            ))}
          {submission.runs.map((run) => (
            <Button
              key={run.id}
              variant="flat"
              color="primary"
              onPress={() =>
                createPortal(
                  ({ onDestroyed: close }: DestroyableProps) => (
                    <Suspense fallback={<Spinner />}>
                      <RunDetail runId={run.id} onDestroyed={close} />
                    </Suspense>
                  ),
                  {},
                )
              }
            >
              {t("acquire.openRun", { id: run.id })}
            </Button>
          ))}
          {(submission.resourceIds.length > 0 || submission.acquisitionTaskIds.length > 0) && (
            <Button
              variant="flat"
              onPress={() => {
                setVisible(false);
                navigate("/acquisitions");
              }}
            >
              {t("acquire.openAcquisitions")}
            </Button>
          )}
        </div>
      ) : (
        <div className="space-y-4">
          {purpose === "plan" && (
            <p className="text-sm text-default-500">{t("acquire.plan.description")}</p>
          )}
          {purpose !== "plan" && (
            <div
              className="grid gap-2 sm:grid-cols-3"
              role="group"
              aria-label={t("acquire.mode.label")}
            >
              {(["files", "library", "lead"] as const).map((value) => (
                <Button
                  key={value}
                  isDisabled={saving}
                  aria-pressed={mode === value}
                  color={mode === value ? "primary" : "default"}
                  variant={mode === value ? "flat" : "bordered"}
                  className="h-auto flex-col items-start whitespace-normal p-3 text-left"
                  onPress={() => {
                    setMode(value);
                    setWorkflowId(undefined);
                  }}
                >
                  <span className="font-medium">{t(`acquire.mode.${value}`)}</span>
                  <span className="text-xs font-normal">{t(`acquire.mode.${value}Hint`)}</span>
                </Button>
              ))}
            </div>
          )}
          <Input
            label={t(mode === "files" ? "acquire.resourceTitle" : "acquire.resourceTitle.required")}
            isRequired={mode !== "files"}
            value={title}
            onValueChange={setTitle}
            isDisabled={saving}
          />
          {manual && (
            <div className="space-y-3">
              <Select
                label={t("acquire.inputKind")}
                selectedKeys={[kind]}
                disallowEmptySelection
                onSelectionChange={(keys) => {
                  setKind(String(Array.from(keys)[0]));
                  setUrl("");
                  setMethodIds({});
                  setAccountIds({});
                }}
              >
                {manualInputMethods.map((method) => (
                  <SelectItem key={method.id}>{t(method.labelKey)}</SelectItem>
                ))}
              </Select>
              <Input
                label={t(
                  manualInputMethods.find((method) => method.id === kind)?.kind === "remote"
                    ? "acquire.download.remotePath"
                    : "cloudDownload.url",
                )}
                value={url}
                onValueChange={setUrl}
                placeholder={
                  manualInputMethods.find((method) => method.id === kind)?.kind === "remote"
                    ? "/folder"
                    : "https://…"
                }
              />
              <div className="grid gap-3 sm:grid-cols-2">
                <Input
                  label={t("cloudDownload.accessCode")}
                  value={accessCode}
                  onValueChange={setAccessCode}
                />
                <ArchivePasswordInput
                  label={t("acquire.archivePassword")}
                  value={archivePassword}
                  onValueChange={setArchivePassword}
                />
              </div>
            </div>
          )}
          {mode !== "lead" && (
            <>
              <DownloadDirectoryField
                value={outputDirectory}
                onChange={setOutputDirectory}
                label={t("bt.outputDirectory")}
              />
              {purpose !== "plan" && (
                <Select
                  label={t(
                    mode === "library" ? "acquire.workflow.library" : "acquire.workflow.files",
                  )}
                  placeholder={t("acquire.workflow.default")}
                  selectedKeys={workflowId ? [workflowId] : []}
                  onSelectionChange={(keys) =>
                    setWorkflowId(Array.from(keys)[0] as string | undefined)
                  }
                >
                  {workflows.map((workflow) => (
                    <SelectItem key={String(workflow.id)}>{workflowLabel(workflow, t)}</SelectItem>
                  ))}
                </Select>
              )}
            </>
          )}
          <div className="flex items-center justify-between gap-2">
            <p className="text-xs text-default-500">
              {t(mode === "lead" ? "acquire.leadHint" : "acquire.chooseHint")}
            </p>
            <Button size="sm" variant="light" isLoading={refreshing} onPress={refresh}>
              {t("acquire.preparation.refresh")}
            </Button>
          </div>
          <div className="max-h-[50vh] space-y-3 overflow-auto">
            {preparedCandidates.map((prepared) => {
              const candidate = {
                ...prepared.candidate,
                title:
                  request.candidates.find((item) => item.id === prepared.candidate.id)?.title ||
                  prepared.candidate.title,
              };
              const selectedMethodId =
                methodIds[candidate.id] ??
                prepared.selectedMethodId ??
                candidate.methodId ??
                prepared.methods[0]?.id;
              const selectedMethods = prepared.methods.filter(
                (method) => method.id === selectedMethodId,
              );
              const requirements = [
                ...prepared.requirements,
                ...selectedMethods.flatMap((method) => method.requirements),
              ].filter(
                (item, index, values) =>
                  values.findIndex(
                    (other) => other.code === item.code && other.message === item.message,
                  ) === index,
              );
              return (
                <div
                  key={candidate.id}
                  className="space-y-2 rounded-lg border border-default-200 p-3"
                >
                  <Checkbox
                    aria-label={candidate.url}
                    isSelected={selected.includes(candidate.id)}
                    isDisabled={saving || (!prepared.canSelect && !selected.includes(candidate.id))}
                    onValueChange={(value) =>
                      setSelected((previous) =>
                        toggleCandidateSelection(
                          previous,
                          candidate,
                          request.candidates,
                          value,
                          mode,
                        ),
                      )
                    }
                  >
                    <div className="min-w-0 text-xs">
                      {candidate.title && <p className="font-medium">{candidate.title}</p>}
                      <p className="break-all">{candidate.url}</p>
                    </div>
                  </Checkbox>
                  <SourceResolution source={prepared.resolvedSource} />
                  <div className="flex flex-wrap gap-2 text-xs">
                    <span>
                      {t(`acquire.readiness.${prepared.state}`, { defaultValue: prepared.state })}
                    </span>
                    {selectedMethods.map((method) => (
                      <span key={method.id} className="text-default-500">
                        {downloadMethodLabel(method, t)}
                        {method.supportsTransfer ? ` · ${t("acquire.method.transfer")}` : ""}
                      </span>
                    ))}
                  </div>
                  {requirements.length > 0 && (
                    <ul className="space-y-1 text-xs text-warning-600">
                      {requirements.map((requirement) => (
                        <li key={`${requirement.kind}:${requirement.code}`}>
                          {t(`acquire.requirement.${requirement.code}`, {
                            defaultValue: requirement.message,
                          })}
                        </li>
                      ))}
                    </ul>
                  )}
                  {prepared.methods.length > 1 && (
                    <Select
                      label={t("acquire.method.label")}
                      selectedKeys={[selectedMethodId!]}
                      disallowEmptySelection
                      isDisabled={saving}
                      onSelectionChange={(keys) => {
                        setMethodIds((current) => ({
                          ...current,
                          [candidate.id]: String(Array.from(keys)[0]),
                        }));
                        setAccountIds((current) => ({ ...current, [candidate.id]: undefined }));
                      }}
                    >
                      {prepared.methods.map((method) => (
                        <SelectItem key={method.id}>{downloadMethodLabel(method, t)}</SelectItem>
                      ))}
                    </Select>
                  )}
                  {selectedMethods.map((method) => (
                    <div key={method.id} className="text-xs text-default-500">
                      {method.engineId && (
                        <p>{t("acquire.method.engine", { engine: method.engineId })}</p>
                      )}
                      {method.scope && <p>{t(`acquire.methodScope.${method.scope}`)}</p>}
                    </div>
                  ))}
                  <MethodPreparation
                    methods={selectedMethods}
                    requirements={requirements}
                    accountId={accountIds[candidate.id] ?? candidate.accountId ?? undefined}
                    onAccountChange={(id) =>
                      setAccountIds((current) => ({ ...current, [candidate.id]: id }))
                    }
                    onRefresh={refresh}
                    refreshKey={revision}
                    isDisabled={saving}
                  />
                  {mode !== "lead" && (
                    <ProcessingPlanPreparation
                      plan={prepared.processingPlan}
                      value={
                        request.candidates.find((item) => item.id === candidate.id)
                          ?.extractionPlanJson ?? ""
                      }
                      edited={Object.prototype.hasOwnProperty.call(processingPlans, candidate.id)}
                      isDisabled={saving}
                      checking={!checked}
                      onChange={(value) =>
                        setProcessingPlans((current) => ({ ...current, [candidate.id]: value }))
                      }
                      onReset={() =>
                        setProcessingPlans((current) => {
                          const next = { ...current };
                          delete next[candidate.id];
                          return next;
                        })
                      }
                    />
                  )}
                </div>
              );
            })}
          </div>
          {checked?.requirements.map((requirement) => (
            <p key={requirement.code} className="text-sm text-warning-600">
              {t(`acquire.requirement.${requirement.code}`, { defaultValue: requirement.message })}
            </p>
          ))}
          {refreshing && (
            <p role="status" className="text-xs text-default-500">
              {t("acquire.preparation.checking")}
            </p>
          )}
          <p className="text-xs text-default-500">{t("acquire.preparation.recheck")}</p>
        </div>
      )}
      {(preflightError || error) && (
        <p role="alert" className="text-sm text-danger">
          {error || preflightError}
        </p>
      )}
    </Modal>
  );
}
