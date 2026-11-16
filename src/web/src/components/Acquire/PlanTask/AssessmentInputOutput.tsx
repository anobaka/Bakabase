import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Spinner } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import type { Attempt } from "./acquisitionAttempt";
import { copyTextToClipboard } from "@/core/clipboard";

type AssessmentIo = NonNullable<
  Awaited<ReturnType<typeof BApi.acquisition.getAcquisitionAssessmentIo>>["data"]
>;

export default function AssessmentInputOutput({
  taskId,
  attempt,
}: {
  taskId: number;
  attempt: Attempt;
}) {
  const { t } = useTranslation();
  const [data, setData] = useState<AssessmentIo>();
  const [error, setError] = useState(false);
  const [reload, setReload] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    setData(undefined);
    setError(false);
    void BApi.acquisition
      .getAcquisitionAssessmentIo(
        taskId,
        attempt.runId,
        { attemptId: attempt.id },
        {
          signal: controller.signal,
          sensitiveResponse: true,
          showErrorToast: false,
        },
      )
      .then((response) => {
        if (controller.signal.aborted) return;
        if (response.code || !response.data) throw new Error("ASSESSMENT_IO_UNAVAILABLE");
        setData(response.data);
      })
      .catch(() => {
        if (!controller.signal.aborted) setError(true);
      });
    return () => controller.abort();
  }, [taskId, attempt.id, attempt.runId, reload]);
  return (
    <section
      className="min-w-0 space-y-2 rounded-lg bg-default-50 p-3"
      aria-label={t("acquisition.records.io")}
    >
      <h4 className="text-sm font-medium">{t("acquisition.records.io")}</h4>
      {error ? (
        <div role="alert" className="flex flex-wrap items-center gap-1 text-sm text-warning-700">
          {t("acquisition.records.ioError")}
          <Button size="sm" variant="light" onPress={() => setReload((value) => value + 1)}>
            {t("common.action.retry")}
          </Button>
        </div>
      ) : !data ? (
        <Spinner size="sm" aria-label={t("common.state.loading")} />
      ) : (
        <>
          {data.calls.some(
            (call) => call.outputKind === "structure" || call.outputKind === "notRecorded",
          ) && (
            <p className="text-xs text-default-500">
              {t("acquisition.records.futureAudit", {
                page: t("menu.ai.configuration"),
                section: t("configuration.ai.quota.title"),
                setting: t("configuration.ai.quota.enableAuditContent"),
              })}
            </p>
          )}
          {!data.calls.length && (
            <p className="text-sm text-default-500">{t("acquisition.records.noCall")}</p>
          )}
          {data.calls.map((call, index) => (
            <article key={index} className="min-w-0 space-y-2">
              {call.iteration != null && (
                <h5 className="text-xs font-semibold">
                  {t("acquisition.records.iteration", { number: call.iteration })}
                </h5>
              )}
              <Preview
                title={t(
                  call.inputKind === "recordedFiles"
                    ? "acquisition.records.recordedFiles"
                    : "acquisition.records.input",
                )}
                text={call.inputPreview}
                truncated={call.inputTruncated}
                note={
                  call.inputKind === "recordedFiles"
                    ? t("acquisition.records.recordedFilesHint")
                    : undefined
                }
                empty={t("acquisition.records.missingInput")}
                document={call.inputTextAvailable && call.callId ? { taskId, attempt, callId: call.callId, direction: "input" } : undefined}
                availability={call.inputAvailability}
                redacted={call.inputRedacted}
              />
              {call.validationError && (
                <div role="alert" className="min-w-0 rounded-md bg-warning-50 p-2 text-sm text-warning-700">
                  <span className="font-medium">{t("acquisition.records.validationError")}</span>
                  <pre className="mt-1 whitespace-pre-wrap break-all text-xs">{call.validationError}</pre>
                </div>
              )}
              <Preview
                title={t("acquisition.records.output")}
                text={call.outputPreview}
                truncated={call.outputTruncated}
                note={
                  call.outputKind === "structure"
                    ? t("acquisition.records.structureHint")
                    : undefined
                }
                empty={t(
                  call.outputKind === "noResponse"
                    ? "acquisition.records.noResponse"
                    : call.outputKind === "empty"
                      ? "acquisition.records.emptyResponse"
                      : "acquisition.records.missingOutput",
                )}
                document={call.outputTextAvailable && call.callId ? { taskId, attempt, callId: call.callId, direction: "output" } : undefined}
                availability={call.outputAvailability}
                redacted={call.outputRedacted}
              />
            </article>
          ))}
        </>
      )}
    </section>
  );
}

function Preview({
  title,
  text,
  note,
  empty,
  truncated,
  document,
  availability,
  redacted,
}: {
  title: string;
  text?: string | null;
  note?: string;
  empty: string;
  truncated: boolean;
  document?: { taskId: number; attempt: Attempt; callId: string; direction: "input" | "output" };
  availability?: string;
  redacted?: boolean;
}) {
  const { t } = useTranslation();
  const [full, setFull] = useState<string>();
  const [next, setNext] = useState<number | null>(0);
  const [contentId, setContentId] = useState<string>();
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(false);
  const request = useRef<AbortController>();
  useEffect(() => () => request.current?.abort(), []);
  const load = async () => {
    if (!document || loading || next == null) return;
    const controller = new AbortController();
    request.current?.abort();
    request.current = controller;
    setLoading(true);
    setError(false);
    try {
      const response = await BApi.acquisition.getAcquisitionAssessmentText(document.taskId, document.attempt.runId, {
        attemptId: document.attempt.id, callId: document.callId, direction: document.direction,
        offset: next, length: 32768, contentId,
      }, { signal: controller.signal, sensitiveResponse: true, showErrorToast: false });
      if (controller.signal.aborted) return;
      if (response.code || !response.data) throw new Error("ASSESSMENT_TEXT_UNAVAILABLE");
      setFull((previous) => (previous ?? "") + response.data!.text);
      setNext(response.data.nextOffset ?? null);
      setContentId(response.data.contentId);
    } catch {
      if (!controller.signal.aborted) setError(true);
    } finally {
      if (!controller.signal.aborted) setLoading(false);
    }
  };
  const shown = full ?? text;
  return (
    <div className="min-w-0 space-y-1">
      <div className="flex flex-wrap items-center gap-2">
        <h6 className="text-xs font-medium">{title}</h6>
        {availability && <span className="text-xs text-default-500">{t(`acquisition.records.text.${availability}`)}</span>}
        {redacted && <span className="text-xs text-default-500">{t("acquisition.records.text.redacted")}</span>}
        {document && next != null && <Button size="sm" variant="light" isLoading={loading} onPress={load}>
          {t(full === undefined ? "acquisition.records.text.open" : "acquisition.records.text.more")}
        </Button>}
        {shown && <Button size="sm" variant="light" onPress={() => copyTextToClipboard(shown)}>
          {t("acquisition.records.text.copyVisible")}
        </Button>}
      </div>
      {note && <p className="text-xs text-default-500">{note}</p>}
      {shown ? (
        <pre className="max-h-[36dvh] overflow-auto whitespace-pre-wrap break-all rounded-md bg-content1 p-2 text-xs select-text">
          {shown}
        </pre>
      ) : (
        <p className="text-sm text-default-500">{empty}</p>
      )}
      {error && <p role="alert" className="text-xs text-warning-700">{t("acquisition.records.ioError")}</p>}
      {truncated && full === undefined && (
        <p className="text-xs text-default-500">{t("acquisition.records.previewTruncated")}</p>
      )}
    </div>
  );
}
