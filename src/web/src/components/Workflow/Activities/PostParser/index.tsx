import type { WorkflowActivityUI } from "../types";
import { postTaskExecutionLabels } from "../executionLabels";
import type { PostAvailability, PostContentSnapshot } from "@/core/models/PostParserTask";

import { useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineLink, AiOutlineReload } from "react-icons/ai";

import { Accordion, AccordionItem, Button, Spinner, Switch } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import ContentReviewDialog from "@/pages/post-parser/components/ContentReviewDialog";
import type { ContentReviewResult } from "@/pages/post-parser/components/ContentPurchaseReviewModal";
import LockedContentDetails from "@/pages/post-parser/components/LockedContentDetails";
import { unresolvedContentLocks } from "@/pages/post-parser/contentReadiness";
import { translateKnownMessage } from "@/pages/post-parser/knownMessages";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { AvailabilityDetails } from "@/pages/post-parser/components/PostDetails";
import { PostParserSource, WorkflowActivityCategory, WorkflowRunStatus } from "@/sdk/constants";
import { openExternalUrl } from "@/utils/openExternalUrl";

type ReadConfig = { useConfiguredSoulPlusPurchaseLimit: boolean };

export const PostParserReadContentUI: WorkflowActivityUI<ReadConfig> = {
  kind: "postParser.readContent",
  executionLabels: postTaskExecutionLabels,
  displayNameKey: "workflow.activity.postParserReadContent.displayName",
  category: WorkflowActivityCategory.Transform,
  defaultConfig: () => ({ useConfiguredSoulPlusPurchaseLimit: false }),
  parseConfig: (json) => {
    try {
      return {
        useConfiguredSoulPlusPurchaseLimit:
          JSON.parse(json || "{}").useConfiguredSoulPlusPurchaseLimit === true,
      };
    } catch {
      return { useConfiguredSoulPlusPurchaseLimit: false };
    }
  },
  serializeConfig: (config) => JSON.stringify(config),
  isValid: () => true,
  ConfigForm: () => {
    const { t } = useTranslation();

    return (
      <div className="space-y-3">
        <p className="text-sm leading-relaxed text-default-500">
          {t("workflow.activity.postParserReadContent.description")}
        </p>
      </div>
    );
  },
  Summary: () => {
    const { t } = useTranslation();

    return (
      <span className="text-xs text-default-500">
        {t("workflow.activity.postParserReadContent.description")}
      </span>
    );
  },
};

const ExtractExplanation = () => {
  const { t } = useTranslation();

  return (
    <p className="text-sm leading-relaxed text-default-500">
      {t("workflow.activity.postParserExtractDownloadInfo.description")}
    </p>
  );
};

export const PostParserExtractDownloadInfoUI: WorkflowActivityUI<Record<string, never>> = {
  kind: "postParser.extractDownloadInfo",
  executionLabels: postTaskExecutionLabels,
  displayNameKey: "workflow.activity.postParserExtractDownloadInfo.displayName",
  category: WorkflowActivityCategory.Transform,
  defaultConfig: () => ({}),
  parseConfig: () => ({}),
  serializeConfig: () => "{}",
  isValid: () => true,
  ConfigForm: ExtractExplanation,
  Summary: ExtractExplanation,
  ResumeForm: ({ submitting, onSubmit }) => {
    const { t } = useTranslation();

    return (
      <div className="space-y-3">
        <p>{t("postParser.tip.aiRequired")}</p>
        <Button isDisabled={submitting} onPress={() => onSubmit("{}")}>
          {t("postParser.action.retry")}
        </Button>
      </div>
    );
  },
};

