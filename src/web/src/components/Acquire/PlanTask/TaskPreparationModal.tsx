import { useEffect, useMemo, useRef, useState } from "react";
import { v4 as uuidv4 } from "uuid";
import { useTranslation } from "react-i18next";
import { EditOutlined, PlusOutlined } from "@ant-design/icons";
import { Button, Checkbox, Chip, Input, Modal, Radio, RadioGroup, Select, Spinner } from "@/components/bakaui";
import { downloadSourceProviderLabel } from "@/components/DownloadSource";
import ArchivePasswordInput from "@/components/PasswordSelector/ArchivePasswordInput";
import SourceHealthBadge from "@/components/SourceAvailability/SourceHealthBadge";
import SourcePriorityModal from "@/components/SourceAvailability/SourcePriorityModal";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import SourceProcessingPlanPanel from "@/components/ProcessingPlanRevisions/SourceProcessingPlanPanel";
import ExternalLink from "@/components/ExternalLink";
import SourceResolution from "../SourceResolution";
import MethodPreparation from "../MethodPreparation";
import { downloadMethodLabel } from "../methodLabels";
import type { DownloadCandidate, PreparedCandidate } from "../models";
import DraftPreparation from "./DraftPreparation";
import { planTaskApi } from "./api";
import type { DraftSource, PlanTaskDetail } from "./models";

type Preview = Awaited<ReturnType<typeof planTaskApi.previewPreparation>>;
type SharedDownloadPreview = NonNullable<Preview["sharedDownload"]>;
type PreparationInput = Parameters<typeof planTaskApi.confirmPreparation>[1];
type PreparationSource = DraftSource & { groupId?: string };
type Relationship = "mirrors" | "independent";

function sourceFromCandidate(candidate: DownloadCandidate): PreparationSource {
  const { extractionPlanJson, mirrorGroupConfirmed: _mirror, sourceCorrection: _correction, ...source } = candidate;
  // The backend emitted this processing plan; keep its evidence and exact source credentials.
  try { return { ...source, processing: extractionPlanJson ? JSON.parse(extractionPlanJson) : undefined }; }
  catch { return source; } // An omitted processing edit preserves the server's source evidence.
}

function sourceLabel(source: DownloadCandidate) {
  try { return new URL(source.url).hostname; } catch { return source.providerId ?? source.kind; }
}

function browserSourceUrl(source: DownloadCandidate) {
  if (source.kind === "remote") return undefined;
  try {
    const url = new URL(source.url);
    return ["http:", "https:"].includes(url.protocol) ? source.url : undefined;
  } catch { return undefined; }
}

