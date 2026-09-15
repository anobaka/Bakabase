import type { WorkflowTriggerBadgeProps } from "./WorkflowTriggerBadge";

import { useTranslation } from "react-i18next";

import { getWorkflowTriggerUI } from "./Triggers";
import { triggerDisplayName } from "./displayNames";
import { workflowDescription } from "./metadata";
import { triggerSourceLabel, useWorkflowTriggerDescriptors } from "./triggerPresentation";
import WorkflowTriggerBadge from "./WorkflowTriggerBadge";

import { HelpCenterButton } from "@/components/HelpCenter";
import { Button } from "@/components/bakaui";

interface Props extends WorkflowTriggerBadgeProps {
  compact?: boolean;
  showActions?: boolean;
  showHelp?: boolean;
  onNavigate?: (path: string) => void;
}

const TriggerUsageSummary = ({
  trigger: supplied,
  triggerKind,
  compact = false,
  showActions = true,
  showHelp = true,
  onNavigate,
}: Props) => {
  const { t } = useTranslation();
  const query = useWorkflowTriggerDescriptors(!supplied && !!triggerKind);
  const trigger = supplied ?? query.data?.find((item) => item.kind === triggerKind);
  const kind = trigger?.kind ?? triggerKind ?? "";
  const guide = getWorkflowTriggerUI(kind)?.guide;

  if (!trigger) {
    return (
      <div className="text-xs text-default-500" role="status">
        {t(
          query.isError
            ? "workflowTriggers.catalog.error"
            : query.isPending && triggerKind
              ? "workflowTriggers.catalog.loading"
              : "workflowTriggers.catalog.unavailable",
        )}
        {showActions && showHelp && <HelpCenterButton section="triggers" topic="workflow" />}
      </div>
    );
  }

  const description = workflowDescription(trigger, t);

  return (
    <div className={`flex flex-col ${compact ? "gap-1.5 text-xs" : "gap-3 text-sm"}`}>
      <div className="flex flex-wrap items-center gap-2">
        <WorkflowTriggerBadge trigger={trigger} />
        <span className="font-medium text-foreground">
          {triggerDisplayName(t, kind, trigger.displayName)}
        </span>
        <span className="text-default-500">
          {t("workflowTriggers.source.label")}: {triggerSourceLabel(trigger, t)}
        </span>
      </div>
      {description && <p className="leading-relaxed text-default-500">{description}</p>}
      {!compact && (
        <dl className="flex flex-col gap-2 text-xs leading-relaxed">
          <div>
            <dt className="font-medium text-default-700">{t("workflowTriggers.input.label")}</dt>
            <dd className="mt-0.5 text-default-500">
              {guide ? t(guide.inputKey) : t("workflowTriggers.input.unknown")}
            </dd>
          </div>
          <div>
            <dt className="font-medium text-default-700">
              {t("workflowTriggers.configure.label")}
            </dt>
            <dd className="mt-0.5 text-default-500">
              {guide ? t(guide.configureKey) : t("workflowTriggers.configure.unknown")}
            </dd>
          </div>
          <div>
            <dt className="font-medium text-default-700">{t("workflowTriggers.manual.label")}</dt>
            <dd className="mt-0.5 text-default-500">
              {t(
                trigger.supportsManualRun
                  ? trigger.requiresManualPayload
                    ? "workflowTriggers.manual.withInput"
                    : "workflowTriggers.manual.configured"
                  : "workflowTriggers.manual.unavailable",
              )}
            </dd>
          </div>
        </dl>
      )}
      {showActions && (
        <div className="flex flex-wrap items-center gap-1">
          {guide && (
            <Button
              as={onNavigate ? "button" : "a"}
              href={onNavigate ? undefined : `#${guide.sourceEntry.path}`}
              size="sm"
              variant="light"
              onPress={() => onNavigate?.(guide.sourceEntry.path)}
            >
              {t("workflowTriggers.openSource", { source: t(guide.sourceEntry.labelKey) })}
            </Button>
          )}
          {showHelp && (
            <HelpCenterButton
              label={t("workflowTriggers.openCatalog")}
              {...(guide?.helpTarget ?? { topic: "workflow", section: "triggers" })}
            />
          )}
        </div>
      )}
    </div>
  );
};

export default TriggerUsageSummary;