const UnlockResumeForm: NonNullable<WorkflowActivityUI["ResumeForm"]> = ({
  promptJson,
  submitting,
  onSubmit,
}) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [continued, setContinued] = useState(false);
  const [reload, setReload] = useState(0);
  const [snapshot, setSnapshot] = useState<{
    key: string;
    status: "loading" | "ready" | "failed" | "changed";
    content?: PostContentSnapshot;
    source?: string;
  }>();
  type Prompt = {
    taskId?: number;
    revision?: number;
    workflowRunId?: number;
    source?: string;
    content?: PostContentSnapshot;
    availability?: PostAvailability;
    minimumRemainingCoins?: number;
    message?: string;
  };

  const prompt = useMemo<Prompt>(() => {
    try {
      return JSON.parse(promptJson || "{}") ?? {};
    } catch {
      return {};
    }
  }, [promptJson]);
  const hasTask = prompt.taskId != null;
  const identified =
    hasTask &&
    Number.isSafeInteger(prompt.taskId) &&
    prompt.taskId! > 0 &&
    Number.isSafeInteger(prompt.revision) &&
    prompt.revision! >= 0 &&
    Number.isSafeInteger(prompt.workflowRunId) &&
    prompt.workflowRunId! > 0;
  const identity = identified
    ? JSON.stringify([prompt.taskId, prompt.revision, prompt.workflowRunId])
    : undefined;
  const latestIdentity = useRef(identity);
  latestIdentity.current = identity;
  useEffect(() => setContinued(false), [identity]);
  useEffect(() => {
    if (!identity) return;
    let active = true;
    const controller = new AbortController();
    const [taskId, revision, workflowRunId] = JSON.parse(identity) as number[];
    setSnapshot({ key: identity, status: "loading" });
    void (async () => {
      try {
        const response = await BApi.postParser.getPostParserContentReadiness(taskId, {
          signal: controller.signal,
          sensitiveResponse: true,
          showErrorToast: false,
        });
        if (!active) return;
        if (response.code || !response.data) throw new Error("CONTENT_READINESS_UNAVAILABLE");
        const view = response.data;
        if (
          view.taskId !== taskId ||
          view.revision !== revision ||
          view.workflowRunId !== workflowRunId
        ) {
          setSnapshot({ key: identity, status: "changed" });
          return;
        }
        if (!view.contentSnapshot) throw new Error("CONTENT_READINESS_UNAVAILABLE");
        setSnapshot({
          key: identity,
          status: "ready",
          content: view.contentSnapshot as PostContentSnapshot,
          source: view.source,
        });
      } catch {
        if (active) setSnapshot({ key: identity, status: "failed" });
      }
    })();
    return () => {
      active = false;
      controller.abort();
    };
  }, [identity, reload]);
  const current = identity && snapshot?.key === identity ? snapshot : undefined;
  const status = !hasTask ? "legacy" : !identity ? "changed" : (current?.status ?? "loading");
  const content = hasTask
    ? current?.status === "ready"
      ? current.content
      : undefined
    : prompt.content;
  const source = hasTask ? current?.source : prompt.source;
  const locks = unresolvedContentLocks(content);
  const sourceUrl = content?.sourceUrl;
  const canResume = !submitting && !continued && (status === "ready" || status === "legacy");

  return (
    <div className="space-y-3">
      {status === "loading" && (
        <div role="status" className="flex items-center gap-2 text-sm text-default-500">
          <Spinner size="sm" />
          {t("postParser.review.currentContentLoading")}
        </div>
      )}
      {(status === "failed" || status === "changed") && (
        <div role="alert" className="space-y-2 text-sm text-warning-600">
          <p>
            {t(
              status === "changed"
                ? "postParser.review.currentContentChanged"
                : "postParser.review.currentContentUnavailable",
            )}
          </p>
          <Button
            size="sm"
            variant="flat"
            isDisabled={submitting || continued || !identity}
            onPress={() => setReload((value) => value + 1)}
          >
            {t("postParser.contentReadiness.reloadState")}
          </Button>
        </div>
      )}
      {status === "ready" && (
        <p className="text-xs text-default-500">{t("postParser.review.currentContentHint")}</p>
      )}
      {status === "legacy" && (
        <>
          <AvailabilityDetails value={prompt.availability} />
          {!prompt.availability && prompt.message && (
            <p className="text-sm">{translateKnownMessage(t, prompt.message)}</p>
          )}
        </>
      )}
      {locks.length > 0 && (
        <p className="rounded-lg bg-warning/10 p-3 text-sm leading-relaxed">
          {t("postParser.review.resumeUnlockHint")}
        </p>
      )}
      {content && (
        <LockedContentDetails
          content={content}
          source={source === "SoulPlus" ? PostParserSource.SoulPlus : undefined}
        />
      )}
      {hasTask && prompt.content && (
        <Accordion variant="light" keepContentMounted={false}>
          <AccordionItem key="paused" title={t("postParser.review.pausedContent")}>
            <div className="space-y-3">
              <p className="text-xs text-default-500">{t("postParser.review.pausedContentHint")}</p>
              <AvailabilityDetails value={prompt.availability} />
              {!prompt.availability && prompt.message && (
                <p className="text-sm">{translateKnownMessage(t, prompt.message)}</p>
              )}
              <LockedContentDetails
                content={prompt.content}
                source={prompt.source === "SoulPlus" ? PostParserSource.SoulPlus : undefined}
              />
            </div>
          </AccordionItem>
        </Accordion>
      )}
      {continued && (
        <p role="status" className="text-sm text-primary">
          {t("postParser.contentReadiness.continuationQueued")}
        </p>
      )}
      <div className="flex flex-wrap gap-2">
        {sourceUrl && (
          <Button
            size="sm"
            startContent={<AiOutlineLink aria-hidden />}
            variant="flat"
            onPress={() => openExternalUrl(sourceUrl)}
          >
            {t(locks.length ? "postParser.action.openPostToUnlock" : "postParser.action.openPost")}
          </Button>
        )}
        {identified && content && (
          <Button
            size="sm"
            variant="flat"
            isDisabled={!canResume}
            onPress={() =>
              createPortal(ContentReviewDialog, {
                scope: { status: "all", taskIds: [prompt.taskId!] },
                expectedIdentity: {
                  taskId: prompt.taskId!,
                  revision: prompt.revision!,
                  workflowRunId: prompt.workflowRunId!,
                },
                onChanged: (result?: ContentReviewResult) => {
                  if (latestIdentity.current !== identity) return;
                  if (result?.queuedCount) setContinued(true);
                  else setReload((value) => value + 1);
                },
              })
            }
          >
            {t("postParser.contentPurchaseReview.title")}
          </Button>
        )}
        <Button
          isDisabled={!canResume}
          size="sm"
          startContent={<AiOutlineReload aria-hidden />}
          onPress={() => {
            if (canResume) onSubmit("{}");
          }}
        >
          {t(
            locks.length
              ? "postParser.action.refreshAfterUnlock"
              : "postParser.action.continueParsing",
          )}
        </Button>
      </div>
    </div>
  );
};

