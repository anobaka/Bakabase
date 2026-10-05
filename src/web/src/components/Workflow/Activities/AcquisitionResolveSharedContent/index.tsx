"use client";

import type { WorkflowActivityUI } from "../types";
import type { PostAvailability } from "@/core/models/PostParserTask";

import React from "react";
import { useTranslation } from "react-i18next";

import { Button, Chip, Switch } from "@/components/bakaui";
import { AcquisitionWaitReason, WorkflowActivityCategory } from "@/sdk/constants";
import { AvailabilityDetails } from "@/pages/post-parser/components/PostDetails";

interface Config {
  neverBuy: boolean;
}

interface LockedPart {
  url: string;
  price: number | null;
}

interface Prompt {
  locked: LockedPart[];
  limit: number;
  where: string;
  minimumRemainingCoins?: number;
  balance?: number | null;
  availability?: PostAvailability | null;
  message?: string | null;
}

/**
 * The envelope the engine hands back to the step: which wait it answers, and the step's own
 * payload inside it.
 */
const signalOf = (approved: boolean) =>
  JSON.stringify({
    reason: AcquisitionWaitReason.PaidContent,
    payloadJson: JSON.stringify({ approved }),
  });

const ConfigForm: React.FC<{ value: Config; onChange: (v: Config) => void }> = ({
  value,
  onChange,
}) => {
  const { t } = useTranslation();

  return (
    <Switch
      isSelected={!!value.neverBuy}
      size="sm"
      onValueChange={(neverBuy) => onChange({ ...value, neverBuy })}
    >
      <span className="text-sm">
        {t<string>("workflow.acquisition.resolveSharedContent.neverBuy")}
      </span>
    </Switch>
  );
};

/**
 * What the user sees when a post charges for the part with the download in it. It shows the price
 * and who is charging it, and does nothing until they say so — the one place in this pipeline
 * where a step spends money.
 */
const ResumeForm: WorkflowActivityUI<Config>["ResumeForm"] = ({
  promptJson,
  submitting,
  onSubmit,
}) => {
  const { t } = useTranslation();
  let prompt: Prompt | null = null;

  try {
    prompt = promptJson ? (JSON.parse(promptJson) as Prompt) : null;
  } catch {
    prompt = null;
  }
  const quoted =
    !!prompt?.locked?.length && prompt.locked.every((part) => part.price != null && !!part.url);
  const total = (prompt?.locked ?? []).reduce((sum, part) => sum + (part.price ?? 0), 0);
  const reserve = prompt?.minimumRemainingCoins ?? 0;
  const insufficient = prompt?.balance != null && prompt.balance - total < reserve;

  return (
    <div className="flex flex-col gap-3">
      <div className="text-sm">
        {t<string>("workflow.acquisition.resolveSharedContent.purchase.explain", {
          where: prompt?.where ?? "",
        })}
      </div>
      <AvailabilityDetails value={prompt?.availability} />
      {prompt?.message && (
        <p className="whitespace-pre-wrap text-xs text-default-500">{prompt.message}</p>
      )}

      <div className="flex flex-col gap-1">
        {(prompt?.locked ?? []).map((l) => (
          <div key={l.url} className="flex items-center gap-2 text-xs">
            <Chip color="warning" size="sm" variant="flat">
              {l.price == null
                ? t<string>("workflow.acquisition.resolveSharedContent.purchase.priceUnknown")
                : l.price}
            </Chip>
            <span className="truncate text-default-500">{l.url}</span>
          </div>
        ))}
      </div>

      {prompt != null && (
        <div className="text-xs text-default-400">
          {t<string>("workflow.acquisition.resolveSharedContent.purchase.limit", {
            limit: prompt.limit,
          })}
        </div>
      )}
      <p className="text-xs text-default-500">
        {t("postParser.purchase.balance", {
          balance: prompt?.balance ?? t("postParser.label.unknown"),
          reserve,
          total: quoted ? total : t("postParser.label.unknown"),
        })}
      </p>
      {insufficient && (
        <p className="text-xs text-warning-600">{t("postParser.purchase.insufficient")}</p>
      )}

      <div className="flex gap-2">
        <Button
          color="primary"
          isDisabled={submitting || !quoted || insufficient}
          size="sm"
          onPress={() => onSubmit(signalOf(true))}
        >
          {t<string>("workflow.acquisition.resolveSharedContent.purchase.approve")}
        </Button>
        <Button
          isDisabled={submitting}
          size="sm"
          variant="flat"
          onPress={() => onSubmit(signalOf(false))}
        >
          {t<string>("workflow.acquisition.resolveSharedContent.purchase.decline")}
        </Button>
      </div>
    </div>
  );
};

export const AcquisitionResolveSharedContentUI: WorkflowActivityUI<Config> = {
  kind: "acquisition.resolveSharedContent",
  displayNameKey: "workflow.acquisition.step.resolveSharedContent",
  category: WorkflowActivityCategory.Action,
  defaultConfig: () => ({ neverBuy: false }),
  parseConfig: (json) => {
    try {
      return json
        ? { neverBuy: false, ...(JSON.parse(json) as Partial<Config>) }
        : { neverBuy: false };
    } catch {
      return { neverBuy: false };
    }
  },
  serializeConfig: (config) => JSON.stringify(config),
  isValid: () => true,
  ConfigForm,
  Summary: ({ config }) => {
    const { t } = useTranslation();

    return config.neverBuy ? (
      <span className="text-xs text-default-500">
        {t<string>("workflow.acquisition.resolveSharedContent.neverBuy")}
      </span>
    ) : null;
  },
  ResumeForm,
};
