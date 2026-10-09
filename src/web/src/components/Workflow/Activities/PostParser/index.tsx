import type { WorkflowActivityUI } from "../types";
import type { PostAvailability, PostContentSnapshot } from "@/core/models/PostParserTask";

import { useTranslation } from "react-i18next";
import { AiOutlineLink, AiOutlineReload } from "react-icons/ai";

import { Button, Switch } from "@/components/bakaui";
import { AvailabilityDetails } from "@/pages/post-parser/components/PostDetails";
import { WorkflowActivityCategory } from "@/sdk/constants";
import { openExternalUrl } from "@/utils/openExternalUrl";

type ReadConfig = { useConfiguredSoulPlusPurchaseLimit: boolean };

export const PostParserReadContentUI: WorkflowActivityUI<ReadConfig> = {
  kind: "postParser.readContent",
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
  let prompt: {
    content?: PostContentSnapshot;
    availability?: PostAvailability;
    minimumRemainingCoins?: number;
    message?: string;
  } = {};

  try {
    prompt = JSON.parse(promptJson || "{}");
  } catch {
    /* Show a refresh action for an unreadable prompt. */
  }
  const locks = prompt.content?.locks?.filter((lock) => !lock.isBought) ?? [];
  const sourceUrl = prompt.content?.sourceUrl;

  return (
    <div className="space-y-3">
      <AvailabilityDetails value={prompt.availability} />
      {!prompt.availability && prompt.message && <p className="text-sm">{prompt.message}</p>}
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
        <Button
          isDisabled={submitting}
          size="sm"
          startContent={<AiOutlineReload aria-hidden />}
          onPress={() => onSubmit("{}")}
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
  displayNameKey: "workflow.activity.postParserCheckLinks.displayName",
  category: WorkflowActivityCategory.Transform,
  defaultConfig: () => ({}),
  parseConfig: () => ({}),
  serializeConfig: () => "{}",
  isValid: () => true,
  ConfigForm: CheckLinksExplanation,
  Summary: CheckLinksExplanation,
};
