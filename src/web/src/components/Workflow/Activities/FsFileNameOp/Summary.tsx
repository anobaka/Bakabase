import type { FsFileNameOpConfig } from "./types";

import React from "react";
import { useTranslation } from "react-i18next";

import { FileNameModifierOperationType } from "@/sdk/constants";

const Summary: React.FC<{ config: FsFileNameOpConfig }> = ({ config }) => {
  const { t } = useTranslation();
  const ops = config.operations ?? [];

  if (ops.length === 0) {
    return (
      <span className="text-xs text-warning">
        {t<string>("workflow.activity.fsFileNameOp.summary.empty")}
      </span>
    );
  }

  return (
    <span className="text-xs text-default-500">
      {t<string>("workflow.activity.fsFileNameOp.summary.operations", {
        count: ops.length,
        kinds: Array.from(new Set(ops.map((o) => FileNameModifierOperationType[o.operation]))).join(
          ", ",
        ),
      })}
    </span>
  );
};

export default Summary;
