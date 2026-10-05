"use client";

import type { FC } from "react";
import type { DownloadInfoData, DownloadResource, ExtractionPlan } from "../results";

import { useTranslation } from "react-i18next";
import {
  AiOutlineArrowRight,
  AiOutlineCheckCircle,
  AiOutlineCloseCircle,
  AiOutlineCopy,
  AiOutlineLink,
  AiOutlineQuestionCircle,
  AiOutlineStop,
} from "react-icons/ai";

import { copyParserText } from "../results";
import { groupDownloadResources } from "../resourceDeduplication";
import { getDownloadUrl, getLinkHealthStatus } from "../downloadLinks";

import { AvailabilityDetails, EvidencePopover } from "./PostDetails";
import Tooltip from "./PostParserTooltip";

import { Button, toast } from "@/components/bakaui";
import BApi from "@/sdk/BApi";

interface Props {
  data: DownloadInfoData;
  showTitle?: boolean;
  showAvailability?: boolean;
}

type DisplayStep = Omit<ExtractionPlan["steps"][number], "op"> & {
  op: string;
  targetName?: string | null;
  targetDirectory?: string | null;
};

const supportedOperations = new Set([
  "renameExtension",
  "extractArchive",
  "renameFile",
  "moveFile",
]);

export function LinkHealthIndicator({ health }: { health?: DownloadResource["linkHealth"] }) {
  const { t } = useTranslation();
  const status = getLinkHealthStatus(health);
  const label = t(`postParser.linkHealth.${status}`);
  const reason = health?.reason
    ? t(`postParser.linkHealth.reason.${health.reason}`, { defaultValue: health.reason })
    : !health
      ? t("postParser.linkHealth.notChecked")
      : undefined;
  const Icon =
    status === "available"
      ? AiOutlineCheckCircle
      : status === "unavailable"
        ? AiOutlineCloseCircle
        : status === "unsupported"
          ? AiOutlineStop
          : AiOutlineQuestionCircle;

  return (
    <Tooltip
      content={
        <div className="max-w-xs space-y-1 text-xs">
          <p className="font-medium">{label}</p>
          {reason && reason !== label && <p>{reason}</p>}
          {health?.checkedAt && (
            <p className="text-default-400">
              {t("postParser.linkHealth.checkedAt", {
                time: new Date(health.checkedAt).toLocaleString(),
              })}
            </p>
          )}
        </div>
      }
    >
      <button
        aria-label={label}
        className={`inline-flex h-6 shrink-0 items-center text-base ${status === "available" ? "text-success" : status === "unavailable" ? "text-danger" : "text-default-400"}`}
        data-link-health={status}
        type="button"
      >
        <Icon aria-hidden />
      </button>
    </Tooltip>
  );
}

function ProcessingPlan({
  plan,
  copy,
}: {
  plan: ExtractionPlan;
  copy: (value: string, successMessage?: string) => Promise<void>;
}) {
  const { t } = useTranslation();
  const steps: DisplayStep[] = Array.isArray(plan.steps) ? plan.steps : [];
  const label = (step: DisplayStep) => {
    switch (step.op) {
      case "renameExtension":
        return t("postParser.extraction.renameExtension", { extension: step.extension ?? "?" });
      case "extractArchive":
        return step.password
          ? t("postParser.extraction.extractArchive", { password: step.password })
          : t("postParser.extraction.extractArchiveNoPassword");
      case "renameFile":
        return t("postParser.processing.renameFile", { name: step.targetName ?? "?" });
      case "moveFile":
        return t("postParser.processing.moveFile", { directory: step.targetDirectory ?? "?" });
      default:
        return step.op;
    }
  };

  return (
    <div
      aria-label={t("postParser.processing.label")}
      className="flex min-w-0 flex-wrap items-center gap-x-1 gap-y-1.5 text-xs"
    >
      <span className="mr-1 shrink-0 text-default-500">{t("postParser.processing.label")}</span>
      {steps.length > 0 ? (
        <ol aria-label={t("postParser.processing.steps")} className="contents">
          {steps.map((step, index) => {
            const inputIndex = steps.findIndex((previous) => previous.id === step.input);
            const input =
              step.input === "download"
                ? t("postParser.extraction.download")
                : inputIndex >= 0
                  ? t("postParser.extraction.stepOutput", { number: inputIndex + 1 })
                  : step.input;
            const copyValue =
              step.op === "extractArchive"
                ? step.password
                : step.op === "renameExtension"
                  ? step.extension
                  : step.op === "renameFile"
                    ? step.targetName
                    : step.op === "moveFile"
                      ? step.targetDirectory
                      : undefined;
            const copyLabel = t(`postParser.processing.copy.${step.op}`);

            return (
              <li key={step.id || index} className="inline-flex max-w-full items-center gap-1">
                {index > 0 && (
                  <AiOutlineArrowRight aria-hidden className="shrink-0 text-default-400" />
                )}
                <Tooltip
                  content={
                    <div className="max-w-xs space-y-1 text-xs">
                      <p>{t("postParser.extraction.input", { input })}</p>
                      {step.selector && (
                        <p className="break-all">
                          {t("postParser.processing.selector", { selector: step.selector })}
                        </p>
                      )}
                      {copyValue ? (
                        <p className="text-default-500">{copyLabel}</p>
                      ) : step.op === "extractArchive" ? (
                        <p className="text-default-500">
                          {t("postParser.processing.noPasswordToCopy")}
                        </p>
                      ) : null}
                      {!supportedOperations.has(step.op) && (
                        <p className="text-warning-600">
                          {t("postParser.processing.unsupportedOperation")}
                        </p>
                      )}
                    </div>
                  }
                >
                  <button
                    className={`max-w-full break-all rounded-md bg-default-100 px-2 py-1 ${copyValue ? "cursor-pointer hover:bg-default-200" : "cursor-default"}`}
                    data-processing-operation={step.op}
                    type="button"
                    onClick={
                      copyValue
                        ? () =>
                            copy(
                              copyValue,
                              t(`postParser.processing.copied.${step.op}`, { value: copyValue }),
                            )
                        : undefined
                    }
                  >
                    {label(step)}
                  </button>
                </Tooltip>
              </li>
            );
          })}
        </ol>
      ) : (
        <span
          className={`rounded-md px-2 py-1 ${plan.requirement === "notRequired" ? "bg-default-100 text-default-500" : "bg-warning/10 text-warning-600"}`}
        >
          {t(
            plan.requirement === "notRequired"
              ? "postParser.processing.notRequired"
              : "postParser.processing.unknown",
          )}
        </span>
      )}
      <Button
        isIconOnly
        aria-label={t("postParser.action.copyPlan")}
        className="h-6 min-w-6 w-6 shrink-0"
        size="sm"
        variant="light"
        onPress={() => copy(JSON.stringify(plan, null, 2))}
      >
        <AiOutlineCopy aria-hidden className="text-sm" />
      </Button>
      <EvidencePopover evidence={plan.evidence} />
    </div>
  );
}

