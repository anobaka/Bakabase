import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Accordion, AccordionItem, Button, Chip, Select, Spinner } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import type { BakabaseServiceComponentsCloudDownloadsRcloneRcloneConfigurationQuestionViewModel as Question } from "@/sdk/Api";
import { presentRcloneAnswerOptions } from "./rcloneQuestionPresentation";

type Choices = NonNullable<
  Awaited<ReturnType<typeof BApi.rclone.getRcloneAuthorizationDrives>>["data"]
>;
type Preview = NonNullable<
  Awaited<ReturnType<typeof BApi.rclone.previewRcloneAuthorizationDrive>>["data"]
>;
const requestOptions = { showErrorToast: false, sensitiveResponse: true };

/** Lists only the current authorization question's candidates; preview never submits a choice. */
export default function RcloneDriveSelection({
  sessionId,
  question,
  answer,
  onAnswer,
  isDisabled,
  allowAutomaticSelection = true,
  problemLabel,
}: {
  sessionId: string;
  question: Question;
  answer: string;
  onAnswer: (value: string) => void;
  isDisabled: boolean;
  allowAutomaticSelection?: boolean;
  problemLabel: (message: string) => string;
}) {
  const { t } = useTranslation();
  const [choices, setChoices] = useState<Choices>();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();
  const [retry, setRetry] = useState(0);
  const [advancedOpen, setAdvancedOpen] = useState(false);
  const [preview, setPreview] = useState<Preview>();
  const [previewError, setPreviewError] = useState<{ identity: string; message: string }>();
  const [previewing, setPreviewing] = useState<string>();
  const selectionTouched = useRef(false);
  const choiceRequest = useRef(0);
  const previewRequest = useRef(0);
  const identity = JSON.stringify([sessionId, question.id, answer]);
  const currentIdentity = useRef(identity);
  currentIdentity.current = identity;
  const currentPreview =
    preview?.questionId === question.id && preview.driveId === answer ? preview : undefined;
  const currentPreviewError =
    previewError?.identity === identity ? previewError.message : undefined;
  const currentlyPreviewing = previewing === identity;
  const options = presentRcloneAnswerOptions(question, t);
  const verifiedPrimary = (candidate: Choices["candidates"][number]) =>
    candidate.isPrimary && candidate.accessStatus === "accessible";
  const candidateFor = (value: string) => choices?.candidates.find((c) => c.driveId === value);
  const primaryOptions = options
    .filter((option) => {
      const candidate = candidateFor(option.value);
      return candidate && verifiedPrimary(candidate);
    })
    .map((option) => ({
      ...option,
      label: t("rclone.authorization.myOneDrive"),
      description: candidateFor(option.value)?.name || option.label,
    }));
  const advancedOptions = options.filter(
    (option) => !primaryOptions.some((primary) => primary.value === option.value),
  );
  const selected = candidateFor(answer);
  const selectedAccess = currentPreview
    ? "accessible"
    : currentPreviewError
      ? "unavailable"
      : (selected?.accessStatus ?? "unchecked");
  const selectedLabel =
    primaryOptions.find((option) => option.value === answer)?.label ??
    options.find((option) => option.value === answer)?.label;
  const choose = (value: string) => {
    selectionTouched.current = true;
    onAnswer(value);
  };
  useEffect(() => {
    const request = ++choiceRequest.current;
    setLoading(true);
    setError(undefined);
    setChoices(undefined);
    void BApi.rclone
      .getRcloneAuthorizationDrives(sessionId, { questionId: question.id }, requestOptions)
      .then((response) => {
        if (request !== choiceRequest.current) return;
        if (response.code || !response.data || response.data.questionId !== question.id)
          throw new Error(response.message || "RCLONE_DRIVE_CHOICES_FAILED");
        setChoices(response.data);
      })
      .catch((failure: unknown) => {
        if (request === choiceRequest.current)
          setError(failure instanceof Error ? failure.message : "RCLONE_DRIVE_CHOICES_FAILED");
      })
      .finally(() => {
        if (request === choiceRequest.current) setLoading(false);
      });
    return () => {
      choiceRequest.current++;
    };
  }, [sessionId, question.id, retry]);
  useEffect(() => {
    if (
      allowAutomaticSelection &&
      !selectionTouched.current &&
      !answer &&
      primaryOptions.length === 1
    )
      onAnswer(primaryOptions[0].value);
  }, [choices, answer, allowAutomaticSelection]);
  useEffect(() => {
    previewRequest.current++;
    setPreview(undefined);
    setPreviewError(undefined);
    setPreviewing(undefined);
    return () => {
      previewRequest.current++;
    };
  }, [identity]);
  const previewRoot = async () => {
    if (!answer || currentlyPreviewing) return;
    const request = ++previewRequest.current;
    const requestedIdentity = identity;
    const active = () =>
      request === previewRequest.current && requestedIdentity === currentIdentity.current;
    setPreviewing(identity);
    setPreview(undefined);
    setPreviewError(undefined);
    try {
      const response = await BApi.rclone.previewRcloneAuthorizationDrive(
        sessionId,
        { questionId: question.id, driveId: answer },
        requestOptions,
      );
      if (!active()) return;
      if (
        response.code ||
        !response.data ||
        response.data.questionId !== question.id ||
        response.data.driveId !== answer
      )
        throw new Error(response.message || "RCLONE_DRIVE_PREVIEW_FAILED");
      setPreview(response.data);
      setChoices(
        (current) =>
          current && {
            ...current,
            candidates: current.candidates.map((candidate) =>
              candidate.driveId === answer
                ? { ...candidate, accessStatus: "accessible", problem: undefined }
                : candidate,
            ),
          },
      );
    } catch (failure) {
      if (active()) {
        setPreviewError({
          identity: requestedIdentity,
          message: failure instanceof Error ? failure.message : "RCLONE_DRIVE_PREVIEW_FAILED",
        });
        setChoices(
          (current) =>
            current && {
              ...current,
              candidates: current.candidates.map((candidate) =>
                candidate.driveId === answer
                  ? { ...candidate, accessStatus: "unavailable" }
                  : candidate,
              ),
            },
        );
      }
    } finally {
      if (active()) setPreviewing(undefined);
    }
  };
  return (
    <div className="space-y-3">
      {loading ? (
        <div className="flex items-center gap-2 text-xs text-default-500" role="status">
          <Spinner size="sm" />
          {t("rclone.authorization.checkingMyDrive")}
        </div>
      ) : primaryOptions.length ? (
        <Select
          label={t("rclone.authorization.myOneDrive")}
          dataSource={primaryOptions}
          selectedKeys={primaryOptions.some((option) => option.value === answer) ? [answer] : []}
          isDisabled={isDisabled}
          description={t("rclone.authorization.primaryVerified")}
          onSelectionChange={(keys) => choose(String(Array.from(keys)[0] ?? ""))}
        />
      ) : (
        <p className="text-xs text-default-500">{t("rclone.authorization.noVerifiedPrimary")}</p>
      )}
      {(error || choices?.problem) && (
        <div className="space-y-1 text-xs text-warning-600" role="alert">
          <p>{problemLabel(error || choices?.problem || "RCLONE_DRIVE_CHOICES_FAILED")}</p>
          <Button
            size="sm"
            variant="light"
            isDisabled={loading || isDisabled}
            onPress={() => setRetry((value) => value + 1)}
          >
            {t("common.action.retry")}
          </Button>
        </div>
      )}
      {advancedOptions.length > 0 && (
        <Accordion
          isCompact
          selectedKeys={advancedOpen ? ["other-drives"] : []}
          onSelectionChange={(keys) =>
            setAdvancedOpen(keys === "all" || Array.from(keys).includes("other-drives"))
          }
        >
          <AccordionItem key="other-drives" title={t("rclone.authorization.otherDrives")}>
            <div className="space-y-2 pb-2">
              <p className="text-xs text-default-500">
                {t("rclone.authorization.otherDrivesHint")}
              </p>
              <Select
                label={t(`rclone.authorization.question.${question.name}`, {
                  defaultValue: question.name,
                })}
                dataSource={advancedOptions.map((option) => {
                  const candidate = candidateFor(option.value);
                  return {
                    ...option,
                    description: t(
                      `rclone.authorization.driveAccess.${candidate?.accessStatus ?? "unchecked"}`,
                    ),
                  };
                })}
                selectedKeys={
                  advancedOptions.some((option) => option.value === answer) ? [answer] : []
                }
                isDisabled={isDisabled}
                listboxProps={{
                  itemClasses: { title: "whitespace-normal", description: "whitespace-normal" },
                }}
                onSelectionChange={(keys) => choose(String(Array.from(keys)[0] ?? ""))}
              />
            </div>
          </AccordionItem>
        </Accordion>
      )}
      {answer && options.some((option) => option.value === answer) && (
        <div className="space-y-2 rounded-lg bg-default-50 p-3">
          <p className="break-words text-sm font-medium">{selectedLabel}</p>
          <div className="flex flex-wrap items-center gap-2">
            <Chip
              size="sm"
              variant="flat"
              color={
                selectedAccess === "accessible"
                  ? "success"
                  : selectedAccess === "unavailable"
                    ? "warning"
                    : "default"
              }
            >
              {t(`rclone.authorization.driveAccess.${selectedAccess}`)}
            </Chip>
            <Button
              size="sm"
              variant="flat"
              isDisabled={isDisabled || currentlyPreviewing}
              isLoading={currentlyPreviewing}
              onPress={() => void previewRoot()}
            >
              {t("rclone.authorization.previewRoot")}
            </Button>
          </div>
          <p className="text-xs text-default-500">{t("rclone.authorization.previewHint")}</p>
          {(currentPreviewError || selected?.problem) && (
            <p role="alert" className="text-xs text-warning-600">
              {problemLabel(
                currentPreviewError || selected?.problem || "RCLONE_DRIVE_PREVIEW_FAILED",
              )}
            </p>
          )}
          {currentPreview && (
            <div className="space-y-2" aria-label={t("rclone.authorization.rootPreview")}>
              {currentPreview.items.length ? (
                <ul className="max-h-60 space-y-1 overflow-y-auto text-xs">
                  {currentPreview.items.map((item, index) => (
                    <li key={`${item.name}:${index}`} className="flex min-w-0 items-start gap-2">
                      <span className="shrink-0 text-default-400">
                        {t(
                          item.isDirectory
                            ? "rclone.authorization.previewFolder"
                            : "rclone.authorization.previewFile",
                        )}
                      </span>
                      <span className="break-all">{item.name}</span>
                    </li>
                  ))}
                </ul>
              ) : (
                <p className="text-xs text-default-500">{t("rclone.authorization.emptyRoot")}</p>
              )}
              {currentPreview.hasMore && (
                <p className="text-xs text-default-500">
                  {t("rclone.authorization.previewHasMore")}
                </p>
              )}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
