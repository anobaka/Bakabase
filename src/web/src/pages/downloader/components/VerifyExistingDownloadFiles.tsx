import type { DestroyableProps } from "@/components/bakaui/types";
import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { FiCheckCircle } from "react-icons/fi";
import { Button, Modal, Spinner, Tooltip } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { downloadProblemSummary } from "@/components/CloudDownload/problemLabels";
import { formatBtBytes } from "@/pages/bt-downloads/format";
import BApi from "@/sdk/BApi";
import { isFileVerificationStage, type CenterTask } from "../downloadCenter";

type Preview = NonNullable<
  Awaited<ReturnType<typeof BApi.downloadCenter.previewDownloadCenterExistingFiles>>["data"]
>;

type VerificationProblem = NonNullable<Preview["verificationProblem"]>;

function PreviousVerification({
  failure,
  announce,
}: {
  failure: VerificationProblem;
  announce: boolean;
}) {
  const { t, i18n } = useTranslation();
  const locale = i18n?.language === "cn" ? "zh-CN" : i18n?.language || undefined;
  const { checkedAt, reasonCode, location } = failure;
  const checked = checkedAt ? new Date(checkedAt) : undefined;
  const mismatch = location?.checksumMismatch;
  return (
    <section
      role={announce ? "alert" : undefined}
      className="space-y-1 rounded-md bg-warning-50 p-3 text-sm"
    >
      <div className="flex flex-wrap items-baseline gap-x-2">
        <h3 className="font-medium">{t("downloadCenter.existingFiles.lastVerification")}</h3>
        {checked && Number.isFinite(checked.getTime()) && (
          <time dateTime={checkedAt} className="text-default-600">
            {checked.toLocaleString(locale)}
          </time>
        )}
      </div>
      {reasonCode && <p>{downloadProblemSummary(reasonCode, t) || reasonCode}</p>}
      {location && (
        <p>
          {t("downloadCenter.existingFiles.filePosition", {
            fileNumber: location.fileNumber,
            fileCount: location.fileCount,
          })}
        </p>
      )}
      {mismatch && (
        <p>
          {t("downloadCenter.existingFiles.blockMismatch", {
            firstBlockNumber: mismatch.firstBlockNumber,
            blockCount: mismatch.blockCount,
            mismatchedBlockCount: mismatch.mismatchedBlockCount,
            bytes: mismatch.firstBlockBytes.toLocaleString(locale),
          })}
        </p>
      )}
    </section>
  );
}

export function VerifyExistingFilesButton({ task }: { task: CenterTask }) {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const label = t<string>("downloadCenter.existingFiles.action");
  return (
    <Tooltip content={label}>
      <Button
        isIconOnly
        aria-label={label}
        color="primary"
        size="sm"
        variant="flat"
        onPress={() => createPortal(VerifyExistingFilesModal, { task })}
      >
        <FiCheckCircle aria-hidden />
      </Button>
    </Tooltip>
  );
}

