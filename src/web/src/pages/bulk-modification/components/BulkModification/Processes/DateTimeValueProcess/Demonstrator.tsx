"use client";

import type { DateTimeProcessOptions } from "./models";
import type { BulkModificationVariable } from "@/pages/bulk-modification/components/BulkModification/models";

import { useTranslation } from "react-i18next";

import { BulkModificationDateTimeProcessOperation } from "@/sdk/constants";
import { ProcessValueDemonstrator } from "@/pages/bulk-modification/components/BulkModification/ProcessValue";
import { Chip } from "@/components/bakaui";

type Props = {
  operation: BulkModificationDateTimeProcessOperation;
  options?: DateTimeProcessOptions;
  variables?: BulkModificationVariable[];
};

const Demonstrator = ({ operation, options, variables }: Props) => {
  const { t } = useTranslation();

  switch (operation) {
    case BulkModificationDateTimeProcessOperation.Delete:
      return <Chip color="danger" size="sm">{t("bulkModification.operation.dateTime.delete")}</Chip>;
    case BulkModificationDateTimeProcessOperation.SetWithFixedValue:
      return (
        <span>
          {t("bulkModification.operation.dateTime.setWithFixedValue")}:{" "}
          {options?.value && <ProcessValueDemonstrator value={options.value} variables={variables} />}
        </span>
      );
    case BulkModificationDateTimeProcessOperation.AddDays:
      return <span>+ {options?.amount} {t("common.unit.days")}</span>;
    case BulkModificationDateTimeProcessOperation.SubtractDays:
      return <span>- {options?.amount} {t("common.unit.days")}</span>;
    case BulkModificationDateTimeProcessOperation.AddMonths:
      return <span>+ {options?.amount} {t("common.unit.months")}</span>;
    case BulkModificationDateTimeProcessOperation.SubtractMonths:
      return <span>- {options?.amount} {t("common.unit.months")}</span>;
    case BulkModificationDateTimeProcessOperation.AddYears:
      return <span>+ {options?.amount} {t("common.unit.years")}</span>;
    case BulkModificationDateTimeProcessOperation.SubtractYears:
      return <span>- {options?.amount} {t("common.unit.years")}</span>;
    case BulkModificationDateTimeProcessOperation.SetToNow:
      return <Chip color="primary" size="sm">{t("bulkModification.operation.dateTime.setToNow")}</Chip>;
    default:
      return null;
  }
};

Demonstrator.displayName = "DateTimeValueProcessDemonstrator";

export default Demonstrator;
