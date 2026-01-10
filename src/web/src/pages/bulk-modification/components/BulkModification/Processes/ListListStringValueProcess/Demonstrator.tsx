"use client";

import type { ListListStringProcessOptions } from "./models";
import type { BulkModificationVariable } from "@/pages/bulk-modification/components/BulkModification/models";

import { useTranslation } from "react-i18next";

import { BulkModificationListListStringProcessOperation } from "@/sdk/constants";
import { ProcessValueDemonstrator } from "@/pages/bulk-modification/components/BulkModification/ProcessValue";
import { Chip } from "@/components/bakaui";

type Props = {
  operation: BulkModificationListListStringProcessOperation;
  options?: ListListStringProcessOptions;
  variables?: BulkModificationVariable[];
};

const Demonstrator = ({ operation, options, variables }: Props) => {
  const { t } = useTranslation();

  switch (operation) {
    case BulkModificationListListStringProcessOperation.Delete:
      return <Chip color="danger" size="sm">{t("bulkModification.operation.listListString.delete")}</Chip>;
    case BulkModificationListListStringProcessOperation.SetWithFixedValue:
      return (
        <span>
          {t("bulkModification.operation.listListString.setWithFixedValue")}:{" "}
          {options?.value && <ProcessValueDemonstrator value={options.value} variables={variables} />}
        </span>
      );
    case BulkModificationListListStringProcessOperation.Append:
      return (
        <span>
          {t("bulkModification.operation.listListString.append")}:{" "}
          {options?.value && <ProcessValueDemonstrator value={options.value} variables={variables} />}
        </span>
      );
    case BulkModificationListListStringProcessOperation.Prepend:
      return (
        <span>
          {t("bulkModification.operation.listListString.prepend")}:{" "}
          {options?.value && <ProcessValueDemonstrator value={options.value} variables={variables} />}
        </span>
      );
    case BulkModificationListListStringProcessOperation.Remove:
      return (
        <span>
          {t("bulkModification.operation.listListString.remove")}:{" "}
          {options?.value && <ProcessValueDemonstrator value={options.value} variables={variables} />}
        </span>
      );
    default:
      return null;
  }
};

Demonstrator.displayName = "ListListStringValueProcessDemonstrator";

export default Demonstrator;
