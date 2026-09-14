"use client";

import type { Operation, OperationFieldsProps } from "./types";

import React from "react";

import { Select } from "../../bakaui";
import { getFieldRequirements } from "../validation";

import { FileNameModifierCaseType, fileNameModifierCaseTypes } from "@/sdk/constants";

const CaseTypeOptions = fileNameModifierCaseTypes.map((opt) => ({
  label: "FileNameModifier.CaseType." + FileNameModifierCaseType[opt.value],
  value: opt.value,
}));

const ChangeCaseOperationFields: React.FC<OperationFieldsProps> = ({ operation, t, onChange }) => {
  const requirements = getFieldRequirements(operation);

  return (
    <Select
      disallowEmptySelection
      className="w-full min-w-0"
      dataSource={CaseTypeOptions.map((opt) => ({
        label: t<string>(opt.label),
        value: opt.value,
      }))}
      isRequired={requirements.caseType}
      label={t<string>("FileNameModifier.Label.CaseType")}
      placeholder={t<string>("FileNameModifier.Placeholder.CaseType")}
      selectedKeys={[operation.caseType?.toString() || ""]}
      size="sm"
      onSelectionChange={(keys) => {
        const key = parseInt(Array.from(keys)[0] as string);

        if (key !== operation.caseType) {
          onChange({ ...operation, caseType: key as Operation["caseType"] });
        }
      }}
    />
  );
};

export default ChangeCaseOperationFields;
