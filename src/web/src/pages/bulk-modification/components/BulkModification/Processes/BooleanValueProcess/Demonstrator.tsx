"use client";

import type { BooleanProcessOptions } from "./models";
import type { BulkModificationVariable } from "@/pages/bulk-modification/components/BulkModification/models";

import { useTranslation } from "react-i18next";

import { BulkModificationBooleanProcessOperation } from "@/sdk/constants";
import { ProcessValueDemonstrator } from "@/pages/bulk-modification/components/BulkModification/ProcessValue";
import { Chip } from "@/components/bakaui";

type Props = {
  operation: BulkModificationBooleanProcessOperation;
  options?: BooleanProcessOptions;
  variables?: BulkModificationVariable[];
};

const Demonstrator = ({ operation, options, variables }: Props) => {
  const { t } = useTranslation();

  switch (operation) {
    case BulkModificationBooleanProcessOperation.Delete:
      return <Chip color="danger" size="sm">{t("bulkModification.operation.boolean.delete")}</Chip>;
    case BulkModificationBooleanProcessOperation.SetWithFixedValue:
      return (
        <span>
          {t("bulkModification.operation.boolean.setWithFixedValue")}:{" "}
          {options?.value && <ProcessValueDemonstrator value={options.value} variables={variables} />}
        </span>
      );
    case BulkModificationBooleanProcessOperation.Toggle:
      return <Chip color="primary" size="sm">{t("bulkModification.operation.boolean.toggle")}</Chip>;
    default:
      return null;
  }
};

Demonstrator.displayName = "BooleanValueProcessDemonstrator";

export default Demonstrator;
