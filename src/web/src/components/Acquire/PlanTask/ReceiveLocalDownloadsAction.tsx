import type { PlanTaskDetail } from "./models";

import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { LuFolderInput } from "react-icons/lu";

import { planTaskApi } from "./api";

import { Button, Modal, Spinner } from "@/components/bakaui";
import FilePathValue from "@/components/FilePathValue";
import { bytesToSize } from "@/components/utils";

type Preview = Awaited<ReturnType<typeof planTaskApi.previewLocalDownloads>>;
type Props = {
  taskId: number;
  revision: number;
  disabled: boolean;
  onSaved: (detail: PlanTaskDetail) => void;
};

function failureKey(cause: unknown, fallback: string) {
  const code = cause instanceof Error ? cause.message.split(/[:\s]/, 1)[0] : "";

  if (code === "PLAN_VERSION_CONFLICT") return "acquisition.receiveLocal.stale";
  if (/^LOCAL_DOWNLOAD_[A-Z_]+$/.test(code)) return `acquisition.receiveLocal.reason.${code}`;

  return `acquisition.receiveLocal.${fallback}`;
}

function ReceiveLocalDownloadsModal({
  taskId,
  revision,
  onSaved,
  onClose,
}: Props & { onClose: () => void }) {
  const { t } = useTranslation();
  const [request, setRequest] = useState({ revision });
  const [preview, setPreview] = useState<Preview>();
  const [error, setError] = useState<string>();
  const [working, setWorking] = useState(false);
  const pending = useRef(false);

  useEffect(() => {
    const controller = new AbortController();

    setPreview(undefined);
    setError(undefined);
    void planTaskApi
      .previewLocalDownloads(taskId, request.revision, controller.signal)
      .then((value) => {
        if (!controller.signal.aborted) setPreview(value);
      })
      .catch((cause) => {
        if (!controller.signal.aborted) setError(failureKey(cause, "loadFailed"));
      });

    return () => controller.abort();
  }, [taskId, request]);

  const stale = preview != null && revision > preview.revision;
  const reload = () => {
    setRequest({ revision: Math.max(revision, preview?.revision ?? revision) });
    setPreview(undefined);
    setError(undefined);
  };
  const act = async (kind: "prepare" | "apply") => {
    if (!preview || error || stale || pending.current) return;
    if (kind === "prepare" ? !preview.canPrepare : !preview.canApply || !preview.token) return;
    pending.current = true;
    setWorking(true);
    try {
      if (kind === "prepare") {
        setPreview(await planTaskApi.prepareLocalDownloads(taskId, preview.revision));
      } else if (preview.token) {
        const next = await planTaskApi.applyLocalDownloads(taskId, preview.revision, preview.token);

        onSaved(next);
        onClose();
      }
    } catch (cause) {
      setError(failureKey(cause, kind === "prepare" ? "prepareFailed" : "applyFailed"));
    } finally {
      pending.current = false;
      setWorking(false);
    }
  };

  const title = t("acquisition.receiveLocal.title");
  const reason = (code: string) =>
    t(`acquisition.receiveLocal.reason.${code}`, {
      defaultValue: t("acquisition.receiveLocal.blocked"),
    });

  return (
    <Modal
      visible
      aria-label={title}
      footer={
        <>
          <Button isDisabled={working} variant="light" onPress={onClose}>
            {t("common.action.close")}
          </Button>
          {(preview || error) && (
            <Button isDisabled={working} variant="flat" onPress={reload}>
              {t("acquisition.receiveLocal.refresh")}
            </Button>
          )}
          {preview?.canPrepare && !error && !stale && (
            <Button color="primary" isLoading={working} onPress={() => void act("prepare")}>
              {t("acquisition.receiveLocal.prepare")}
            </Button>
          )}
          {preview?.canApply && preview.token && !error && !stale && (
            <Button color="primary" isLoading={working} onPress={() => void act("apply")}>
              {t("acquisition.receiveLocal.confirm")}
            </Button>
          )}
        </>
      }
      hideCloseButton={working}
      isDismissable={!working}
      isKeyboardDismissDisabled={working}
      size="2xl"
      title={title}
      onClose={() => {
        if (!pending.current) onClose();
      }}
    >
      {error ? (
        <p role="alert">{t(error, { defaultValue: t("acquisition.receiveLocal.blocked") })}</p>
      ) : stale ? (
        <p role="alert">{t("acquisition.receiveLocal.stale")}</p>
      ) : !preview ? (
        <div className="flex items-center gap-2 text-sm" role="status">
          <Spinner aria-label={t("common.state.loading")} size="sm" />
          {t("acquisition.receiveLocal.checking")}
        </div>
      ) : (
        <>
          {working && preview.canApply && (
            <p className="text-sm" role="status">
              {t("acquisition.receiveLocal.verifying")}
            </p>
          )}
          {(preview.canPrepare ||
            preview.canApply ||
            preview.parts.some((part) => part.reasonCode === "LOCAL_DOWNLOAD_FILES_MISSING")) && (
            <p className="text-sm">
              {t(
                preview.canPrepare
                  ? "acquisition.receiveLocal.prepareHint"
                  : preview.canApply
                    ? "acquisition.receiveLocal.reviewHint"
                    : "acquisition.receiveLocal.placeHint",
              )}
            </p>
          )}
          {preview.reasonCode &&
            !preview.parts.some((part) => part.reasonCode === preview.reasonCode) && (
              <p className="text-sm text-warning-700" role="status">
                {reason(preview.reasonCode)}
              </p>
            )}
          {preview.parts.map((part, index) => (
            <section key={part.partId} className="min-w-0 space-y-2 rounded-lg bg-default-50 p-3">
              <h4 className="font-medium">
                {part.title || t("acquisition.plan.part", { index: index + 1 })}
              </h4>
              {part.directory && <FilePathValue compact path={part.directory} />}
              {part.fileCount > 0 && (
                <span className="block text-sm">
                  {t("acquisition.receiveLocal.summary", {
                    count: part.fileCount,
                    size: bytesToSize(part.bytes),
                  })}
                </span>
              )}
              {part.reasonCode && (
                <p className="text-sm text-warning-700" role="status">
                  {reason(part.reasonCode)}
                </p>
              )}
              {part.files.length > 0 && (
                <ul className="max-h-48 overflow-auto text-sm">
                  {part.files.map((file) => (
                    <li key={file.relativePath} className="flex justify-between gap-3">
                      <span className="min-w-0 [overflow-wrap:anywhere]">{file.relativePath}</span>
                      <span className="shrink-0 tabular-nums">{bytesToSize(file.bytes)}</span>
                    </li>
                  ))}
                </ul>
              )}
              {part.filesTruncated && (
                <p className="text-xs text-default-500">
                  {t("acquisition.receiveLocal.truncated", {
                    shown: part.files.length,
                    total: part.fileCount,
                  })}
                </p>
              )}
            </section>
          ))}
        </>
      )}
    </Modal>
  );
}

export default function ReceiveLocalDownloadsAction(props: Props) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);

  return (
    <>
      <Button
        aria-haspopup="dialog"
        isDisabled={props.disabled}
        size="sm"
        startContent={<LuFolderInput />}
        variant="flat"
        onPress={() => setOpen(true)}
      >
        {t("acquisition.receiveLocal.title")}
      </Button>
      {open && <ReceiveLocalDownloadsModal {...props} onClose={() => setOpen(false)} />}
    </>
  );
}
