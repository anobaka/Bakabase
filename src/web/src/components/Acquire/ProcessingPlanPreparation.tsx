import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Button } from "@/components/bakaui";
import ProcessingPlanField from "@/components/Workflow/ProcessingPlanField";
import type { PreparedCandidate } from "./models";

export default function ProcessingPlanPreparation({
  plan,
  value,
  onChange,
  onReset,
  edited,
  isDisabled = false,
  checking = false,
}: {
  plan?: PreparedCandidate["processingPlan"];
  value: string;
  onChange: (value: string) => void;
  onReset: () => void;
  edited: boolean;
  isDisabled?: boolean;
  checking?: boolean;
}) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  return (
    <div className="min-w-0 space-y-2 rounded-lg bg-default-50 p-3">
      <div className="flex flex-wrap items-center gap-2 text-xs">
        <span className="font-medium">{t("acquire.processingPlan.title")}</span>
        <span className={plan?.blocksSubmission ? "text-warning-600" : "text-default-500"}>
          {t(
            checking
              ? "acquire.preparation.checking"
              : `acquire.processingPlan.status.${plan?.status ?? "notProvided"}`,
          )}
        </span>
        {plan && (
          <span className="text-default-500">
            {t("acquire.processingPlan.stepCount", { count: plan.stepCount })}
          </span>
        )}
        <Button
          size="sm"
          variant="light"
          isDisabled={isDisabled}
          onPress={() => setOpen((current) => !current)}
        >
          {t(open ? "acquire.processingPlan.hide" : "acquire.processingPlan.edit")}
        </Button>
      </div>
      {plan?.blocksSubmission && (
        <p className="text-xs text-warning-600">{t("acquire.processingPlan.blockedHint")}</p>
      )}
      {plan?.issues.map((issue, index) => (
        <p key={`${issue.code}:${index}`} className="break-words text-xs text-warning-600">
          {t(`acquire.requirement.${issue.code}`, { defaultValue: issue.message })}
        </p>
      ))}
      {open && (
        <div className="space-y-2">
          <p className="text-xs text-default-500">{t("acquire.processingPlan.editHint")}</p>
          <ProcessingPlanField value={value} onChange={onChange} isDisabled={isDisabled} />
          {edited && (
            <Button size="sm" variant="light" isDisabled={isDisabled} onPress={onReset}>
              {t("acquire.processingPlan.reset")}
            </Button>
          )}
        </div>
      )}
    </div>
  );
}
