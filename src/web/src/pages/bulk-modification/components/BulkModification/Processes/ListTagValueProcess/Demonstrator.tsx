"use client";

import type { ListTagProcessOptions } from "./models";
import type { BulkModificationVariable } from "@/pages/bulk-modification/components/BulkModification/models";

import { useTranslation } from "react-i18next";

import { BulkModificationListTagProcessOperation } from "@/sdk/constants";
import { ProcessValueDemonstrator } from "@/pages/bulk-modification/components/BulkModification/ProcessValue";
import { Chip } from "@/components/bakaui";

type Props = {
  operation: BulkModificationListTagProcessOperation;
  options?: ListTagProcessOptions;
  variables?: BulkModificationVariable[];
};

const Demonstrator = ({ operation, options, variables }: Props) => {
  const { t } = useTranslation();

  switch (operation) {
    case BulkModificationListTagProcessOperation.Delete:
      return <Chip color="danger" size="sm">{t("bulkModification.operation.listTag.delete")}</Chip>;
    case BulkModificationListTagProcessOperation.SetWithFixedValue:
      return (
        <span>
          {t("bulkModification.operation.listTag.setWithFixedValue")}:{" "}
          {options?.value && <ProcessValueDemonstrator value={options.value} variables={variables} />}
        </span>
      );
    case BulkModificationListTagProcessOperation.Append:
      return (
        <span>
          {t("bulkModification.operation.listTag.append")}:{" "}
          {options?.value && <ProcessValueDemonstrator value={options.value} variables={variables} />}
        </span>
      );
    case BulkModificationListTagProcessOperation.Prepend:
      return (
        <span>
          {t("bulkModification.operation.listTag.prepend")}:{" "}
          {options?.value && <ProcessValueDemonstrator value={options.value} variables={variables} />}
        </span>
      );
    case BulkModificationListTagProcessOperation.Remove:
      return (
        <span>
          {t("bulkModification.operation.listTag.remove")}:{" "}
          {options?.value && <ProcessValueDemonstrator value={options.value} variables={variables} />}
        </span>
      );
    default:
      return null;
  }
};

Demonstrator.displayName = "ListTagValueProcessDemonstrator";

export default Demonstrator;