const DownloadInfoResultRenderer: FC<Props> = ({
  data,
  showTitle = true,
  showAvailability = true,
}) => {
  const { t } = useTranslation();
  const copy = async (value: string, successMessage?: string) => {
    try {
      await copyParserText(value);
      toast.success({
        title: successMessage ?? t<string>("postParser.result.copied"),
        timeout: 3000,
      });
    } catch {
      toast.danger(t<string>("postParser.result.copyFailed"));
    }
  };
  const warnings = Array.isArray(data.warnings) ? data.warnings : [];
  const resources = groupDownloadResources(Array.isArray(data.resources) ? data.resources : []);

  return (
    <div className="flex min-w-0 flex-col gap-2">
      {(data.isComplete === false || warnings.length > 0) && (
        <div className="flex items-center gap-1 text-xs text-warning-600">
          <span>
            {t(
              data.isComplete === false
                ? "postParser.result.partial"
                : "postParser.result.hasWarnings",
            )}
          </span>
          <EvidencePopover evidence={warnings} label={t("postParser.label.resultDetails")} />
        </div>
      )}
      {showAvailability && <AvailabilityDetails value={data.availability} />}
      {showTitle && data.title && <p className="text-sm font-medium">{data.title}</p>}
      {!resources.length && (
        <p className="text-sm text-default-400">{t("postParser.result.noResources")}</p>
      )}
      {resources.map(({ resource }, index) => {
        const url = getDownloadUrl(resource.link, resource.code);
        const passwordInPlan =
          resource.password &&
          Array.isArray(resource.extraction?.steps) &&
          resource.extraction.steps.some(
            (step) => step.op === "extractArchive" && step.password === resource.password,
          );

        return (
          <div key={index} className="flex min-w-0 flex-col gap-1.5 text-sm">
            <div className="flex min-w-0 flex-wrap items-center gap-1">
              {url && (
                <div className="inline-flex min-w-0 max-w-full items-center gap-1">
                  <Tooltip content={<span className="max-w-sm break-all">{url}</span>}>
                    <Button
                      aria-label={url}
                      className="h-auto min-w-0 flex-1 justify-start px-0 py-1"
                      color="primary"
                      size="sm"
                      startContent={<AiOutlineLink aria-hidden className="shrink-0 text-base" />}
                      variant="light"
                      onPress={() => BApi.gui.openUrlInDefaultBrowser({ url })}
                    >
                      <span className="min-w-0 truncate text-left">{url}</span>
                    </Button>
                  </Tooltip>
                  <LinkHealthIndicator health={resource.linkHealth} />
                  <Button
                    isIconOnly
                    aria-label={t("postParser.action.copyLink")}
                    className="h-6 min-w-6 w-6 shrink-0"
                    size="sm"
                    variant="light"
                    onPress={() => copy(url)}
                  >
                    <AiOutlineCopy aria-hidden className="text-sm" />
                  </Button>
                </div>
              )}
              {resource.code && (
                <Button
                  aria-label={t("postParser.action.copyCode")}
                  className="h-auto min-h-6 min-w-0 whitespace-normal px-2 py-1 text-xs"
                  size="sm"
                  startContent={<AiOutlineCopy aria-hidden />}
                  variant="flat"
                  onPress={() => copy(resource.code!)}
                >
                  {t("postParser.label.accessCode")}: {resource.code}
                </Button>
              )}
              {resource.password && !passwordInPlan && (
                <Button
                  aria-label={t("postParser.action.copyPassword")}
                  className="h-auto min-h-6 min-w-0 whitespace-normal px-2 py-1 text-xs"
                  color="warning"
                  size="sm"
                  startContent={<AiOutlineCopy aria-hidden />}
                  variant="flat"
                  onPress={() => copy(resource.password!)}
                >
                  {t("postParser.label.decompressionPassword")}: {resource.password}
                </Button>
              )}
            </div>
            {resource.extraction && <ProcessingPlan copy={copy} plan={resource.extraction} />}
          </div>
        );
      })}
    </div>
  );
};

export default DownloadInfoResultRenderer;