const UnlockExplanation = () => {
  const { t } = useTranslation();

  return (
    <p className="text-sm text-default-500">
      {t("workflow.activity.postParserUnlockContent.description")}
    </p>
  );
};

export const PostParserUnlockContentUI: WorkflowActivityUI<ReadConfig> = {
  kind: "postParser.unlockContent",
  executionLabels: postTaskExecutionLabels,
  displayNameKey: "workflow.activity.postParserUnlockContent.displayName",
  category: WorkflowActivityCategory.Transform,
  defaultConfig: PostParserReadContentUI.defaultConfig,
  parseConfig: PostParserReadContentUI.parseConfig,
  serializeConfig: PostParserReadContentUI.serializeConfig,
  isValid: () => true,
  ConfigForm: ({ value, onChange }) => {
    const { t } = useTranslation();

    return (
      <div className="space-y-3">
        <UnlockExplanation />
        <Switch
          isSelected={value.useConfiguredSoulPlusPurchaseLimit}
          size="sm"
          onValueChange={(useConfiguredSoulPlusPurchaseLimit) =>
            onChange({ useConfiguredSoulPlusPurchaseLimit })
          }
        >
          {t("workflow.postParser.purchaseLimit")}
        </Switch>
      </div>
    );
  },
  Summary: UnlockExplanation,
  ResumeForm: UnlockResumeForm,
};
const CheckLinksExplanation = () => {
  const { t } = useTranslation();

  return (
    <p className="text-sm text-default-500">
      {t("workflow.activity.postParserCheckLinks.description")}
    </p>
  );
};

export const PostParserCheckLinksUI: WorkflowActivityUI<Record<string, never>> = {
  kind: "postParser.checkLinks",
  executionLabels: postTaskExecutionLabels,
  displayNameKey: "workflow.activity.postParserCheckLinks.displayName",
  category: WorkflowActivityCategory.Transform,
  defaultConfig: () => ({}),
  parseConfig: () => ({}),
  serializeConfig: () => "{}",
  isValid: () => true,
  ConfigForm: CheckLinksExplanation,
  Summary: CheckLinksExplanation,
};
