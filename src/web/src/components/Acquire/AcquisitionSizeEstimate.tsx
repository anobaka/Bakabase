import { useTranslation } from "react-i18next";
import { Tooltip } from "@/components/bakaui";
import { bytesToSize } from "@/components/utils";
import type BApi from "@/sdk/BApi";

type TaskPage = NonNullable<
  Awaited<ReturnType<typeof BApi.acquisition.searchAcquisitionTasks>>["data"]
>;
export type AcquisitionSizeEstimateValue = NonNullable<TaskPage["sizeEstimates"]>[string];

export default function AcquisitionSizeEstimate({
  estimate,
}: {
  estimate?: AcquisitionSizeEstimateValue;
}) {
  const { t } = useTranslation();
  const known =
    estimate?.bytes != null &&
    Number.isFinite(estimate.bytes) &&
    estimate.bytes >= 0 &&
    ["delivered", "processed", "downloadReference"].includes(estimate.basis);
  const basis = known ? estimate!.basis : "unknown";
  const description = [
    t(`acquisition.size.basis.${basis}`),
    known && estimate?.partial && t("acquisition.size.partial.hint"),
  ]
    .filter(Boolean)
    .join(" ");
  const label = known
    ? t(`acquisition.size.value.${basis}`, { size: bytesToSize(estimate!.bytes) })
    : t("acquisition.size.unknown");
  return (
    <Tooltip content={description}>
      <span
        className="text-xs text-default-500"
        tabIndex={0}
        aria-label={`${label}. ${description}`}
      >
        {label}
        {known && estimate?.partial && ` · ${t("acquisition.size.partial.label")}`}
      </span>
    </Tooltip>
  );
}
