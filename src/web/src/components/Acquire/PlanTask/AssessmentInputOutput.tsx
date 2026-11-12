import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Spinner } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import type { Attempt } from "./acquisitionAttempt";

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
}: {
  title: string;
  text?: string | null;
  note?: string;
  empty: string;
  truncated: boolean;
}) {
  const { t } = useTranslation();
  return (
    <div className="min-w-0 space-y-1">
      <h6 className="text-xs font-medium">{title}</h6>
      {note && <p className="text-xs text-default-500">{note}</p>}
      {text ? (
        <pre className="max-h-[36dvh] overflow-auto whitespace-pre-wrap break-all rounded-md bg-content1 p-2 text-xs select-text">
          {text}
        </pre>
      ) : (
        <p className="text-sm text-default-500">{empty}</p>
      )}
      {truncated && (
        <p className="text-xs text-default-500">{t("acquisition.records.previewTruncated")}</p>
      )}
    </div>
  );
}
