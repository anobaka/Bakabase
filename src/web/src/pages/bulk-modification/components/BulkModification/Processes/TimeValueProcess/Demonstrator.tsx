"use client";

import type { TimeProcessOptions } from "./models";
import type { BulkModificationVariable } from "@/pages/bulk-modification/components/BulkModification/models";

import { useTranslation } from "react-i18next";

import { BulkModificationTimeProcessOperation } from "@/sdk/constants";
import { ProcessValueDemonstrator } from "@/pages/bulk-modification/components/BulkModification/ProcessValue";
import { Chip } from "@/components/bakaui";

type Props = {
  operation: BulkModificationTimeProcessOperation;
  options?: TimeProcessOptions;
  variables?: BulkModificationVariable[];
};

const Demonstrator = ({ operation, options, variables }: Props) => {
  const { t } = useTranslation();

  switch (operation) {
    case BulkModificationTimeProcessOperation.Delete:
      return <Chip color="danger" size="sm">{t("bulkModification.operation.time.delete")}</Chip>;
    case BulkModificationTimeProcessOperation.SetWithFixedValue:
      return (
        <span>
          {t("bulkModification.operation.time.setWithFixedValue")}:{" "}
          {options?.value && <ProcessValueDemonstrator value={options.value} variables={variables} />}
        </span>
      );
    case BulkModificationTimeProcessOperation.AddHours:
      return <span>+ {options?.amount} {t("common.unit.hours")}</span>;
    case BulkModificationTimeProcessOperation.SubtractHours:
      return <span>- {options?.amount} {t("common.unit.hours")}</span>;
    case BulkModificationTimeProcessOperation.AddMinutes:
      return <span>+ {options?.amount} {t("common.unit.minutes")}</span>;
    case BulkModificationTimeProcessOperation.SubtractMinutes:
      return <span>- {options?.amount} {t("common.unit.minutes")}</span>;
    case BulkModificationTimeProcessOperation.AddSeconds:
      return <span>+ {options?.amount} {t("common.unit.seconds")}</span>;
    case BulkModificationTimeProcessOperation.SubtractSeconds:
      return <span>- {options?.amount} {t("common.unit.seconds")}</span>;
    default:
      return null;
  }
};

Demonstrator.displayName = "TimeValueProcessDemonstrator";

export default Demonstrator;
