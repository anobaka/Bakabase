"use client";

import type { LinkProcessOptions } from "./models";
import type { BulkModificationVariable } from "@/pages/bulk-modification/components/BulkModification/models";

import { useTranslation } from "react-i18next";

import { BulkModificationLinkProcessOperation } from "@/sdk/constants";
import { ProcessValueDemonstrator } from "@/pages/bulk-modification/components/BulkModification/ProcessValue";
import { Chip } from "@/components/bakaui";
import { StringValueProcessDemonstrator } from "../StringValueProcess";

type Props = {
  operation: BulkModificationLinkProcessOperation;
  options?: LinkProcessOptions;
  variables?: BulkModificationVariable[];
};

const Demonstrator = ({ operation, options, variables }: Props) => {
  const { t } = useTranslation();

  switch (operation) {
    case bulkModification.operation.link.delete:
      return <Chip color="danger" size="sm">{t("bulkModification.operation.link.delete")}</Chip>;
    case bulkModification.operation.link.setWithFixedValue:
      return (
        <span>
          {t("bulkModification.operation.link.setWithFixedValue")}:{" "}
          {options?.value && <ProcessValueDemonstrator value={options.value} variables={variables} />}
        </span>
      );
    case bulkModification.operation.link.setText:
      return (
        <span>
          {t("bulkModification.operation.link.setText")}:{" "}
          {options?.text && <ProcessValueDemonstrator value={options.text} variables={variables} />}
        </span>
      );
    case bulkModification.operation.link.setUrl:
      return (
        <span>
          {t("bulkModification.operation.link.setUrl")}:{" "}
          {options?.url && <ProcessValueDemonstrator value={options.url} variables={variables} />}
        </span>
      );
    case bulkModification.operation.link.modifyText:
      return (
        <span>
          {t("bulkModification.operation.link.modifyText")}:{" "}
          {options?.stringOperation !== undefined && (
            <StringValueProcessDemonstrator
              operation={options.stringOperation}
              options={options.stringOptions}
              variables={variables}
            />
          )}
        </span>
      );
    case bulkModification.operation.link.modifyUrl:
      return (
        <span>
          {t("bulkModification.operation.link.modifyUrl")}:{" "}
          {options?.stringOperation !== undefined && (
            <StringValueProcessDemonstrator
              operation={options.stringOperation}
              options={options.stringOptions}
              variables={variables}
            />
          )}
        </span>
      );
    default:
      return null;
  }
};

Demonstrator.displayName = "LinkValueProcessDemonstrator";

export default Demonstrator;