function SharedDownloadReview({ preview, saving, busy, stale, error, onClose, onConfirm }: {
  preview: SharedDownloadPreview;
  saving: boolean;
  busy: boolean;
  stale: boolean;
  error?: string;
  onClose: () => void;
  onConfirm: (keepPartId: string, useArchiveDirectories: boolean) => void;
}) {
  const { t } = useTranslation();
  const [keepPartId, setKeepPartId] = useState("");
  const [useArchiveDirectories, setUseArchiveDirectories] = useState(false);
  const disabled = busy || saving || stale;
  const requiresRefresh = error === "SHARED_DOWNLOAD_UNAVAILABLE" || error === "PLAN_VERSION_CONFLICT";
  return (
    <Modal visible size="2xl" scrollBehavior="inside" title={t("acquisition.preparation.sharedDownload.title")}
      isDismissable={!saving} isKeyboardDismissDisabled={saving} onClose={() => { if (!saving) onClose(); }}
      footer={<>
        <Button variant="light" isDisabled={saving} onPress={onClose}>{t("common.action.cancel")}</Button>
        <Button color="primary" isLoading={saving} isDisabled={disabled || requiresRefresh || !keepPartId}
          onPress={() => {
            if (!disabled && !requiresRefresh && keepPartId) onConfirm(keepPartId, useArchiveDirectories);
          }}>
          {t("acquisition.preparation.sharedDownload.confirm")}
        </Button>
      </>}>
      <div className="space-y-4">
        <p className="text-sm text-default-600">{t("acquisition.preparation.sharedDownload.scope")}</p>
        <RadioGroup label={t("acquisition.preparation.sharedDownload.keepGroup")} value={keepPartId}
          isDisabled={disabled || requiresRefresh} onValueChange={setKeepPartId}>
          {preview.groups.map(group => (
            <Radio key={group.partId} value={group.partId} className="max-w-full items-start">
              <span className="block break-words font-medium">{group.title || t("acquisition.preparation.sourceGroup")}</span>
              <span className="block break-all text-xs text-default-500">{group.deliveryDirectory}</span>
            </Radio>
          ))}
        </RadioGroup>
        {preview.alreadyUsesArchiveDirectories ? (
          <p className="text-sm text-default-600">{t("acquisition.preparation.sharedDownload.archiveDirectoriesAlreadyEnabled")}</p>
        ) : preview.canUseArchiveDirectories && (
          <Checkbox isSelected={useArchiveDirectories} isDisabled={disabled || requiresRefresh} onValueChange={setUseArchiveDirectories}>
            {t("acquisition.preparation.sharedDownload.archiveDirectories")}
          </Checkbox>
        )}
        <p className="text-sm text-default-500">{t("acquisition.preparation.sharedDownload.next")}</p>
        {(stale || error) && <p role="alert" className="text-sm text-danger">
          {t(`acquisition.plan.replan.error.${stale ? "PLAN_VERSION_CONFLICT" : error}`, {
            defaultValue: t("acquisition.plan.actionFailed"),
          })}
        </p>}
      </div>
    </Modal>
  );
}

