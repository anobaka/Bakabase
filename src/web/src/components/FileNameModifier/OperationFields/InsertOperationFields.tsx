"use client";

import type { Operation, OperationFieldsProps } from "./types";

import React from "react";

import { Input, Select, NumberInput } from "../../bakaui";
import { getFieldRequirements } from "../validation";

import { FileNameModifierPosition, fileNameModifierPositions } from "@/sdk/constants";

const PositionType = FileNameModifierPosition;
const PositionTypeOptions = fileNameModifierPositions.map((opt) => ({
  label: "FileNameModifier.PositionType." + FileNameModifierPosition[opt.value],
  value: opt.value,
}));

const InsertOperationFields: React.FC<OperationFieldsProps> = ({ operation, t, onChange }) => {
  const handleChangeField = <K extends keyof Operation>(key: K, value: Operation[K]) =>
    onChange({ ...operation, [key]: value });

  const requirements = getFieldRequirements(operation);

  return (
    <>
      <Input
        className="w-full min-w-0"
        isRequired={requirements.text}
        label={t<string>("FileNameModifier.Label.Text")}
        placeholder={t<string>("FileNameModifier.Placeholder.Text")}
        size="sm"
        value={operation.text || ""}
        onValueChange={(e) => handleChangeField("text", e)}
      />
      {(operation.position === PositionType.BeforeText ||
        operation.position === PositionType.AfterText) && (
        <Input
          className="w-full min-w-0"
          isRequired={requirements.targetText}
          label={t<string>("FileNameModifier.Label.TargetText")}
          placeholder={t<string>("FileNameModifier.Placeholder.TargetText")}
          size="sm"
          value={operation.targetText || ""}
          onValueChange={(e) => handleChangeField("targetText", e)}
        />
      )}
      <Select
        disallowEmptySelection
        className="w-full min-w-0"
        dataSource={PositionTypeOptions.map((opt) => ({
          label: t<string>(opt.label),
          value: opt.value,
        }))}
        isRequired={requirements.position}
        label={t<string>("FileNameModifier.Label.PositionType")}
        placeholder={t<string>("FileNameModifier.Placeholder.PositionType")}
        selectedKeys={[operation.position?.toString() || ""]}
        size="sm"
        onSelectionChange={(keys) => {
          const key = parseInt(Array.from(keys)[0] as string);

          if (key !== operation.position) {
            handleChangeField("position", key as Operation["position"]);
          }
        }}
      />
      {operation.position === PositionType.AtPosition && (
        <NumberInput
          className="w-full min-w-0"
          isRequired={requirements.positionIndex}
          label={t<string>("FileNameModifier.Label.PositionIndex")}
          placeholder={t<string>("FileNameModifier.Placeholder.PositionIndex")}
          size="sm"
          value={operation.positionIndex}
          onValueChange={(e) => handleChangeField("positionIndex", e)}
        />
      )}
    </>
  );
};

export default InsertOperationFields;
