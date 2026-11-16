import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Checkbox, Modal, Spinner } from "@/components/bakaui";
import FileSystemSelectorButton from "@/components/FileSystemSelector/components/Button";
import BApi from "@/sdk/BApi";
import type { AcquisitionStage } from "./AcquisitionExecutionTimeline";

const stages: AcquisitionStage[] = ["download", "processing", "delivery"];
type Snapshot = NonNullable<Awaited<ReturnType<typeof BApi.acquisition.getAcquisitionStageDirectories>>["data"]>;
type Input = Parameters<typeof BApi.acquisition.applyAcquisitionStageDirectoryChange>[1];
type Choice = { directory: string; moveFiles: boolean };
type Draft = Record<string, Record<AcquisitionStage, Choice>>;

export default function TaskDirectoriesModal({ taskId, onClose, onChanged }: {
  taskId: number; onClose: () => void; onChanged: () => void;
}) {
  const { t } = useTranslation();
  const [snapshot, setSnapshot] = useState<Snapshot>();
  const [draft, setDraft] = useState<Draft>({});
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const [reload, setReload] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    setSnapshot(undefined);
    void BApi.acquisition.getAcquisitionStageDirectories(taskId, { signal: controller.signal, sensitiveResponse: true, showErrorToast: false })
      .then(response => {
        if (controller.signal.aborted) return;
        if (response.code || !response.data) throw new Error(response.message || "failed");
        const value = response.data;
        setSnapshot(value);
        setSelected(new Set());
        setDraft(Object.fromEntries(value.parts.map(part => [part.partId, Object.fromEntries(stages.map(stage => {
          const pending = value.pendingChange?.parts.find(item => item.partId === part.partId)?.[stage];
          return [stage, { directory: pending?.directory ?? part[`${stage}Directory`] ?? "", moveFiles: pending?.moveFiles ?? true }];
        })) as Record<AcquisitionStage, Choice>])));
      })
      .catch(cause => { if (!controller.signal.aborted) setError(cause instanceof Error ? cause.message : "failed"); });
    return () => controller.abort();
  }, [taskId, reload]);

  const changes: Input["parts"] = snapshot?.parts.map(part => ({ partId: part.partId, ...Object.fromEntries(stages.map(stage => [stage,
    selected.has(JSON.stringify([part.partId, stage])) || draft[part.partId]?.[stage].directory !== (part[`${stage}Directory`] ?? "")
      ? draft[part.partId]?.[stage] : null])) })) ?? [];
  const changed = changes.some(part => stages.some(stage => !!part[stage]));
  const update = (partId: string, stage: AcquisitionStage, value: Partial<Choice>) => setDraft(current => ({
    ...current, [partId]: { ...current[partId], [stage]: { ...current[partId][stage], ...value } },
  }));
  const apply = async () => {
    if (!snapshot || busy) return;
    setBusy(true); setError(undefined);
    try {
      const input: Input = snapshot.pendingChange ?? { expectedRevision: snapshot.expectedRevision, parts: changes };
      const response = await BApi.acquisition.applyAcquisitionStageDirectoryChange(taskId, input,
        { sensitiveResponse: true, showErrorToast: false });
      if (response.code || !response.data) throw new Error(response.message || "failed");
      onChanged(); onClose();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "failed");
      // A partial, interrupted move must be resumed with the durable original intent.
      setReload(value => value + 1);
    } finally { setBusy(false); }
  };
  return <Modal visible size="4xl" scrollBehavior="inside" title={t("acquisition.directories.title")}
    onClose={() => { if (!busy) onClose(); }} isDismissable={!busy} isKeyboardDismissDisabled={busy} hideCloseButton={busy}
    footer={<div className="flex justify-end gap-2">
      <Button variant="light" isDisabled={busy} onPress={onClose}>{t("common.action.cancel")}</Button>
      <Button color="primary" isLoading={busy} isDisabled={!snapshot || (!snapshot.canChange && !snapshot.pendingChange) || (!changed && !snapshot.pendingChange)} onPress={apply}>
        {t(snapshot?.pendingChange ? "acquisition.directories.resume" : "common.action.apply")}
      </Button>
    </div>}>
    {!snapshot && !error && <Spinner aria-label={t("common.state.loading")} />}
    <p className="text-sm text-default-500">{t("acquisition.directories.hint")}</p>
    {snapshot?.pendingChange && <p role="status" className="text-sm text-warning-700">{t("acquisition.directories.pending")}</p>}
    {snapshot?.blockers.map(code => <p key={code} role="status" className="text-sm text-warning-700">{t(`acquisition.directories.error.${code}`, { defaultValue: t("acquisition.directories.blocked") })}</p>)}
    {snapshot?.parts.map(part => <section key={part.partId} className="min-w-0 space-y-3">
      {snapshot.parts.length > 1 && <h4 className="text-sm font-semibold">{part.title || part.partId}</h4>}
      {stages.map(stage => <div key={stage} className="grid min-w-0 gap-2 rounded-lg bg-default-50 p-3 sm:grid-cols-[5rem_1fr]">
        <h5 className="text-sm font-medium">{t(`acquisition.timeline.stage.${stage}`)}</h5>
        <div className="min-w-0 space-y-2">
          <FileSystemSelectorButton wrapPath key={draft[part.partId]?.[stage].directory}
            aria-label={t("acquisition.directories.select", { stage: t(`acquisition.timeline.stage.${stage}`) })}
            className="h-auto min-h-8 max-w-full whitespace-normal break-all text-left" isDisabled={busy || !snapshot.canChange || !!snapshot.pendingChange}
            fileSystemSelectorProps={{ targetType: "folder", multiple: false,
              defaultSelectedPath: draft[part.partId]?.[stage].directory || undefined,
              startPath: draft[part.partId]?.[stage].directory || undefined,
              onSelected: entry => { if (entry.path) {
                update(part.partId, stage, { directory: entry.path });
                setSelected(current => new Set([...current, JSON.stringify([part.partId, stage])]));
              } } }}>
            {t("acquisition.directories.select", { stage: t(`acquisition.timeline.stage.${stage}`) })}
          </FileSystemSelectorButton>
          <Checkbox size="sm" isSelected={draft[part.partId]?.[stage].moveFiles ?? true}
            isDisabled={busy || !snapshot.canChange || !!snapshot.pendingChange}
            onValueChange={moveFiles => update(part.partId, stage, { moveFiles })}>
            {t("acquisition.directories.moveFiles")}
          </Checkbox>
        </div>
      </div>)}
    </section>)}
    {busy && <p role="status" className="text-sm text-default-500">{t("acquisition.directories.moving")}</p>}
    {error && <div role="alert" className="flex items-center gap-2 text-sm text-danger">
      {t(`acquisition.directories.error.${error}`, { defaultValue: t("acquisition.plan.actionFailed") })}
      <Button size="sm" variant="light" onPress={() => { setError(undefined); setReload(value => value + 1); }}>{t("common.action.retry")}</Button>
    </div>}
  </Modal>;
}
