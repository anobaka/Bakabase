import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { LuFolderInput } from "react-icons/lu";
import { Button, Modal, Spinner } from "@/components/bakaui";
import { bytesToSize } from "@/components/utils";
import { planTaskApi } from "./api";
import type { PlanTaskDetail } from "./models";

type Preview = Awaited<ReturnType<typeof planTaskApi.registeredDownloads>>;
type Props = {
  taskId: number;
  revision: number;
  disabled: boolean;
  onApplied: (detail: PlanTaskDetail) => void;
};

function RegisteredDownloadPicker({ taskId, revision, onApplied, onClose }: Props & { onClose: () => void }) {
  const { t } = useTranslation();
  const [preview, setPreview] = useState<Preview>();
  const [selected, setSelected] = useState<string>();
  const [error, setError] = useState<"loadFailed" | "applyFailed">();
  const [saving, setSaving] = useState(false);
  const [refresh, setRefresh] = useState(0);
  const submitting = useRef(false);
  useEffect(() => {
    const controller = new AbortController();
    setPreview(undefined);
    setSelected(undefined);
    setError(undefined);
    void planTaskApi.registeredDownloads(taskId, revision, controller.signal).then((value) => {
      if (!controller.signal.aborted) setPreview(value);
    }).catch(() => {
      if (!controller.signal.aborted) setError("loadFailed");
    });
    return () => controller.abort();
  }, [taskId, revision, refresh]);
  const candidate = preview?.candidates.find(item => item.token === selected);
  const apply = async () => {
    if (!candidate || submitting.current || error) return;
    submitting.current = true;
    setSaving(true);
    try {
      const next = await planTaskApi.useRegisteredDownload(taskId, revision, candidate.token);
      onApplied(next);
      onClose();
    } catch {
      setSelected(undefined);
      setError("applyFailed");
    } finally {
      submitting.current = false;
      setSaving(false);
    }
  };
  return (
    <Modal visible size="2xl" title={t("acquisition.registered.title")} onClose={() => { if (!saving) onClose(); }}
      isDismissable={!saving} isKeyboardDismissDisabled={saving} footer={<>
        <Button variant="light" isDisabled={saving} onPress={onClose}>{t("common.action.close")}</Button>
        <Button color="primary" isDisabled={!candidate || !!error || saving} isLoading={saving}
          onPress={() => void apply()}>{t("acquisition.registered.use")}</Button>
      </>}>
      {error ? (
        <div role="alert" className="flex flex-wrap items-center gap-2">
          <span>{t(`acquisition.registered.${error}`)}</span>
          <Button size="sm" variant="flat" onPress={() => setRefresh(n => n + 1)}>{t("common.action.retry")}</Button>
        </div>
      ) : !preview ? <Spinner size="sm" aria-label={t("common.state.loading")} /> :
        !preview.candidates.length ? <p role="status">{t("acquisition.registered.empty")}</p> : (
          <>
            <p className="text-sm text-default-600">{t("acquisition.registered.hint")}</p>
            {preview.candidates.map(item => (
              <Button key={item.token} variant="flat" color={selected === item.token ? "primary" : "default"}
                aria-pressed={selected === item.token} isDisabled={saving} className="h-auto w-full justify-start py-2"
                onPress={() => setSelected(item.token)}>
                <span className="min-w-0 whitespace-normal text-start">
                  <span className="block font-medium">{t(`acquisition.timeline.stage.${item.stage}`)} · {item.sourceTitle || t("acquisition.registered.download", { id: item.downloadTaskId })}</span>
                  <span className="block text-xs">{t("acquisition.registered.details", {
                    id: item.downloadTaskId, run: item.runId, count: item.fileCount, size: bytesToSize(item.totalBytes),
                  })}</span>
                  <span className="block text-xs text-default-500 [overflow-wrap:anywhere]">{item.directory}</span>
                </span>
              </Button>
            ))}
          </>
        )}
    </Modal>
  );
}

export default function RegisteredDownloadAction(props: Props) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  return <div>
    <Button size="sm" variant="flat" startContent={<LuFolderInput />} aria-haspopup="dialog"
      isDisabled={props.disabled} onPress={() => setOpen(true)}>{t("acquisition.registered.title")}</Button>
    {open && <RegisteredDownloadPicker {...props} onClose={() => setOpen(false)} />}
  </div>;
}
