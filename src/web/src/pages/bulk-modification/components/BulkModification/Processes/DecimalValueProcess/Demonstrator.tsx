"use client";

import type { DecimalProcessOptions } from "./models";
import type { BulkModificationVariable } from "@/pages/bulk-modification/components/BulkModification/models";

import { useTranslation } from "react-i18next";

import { BulkModificationDecimalProcessOperation } from "@/sdk/constants";
import { ProcessValueDemonstrator } from "@/pages/bulk-modification/components/BulkModification/ProcessValue";
import { Chip } from "@/components/bakaui";

type Props = {
  operation: BulkModificationDecimalProcessOperation;
  options?: DecimalProcessOptions;
  variables?: BulkModificationVariable[];
};

const Demonstrator = ({ operation, options, variables }: Props) => {
  const { t } = useTranslation();

  const renderValue = () => {
    if (!options?.value) return null;
    return <ProcessValueDemonstrator value={options.value} variables={variables} />;
  };

  switch (operation) {
    case BulkModificationDecimalProcessOperation.Delete:
      return <Chip color="danger" size="sm">{t("bulkModification.operation.decimal.delete")}</Chip>;
    case BulkModificationDecimalProcessOperation.SetWithFixedValue:
      return (
        <span>
          {t("bulkModification.operation.decimal.setWithFixedValue")}: {renderValue()}
        </span>
      );
    case BulkModificationDecimalProcessOperation.Add:
      return <span>+ {renderValue()}</span>;
    case BulkModificationDecimalProcessOperation.Subtract:
      return <span>- {renderValue()}</span>;
    case BulkModificationDecimalProcessOperation.Multiply:
      return <span>× {renderValue()}</span>;
    case BulkModificationDecimalProcessOperation.Divide:
      return <span>÷ {renderValue()}</span>;
    case BulkModificationDecimalProcessOperation.Round:
      return <span>{t("bulkModification.operation.decimal.round")} ({options?.decimalPlaces ?? 0})</span>;
    case BulkModificationDecimalProcessOperation.Ceil:
      return <span>{t("bulkModification.operation.decimal.ceil")}</span>;
    case BulkModificationDecimalProcessOperation.Floor:
      return <span>{t("bulkModification.operation.decimal.floor")}</span>;
    default:
      return null;
  }
};

Demonstrator.displayName = "DecimalValueProcessDemonstrator";

export default Demonstrator;