export function VerifyExistingFilesModal({
  task,
  onDestroyed,
}: DestroyableProps & { task: CenterTask }) {
  const { t } = useTranslation();
  const [preview, setPreview] = useState<Preview>();
  const [review, setReview] = useState(0);
  const [problem, setProblem] = useState<string>();
  const [verificationProblem, setVerificationProblem] = useState<VerificationProblem>();
  const [submitting, setSubmitting] = useState(false);
  const [stage, setStage] = useState<string>();
  const [resultId, setResultId] = useState<number>();
  const [visible, setVisible] = useState(true);
  const claimed = useRef(false);
  const closeButton = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    // Submitting removes the focused confirmation button when its result arrives.
    if (
      visible &&
      claimed.current &&
      !submitting &&
      (problem || resultId != null) &&
      document.activeElement === document.body
    )
      closeButton.current?.focus({ preventScroll: true });
  }, [visible, submitting, problem, resultId]);
  useEffect(() => {
    let active = true;
    async function load() {
      try {
        const reply = await BApi.downloadCenter.previewDownloadCenterExistingFiles(task.id, {
          showErrorToast: false,
        });
        if (!active) return;
        if (
          reply.code ||
          !reply.data ||
          task.id !== `site:${reply.data.siteTaskId}` ||
          (task.siteTaskId != null && reply.data.siteTaskId !== task.siteTaskId)
        )
          throw new Error(reply.message || "downloadCenter.existingFiles.previewFailed");
        setPreview(reply.data);
        setVerificationProblem(reply.data.verificationProblem ?? undefined);
      } catch (error) {
        if (active)
          setProblem(
            error instanceof Error && error.message
              ? error.message
              : "downloadCenter.existingFiles.previewFailed",
          );
      }
    }
    void load();
    return () => {
      active = false;
    };
  }, [task.id, task.siteTaskId, review]);

  useEffect(() => {
    if (!submitting) return;
    let active = true;
    let reading = false;
    const update = async () => {
      if (reading) return;
      reading = true;
      try {
        const reply = await BApi.downloadCenter.getDownloadCenterTask(task.id, {
          showErrorToast: false,
        });
        const observed = reply.data?.stage;
        if (active && !reply.code)
          setStage(isFileVerificationStage(observed) ? observed : undefined);
      } catch {
        // The verification request itself reports the outcome. Missing progress has no percentage.
        if (active) setStage(undefined);
      } finally {
        reading = false;
      }
    };
    void update();
    const interval = setInterval(() => {
      if (!document.hidden) void update();
    }, 3000);
    return () => {
      active = false;
      clearInterval(interval);
    };
  }, [submitting, task.id]);

  const token =
    preview?.canVerify && !problem && resultId == null ? preview.previewToken : undefined;
  async function confirm() {
    if (!token || claimed.current) return;
    claimed.current = true;
    setSubmitting(true);
    try {
      const reply = await BApi.downloadCenter.verifyDownloadCenterExistingFiles(
        task.id,
        { previewToken: token },
        { showErrorToast: false },
      );
      const receipt = reply.data;
      if (
        reply.code &&
        receipt?.siteTaskId === preview?.siteTaskId &&
        receipt?.verificationProblem
      ) {
        setVerificationProblem(receipt.verificationProblem);
        setProblem(
          receipt.verificationProblem.reasonCode || "downloadCenter.existingFiles.verifyFailed",
        );
        return;
      }
      if (
        reply.code ||
        !receipt ||
        receipt.siteTaskId !== preview?.siteTaskId ||
        receipt.downloadResultId == null ||
        receipt.downloadResultId <= 0
      )
        throw new Error(reply.message || t("downloadCenter.existingFiles.verifyFailed"));
      setVerificationProblem(undefined);
      setResultId(receipt.downloadResultId);
    } catch (error) {
      setProblem(
        error instanceof Error ? error.message : t("downloadCenter.existingFiles.verifyFailed"),
      );
    } finally {
      setSubmitting(false);
    }
  }
  const reason = resultId == null ? problem || preview?.reasonCode : undefined;
  function refreshPreview() {
    setPreview(undefined);
    setProblem(undefined);
    setStage(undefined);
    claimed.current = false;
    setReview((current) => current + 1);
  }
  return (
    <Modal
      visible={visible}
      size="lg"
      title={t("downloadCenter.existingFiles.action")}
      onClose={() => setVisible(false)}
      onDestroyed={onDestroyed}
      footer={
        <>
          <Button ref={closeButton} variant="light" onPress={() => setVisible(false)}>
            {t("common.action.close")}
          </Button>
          {!submitting && (problem || (preview && !token && resultId == null)) && (
            <Button variant="bordered" onPress={refreshPreview}>
              {t("downloadCenter.existingFiles.checkAgain")}
            </Button>
          )}
          {token && (
            <Button
              color="primary"
              isDisabled={submitting}
              isLoading={submitting}
              onPress={() => void confirm()}
            >
              {t(
                verificationProblem
                  ? "downloadCenter.existingFiles.verifyAgain"
                  : "downloadCenter.existingFiles.confirm",
              )}
            </Button>
          )}
        </>
      }
    >
      <p className="break-words font-medium">{task.name || task.id}</p>
      {preview && (
        <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
          <dt>{t("downloadCenter.existingFiles.directory")}</dt>
          <dd className="break-all">{preview.outputDirectory || "—"}</dd>
          <dt>{t("downloadCenter.existingFiles.files")}</dt>
          <dd>{preview.fileCount ?? "—"}</dd>
          <dt>{t("downloadCenter.existingFiles.size")}</dt>
          <dd>{formatBtBytes(preview.totalBytes)}</dd>
        </dl>
      )}
      {verificationProblem && (
        <PreviousVerification
          failure={verificationProblem}
          announce={!!problem && reason === verificationProblem.reasonCode}
        />
      )}
      {reason ? (
        reason !== verificationProblem?.reasonCode && (
          <p role="alert" className="text-sm text-danger">
            {reason.startsWith("downloadCenter.existingFiles.")
              ? t(reason)
              : downloadProblemSummary(reason, t) || reason}
          </p>
        )
      ) : resultId != null ? (
        <p role="status" className="text-sm text-success">
          {t("downloadCenter.existingFiles.registered", { id: resultId })}
        </p>
      ) : !preview || submitting ? (
        <div role="status" className="flex items-center gap-2">
          <Spinner size="sm" />
          {t(
            submitting
              ? stage
                ? `downloadCenter.stage.${stage}`
                : "downloadCenter.existingFiles.verifying"
              : "downloadCenter.existingFiles.previewing",
          )}
        </div>
      ) : (
        <p className="text-sm text-default-600">
          {t(
            token
              ? "downloadCenter.existingFiles.description"
              : "downloadCenter.existingFiles.unavailable",
          )}
        </p>
      )}
    </Modal>
  );
}