export default function TaskPreparationModal({ detail, busy, onAction, onClose }: {
  detail: PlanTaskDetail;
  busy: boolean;
  onAction: (action: () => Promise<PlanTaskDetail>) => Promise<void>;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [sources, setSources] = useState<PreparationSource[]>();
  const [relationship, setRelationship] = useState<Relationship>("mirrors");
  const [selected, setSelected] = useState<string[]>();
  const [allowUnavailableSourceIds, setAllowUnavailableSourceIds] = useState<string[]>();
  const [preview, setPreview] = useState<Preview>();
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string>();
  const [retry, setRetry] = useState(0);
  const [editing, setEditing] = useState<PreparationSource>();
  const [editingRevision, setEditingRevision] = useState<number>();
  const [saving, setSaving] = useState(false);
  const [editedRevision, setEditedRevision] = useState<number>();
  const [previewRevision, setPreviewRevision] = useState<number>();
  const [sharedReview, setSharedReview] = useState<{ taskId: number; revision: number; preview: SharedDownloadPreview }>();
  const [sharedError, setSharedError] = useState<string>();
  const sourceRevision = editedRevision ?? previewRevision;
  const stale = sourceRevision !== undefined && sourceRevision !== detail.task.planRevision;
  const sequence = useRef(0);
  const submitLock = useRef(false);
  const checkedInput = useRef<string>();
  const hasSources = sources !== undefined ? sources.length > 0 :
    !!detail.draftSources?.length || !!detail.plan?.parts.some(part => part.sources.length);
  const request = useMemo(() => ({ expectedRevision: sourceRevision ?? detail.task.planRevision, sources,
    relationship, selectedSourceIds: selected, allowUnavailableSourceIds }),
    [detail.task.planRevision, sourceRevision, sources, relationship, selected, allowUnavailableSourceIds]);

  useEffect(() => {
    if (!hasSources || stale) return;
    const id = ++sequence.current;
    const controller = new AbortController();
    const healthInput = JSON.stringify([detail.task.planRevision, sources, retry]);
    const checkAvailability = checkedInput.current !== healthInput;
    setRefreshing(true);
    setError(undefined);
    void planTaskApi.previewPreparation(detail.task.id, { ...request, checkAvailability }, controller.signal).then(next => {
      if (id !== sequence.current) return;
      checkedInput.current = healthInput;
      setPreviewRevision(next.expectedRevision);
      setPreview(next);
      // The server preserves choices, or recommends from the configured global priority.
      const choice = next.groups.flatMap(group => group.selectedSourceIds);
      setSelected(current => JSON.stringify(current) === JSON.stringify(choice) ? current : choice);
      setAllowUnavailableSourceIds(current => JSON.stringify(current) === JSON.stringify(next.allowUnavailableSourceIds)
        ? current : next.allowUnavailableSourceIds);
    }).catch(cause => {
      if (id === sequence.current) setError(cause instanceof Error ? cause.message : "ACQUISITION_PLAN_REQUEST_FAILED");
    }).finally(() => { if (id === sequence.current) setRefreshing(false); });
    return () => { sequence.current++; controller.abort(); };
  }, [detail.task.id, request, hasSources, retry, stale]);

  const currentSources = () => sources ?? preview?.groups.flatMap(group => group.sources.map(item => sourceFromCandidate(item.candidate))) ?? detail.draftSources ?? [];
  const editSource = (source: PreparationSource) => {
    setEditingRevision(editedRevision ?? detail.task.planRevision);
    setEditing({ ...source });
  };
  const clearSourceOverride = (id: string) => {
    const granted = allowUnavailableSourceIds ?? preview?.allowUnavailableSourceIds ?? [];
    if (!granted.includes(id)) return;
    setAllowUnavailableSourceIds(granted.filter(sourceId => sourceId !== id));
    setSelected(current => current?.filter(sourceId => sourceId !== id));
  };
  const patchSource = (id: string, values: Partial<PreparationSource>) => {
    clearSourceOverride(id);
    setEditedRevision(value => value ?? detail.task.planRevision);
    setSources(currentSources().map(source => source.id === id ? { ...source, ...values } : source));
  };
  const addSource = (groupId?: string) => editSource({ id: uuidv4(), kind: "auto", url: "", groupId });
  const toggle = (group: Preview["groups"][number], item: PreparedCandidate, checked: boolean) => {
    if (!item.canSelect && !item.canSelectManually) return;
    setEditedRevision(value => value ?? detail.task.planRevision);
    const ids = selected ?? preview?.groups.flatMap(value => value.selectedSourceIds) ?? [];
    const siblings = new Set(group.sources.map(source => source.candidate.id));
    const next = checked ? [...ids.filter(id => id !== item.candidate.id && (group.selectionMode !== "single" || !siblings.has(id))), item.candidate.id]
      : ids.filter(id => id !== item.candidate.id);
    setSelected(next);
    const grants = (allowUnavailableSourceIds ?? preview?.allowUnavailableSourceIds ?? []).filter(id => next.includes(id));
    if (checked && item.canSelectManually && item.availability?.status === "confirmedUnavailable") grants.push(item.candidate.id);
    setAllowUnavailableSourceIds([...new Set(grants)]);
  };
  const disabled = busy || saving || stale || !!sharedReview;
  const sharedReviewStale = sharedReview != null && (stale || sharedReview.taskId !== detail.task.id ||
    sharedReview.revision !== detail.task.planRevision || detail.task.workflowRunId != null ||
    sharedReview.preview.fingerprint !== preview?.sharedDownload?.fingerprint);
  const submitPreparation = (input: PreparationInput, reportError: (message?: string) => void) => {
    if (submitLock.current) return;
    submitLock.current = true;
    setSaving(true);
    reportError(undefined);
    const recordFailure = (cause: unknown) =>
      reportError(cause instanceof Error ? cause.message.split(":")[0].trim() : "ACQUISITION_PLAN_REQUEST_FAILED");
    void onAction(async () => {
      try {
        const next = await planTaskApi.confirmPreparation(detail.task.id, input);
        onClose();
        return next;
      } catch (cause) {
        recordFailure(cause);
        throw cause;
      }
    }).catch(recordFailure).finally(() => { submitLock.current = false; setSaving(false); });
  };
  const requirements = Array.from(new Map((preview?.requirements ?? []).map(requirement => [requirement.code, requirement])).values());
  return <>
    <Modal visible size="4xl" scrollBehavior="inside" title={t("acquisition.timeline.stage.preparation")}
      isDismissable={!sharedReview} isKeyboardDismissDisabled={!!sharedReview}
      onClose={() => { if (!sharedReview) onClose(); }}
      footer={<>
        <Button variant="light" isDisabled={saving || !!sharedReview} onPress={onClose}>{t("common.action.cancel")}</Button>
        {hasSources && <Button color="primary" isLoading={saving} isDisabled={disabled || refreshing || !preview?.canConfirm || !!error}
          onPress={() => {
            if (!disabled && !refreshing && preview?.canConfirm && !error)
              submitPreparation({ ...request, checkAvailability: false }, setError);
          }}>{t("acquisition.preparation.confirm")}</Button>}
      </>}>
      <div className="space-y-4">
        <p className="text-sm text-default-500">{t("acquisition.preparation.hint")}</p>
        {stale && <p role="alert" className="text-warning-700">{t("acquisition.plan.replan.error.PLAN_VERSION_CONFLICT")}</p>}
        {error && <div role="alert" className="flex flex-wrap items-center gap-2 text-sm text-danger">
          <span>{t(`acquisition.plan.replan.error.${error}`, { defaultValue: t("acquisition.plan.actionFailed") })}</span>
          <Button size="sm" variant="light" isDisabled={disabled} onPress={() => setRetry(value => value + 1)}>{t("common.action.retry")}</Button>
        </div>}
        {!hasSources && !detail.plan && <DraftPreparation detail={detail} busy={disabled} onAction={onAction} />}
        {refreshing && <div className="flex items-center gap-2 text-sm text-default-500"><Spinner size="sm" />{t("acquisition.preparation.checking")}</div>}
        {preview?.requiresRelationshipChoice && <Select label={t("acquisition.preparation.relationship")}
          description={t("acquisition.preparation.relationshipHint")} isDisabled={disabled || refreshing}
          selectedKeys={[relationship]} disallowEmptySelection
          dataSource={(["mirrors", "independent"] as const).map(value => ({ value, label: t(`acquisition.preparation.relationship.${value}`) }))}
          onSelectionChange={keys => { setEditedRevision(value => value ?? detail.task.planRevision); setRelationship(String(Array.from(keys)[0]) as Relationship); setSelected(undefined); setAllowUnavailableSourceIds([]); }} />}
        {hasSources && preview && <div className="flex flex-wrap items-center gap-1">
          <p className="text-xs text-default-500">{t("acquisition.preparation.priorityHint")}</p>
          <Button size="sm" variant="light" isDisabled={disabled || refreshing} onPress={() => createPortal(SourcePriorityModal, {
            onDestroyed: () => {
              if (editedRevision === undefined) setSelected(undefined);
              setRetry(value => value + 1);
            },
          })}>{t("sourceAvailability.priority.title")}</Button>
          <Button size="sm" variant="light" isDisabled={disabled || refreshing}
            onPress={() => setRetry(value => value + 1)}>{t("sourceAvailability.review.refresh")}</Button>
        </div>}
        {preview?.sharedDownload && editedRevision === undefined && !editing && (
          <Button size="sm" variant="flat" aria-haspopup="dialog" isDisabled={disabled || refreshing || !!error}
            onPress={() => {
              if (disabled || refreshing || error || !preview.sharedDownload) return;
              setSharedError(undefined);
              setSharedReview({ taskId: detail.task.id, revision: preview.expectedRevision, preview: preview.sharedDownload });
            }}>
            {t("acquisition.preparation.sharedDownload.open")}
          </Button>
        )}
        {hasSources && preview?.groups.map(group => <section key={group.id} className="space-y-2" aria-label={group.title ?? group.id}>
          {preview.groups.length > 1 && <h3 className="text-sm font-medium">{group.title || t("acquisition.preparation.sourceGroup")}</h3>}
          <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
            {group.sources.map(item => {
              const candidate = item.candidate;
              const chosen = selected?.includes(candidate.id) ?? group.selectedSourceIds.includes(candidate.id);
              const editableSource = currentSources().find(source => source.id === candidate.id) ?? sourceFromCandidate(candidate);
              const method = item.methods.find(value => value.id === item.selectedMethodId);
              const unavailable = item.availability?.status === "confirmedUnavailable";
              const sourceUrl = browserSourceUrl(candidate);
              const label = downloadSourceProviderLabel(item.resolvedSource?.providerId, t) ??
                downloadSourceProviderLabel(candidate.providerId, t) ?? sourceLabel(candidate);
              const manualOverride = chosen && unavailable && allowUnavailableSourceIds?.includes(candidate.id);
              const sourceRequirements = item.requirements.filter(requirement =>
                !(unavailable && item.canSelectManually && requirement.code === "SOURCE_CONFIRMED_UNAVAILABLE"));
              return <article key={candidate.id} className={`min-w-0 rounded-lg border p-3 ${unavailable ? "bg-default-100 text-default-500" : chosen ? "bg-primary-50/40" : "bg-default-50/30"} ${chosen ? "border-primary-400" : "border-default-200"}`}>
                <div className="flex items-start justify-between gap-2">
                  <Checkbox isSelected={chosen} isDisabled={disabled || refreshing || !item.canSelect && !item.canSelectManually}
                    onValueChange={checked => toggle(group, item, checked)}>
                    <span className="break-words font-medium">{label}</span>
                  </Checkbox>
                  <Button isIconOnly size="sm" variant="light" aria-label={t("acquisition.preparation.editSource", { source: label })}
                    isDisabled={disabled || refreshing} onPress={() => editSource(editableSource)}><EditOutlined aria-hidden /></Button>
                </div>
                <div className="my-2 flex flex-wrap items-center gap-1">
                  {chosen && <Chip size="sm" color="primary" variant="flat">{t("acquisition.plan.selected")}</Chip>}
                  {group.recommendedSourceIds.includes(candidate.id) && <Chip size="sm" variant="flat">{t("acquisition.preparation.recommended")}</Chip>}
                  <SourceHealthBadge health={item.availability && { ...item.availability, code: item.availability.reasonCode }} disabled={item.sourceDisabled} />
                </div>
                {candidate.title && <p className="mb-2 break-words text-sm">{candidate.title}</p>}
                {sourceUrl && <ExternalLink href={sourceUrl} className="mb-2 inline-flex max-w-full break-all text-xs"
                  aria-label={t("acquisition.preparation.openSource", { source: label })}>
                  {sourceUrl}
                </ExternalLink>}
                {unavailable && item.canSelectManually && <p role="status" className="mb-2 text-xs text-warning-700">
                  {t(manualOverride ? "acquisition.preparation.unavailableSelected" : "acquisition.preparation.unavailableHint")}
                </p>}
                {!!sourceRequirements.length && <div className="space-y-1 text-xs text-warning-700">
                  {sourceRequirements.map(requirement => <p key={requirement.code}>{t(`acquire.requirement.${requirement.code}`, { defaultValue: requirement.message })}</p>)}
                </div>}
                <SourceResolution source={item.resolvedSource} />
                {item.methods.filter(value => value.supported).length > 1 && <Select className="mt-2" size="sm"
                  label={t("acquire.method.label")} isDisabled={disabled || refreshing} selectedKeys={item.selectedMethodId ? [item.selectedMethodId] : []}
                  dataSource={item.methods.filter(value => value.supported).map(value => ({ value: value.id, label: downloadMethodLabel(value, t) }))}
                  onSelectionChange={keys => patchSource(candidate.id, { methodId: String(Array.from(keys)[0] ?? "") })} />}
                {method && <MethodPreparation methods={[method]} requirements={item.requirements}
                  accountId={candidate.accountId} isDisabled={disabled || refreshing} refreshKey={retry}
                  onAccountChange={accountId => patchSource(candidate.id, { accountId })}
                  onRefresh={() => setRetry(value => value + 1)} />}
              </article>;
            })}
          </div>
          {!preview.requiresRelationshipChoice && <Button size="sm" variant="light" startContent={<PlusOutlined />} isDisabled={disabled || refreshing}
            onPress={() => addSource(group.sources[0]?.candidate.groupId)}>{t("acquisition.draft.addSource")}</Button>}
        </section>)}
        {(!preview || preview.requiresRelationshipChoice || !hasSources) && <Button size="sm" variant="flat" startContent={<PlusOutlined />} isDisabled={disabled || refreshing}
          onPress={() => addSource()}>{t("acquisition.draft.addSource")}</Button>}
        {detail.plan?.parts.filter(part => part.processingSource).map(part => <section key={part.id} className="space-y-2">
          <h3 className="text-sm font-medium">{t("acquisition.preparation.processingData")}</h3>
          <SourceProcessingPlanPanel source={part.processingSource!} currentPlan={part.processing ?? null}
            appliedRevisionId={part.appliedProcessingRevisionId} frozen={false} isDisabled={disabled || editedRevision !== undefined}
            onApply={detail.task.phase === "ready" && editedRevision === undefined ? revision => onAction(async () => {
              const next = await planTaskApi.applyProcessingRevision(detail.task.id, detail.task.planRevision, part.id, part.processingSource!, revision);
              setPreviewRevision(undefined);
              setSelected(undefined);
              setAllowUnavailableSourceIds(undefined);
              setPreview(undefined);
              return next;
            }) : undefined} />
        </section>)}
        {requirements.map(requirement => <p key={requirement.code} role="status" className="text-sm text-warning-700">
          {t(`acquire.requirement.${requirement.code}`, { defaultValue: requirement.message })}
        </p>)}
      </div>
    </Modal>
    {sharedReview && <SharedDownloadReview preview={sharedReview.preview} busy={busy} saving={saving}
      stale={sharedReviewStale} error={sharedError} onClose={() => setSharedReview(undefined)}
      onConfirm={(keepPartId, useArchiveDirectories) => {
        if (sharedReviewStale) return;
        submitPreparation({
          expectedRevision: sharedReview.revision,
          sharedDownload: { keepPartId, useArchiveDirectories, expectedFingerprint: sharedReview.preview.fingerprint },
        }, setSharedError);
      }} />}
    {editing && <Modal visible size="2xl" title={t("acquisition.preparation.sourceDetails")} onClose={() => setEditing(undefined)}
      footer={<>
        <Button variant="light" onPress={() => setEditing(undefined)}>{t("common.action.cancel")}</Button>
        <Button color="primary" isDisabled={!editing.url.trim() || stale || editingRevision !== detail.task.planRevision} onPress={() => {
          if (stale || editingRevision !== detail.task.planRevision) return;
          const current = currentSources();
          const original = current.find(source => source.id === editing.id);
          const url = editing.url.trim();
          const reclassify = editing.kind !== "remote" && original && original.url !== url;
          const saved = { ...editing, url, ...(reclassify ? {
            kind: "auto", providerId: undefined, methodId: undefined, accountId: undefined,
          } : {}) };
          clearSourceOverride(saved.id);
          setEditedRevision(value => value ?? editingRevision);
          setSources(current.some(source => source.id === saved.id) ? current.map(source => source.id === saved.id ? saved : source) : [...current, saved]);
          setEditing(undefined);
        }}>{t("common.action.save")}</Button>
      </>}>
      <div className="space-y-3">
        {editingRevision !== detail.task.planRevision && <p role="alert" className="text-warning-700">{t("acquisition.plan.replan.error.PLAN_VERSION_CONFLICT")}</p>}
        <Input label={t("acquisition.preparation.url")} value={editing.url} onValueChange={url => setEditing({ ...editing, url })} />
        <Input label={t("acquisition.draft.accessCode")} value={editing.accessCode ?? ""} onValueChange={accessCode => setEditing({ ...editing, accessCode })} />
        <ArchivePasswordInput label={t("acquisition.sharedList.column.password")} value={editing.archivePassword ?? ""}
          onValueChange={archivePassword => setEditing({ ...editing, archivePassword })} />
      </div>
    </Modal>}
  </>;
}
