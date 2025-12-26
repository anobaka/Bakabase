"use client";

import React from "react";

import { Input, NumberInput, Select } from "../../bakaui";

import {
  FileNameModifierPosition,
  fileNameModifierPositions,
} from "@/sdk/constants";
import { getFieldRequirements } from "../validation";

const PositionType = FileNameModifierPosition;
const PositionTypeOptions = fileNameModifierPositions.map((opt) => ({
  label: "FileNameModifier.PositionType." + FileNameModifierPosition[opt.value],
  value: opt.value,
}));

const DeleteOperationFields: React.FC<any> = ({ operation, t, onChange }) => {
  const handleChangeField = (key: string, value: any) =>
    onChange({ ...operation, [key]: value });

  const requirements = getFieldRequirements(operation);

  return (
    <>
      <NumberInput
        className="w-[120px]"
        isRequired={requirements.deleteCount}
        label={t<string>("FileNameModifier.Label.DeleteCount")}
        placeholder={t<string>("FileNameModifier.Placeholder.DeleteCount")}
        size="sm"
        value={operation.deleteCount}
        onValueChange={(e) => handleChangeField("deleteCount", e)}
      />
      <NumberInput
        className="w-[120px]"
        isRequired={requirements.deleteStartPosition}
        label={t<string>("FileNameModifier.Label.DeleteStartPosition")}
        placeholder={t<string>("FileNameModifier.Placeholder.DeleteStartPosition")}
        size="sm"
        value={operation.deleteStartPosition}
        onValueChange={(e) => handleChangeField("deleteStartPosition", e)}
      />
      <Input
        className="w-[180px]"
        isRequired={requirements.targetText}
        label={t<string>("FileNameModifier.Label.MatchText")}
        placeholder={t<string>("FileNameModifier.Placeholder.MatchText")}
        size="sm"
        value={operation.targetText || ""}
        onValueChange={(e) => handleChangeField("targetText", e)}
      />
      <Select
        className="w-[140px]"
        dataSource={PositionTypeOptions.map((opt) => ({
          label: t<string>(opt.label),
          value: opt.value,
        }))}
        disallowEmptySelection
        isRequired={requirements.position}
        label={t<string>("FileNameModifier.Label.PositionType")}
        placeholder={t<string>("FileNameModifier.Placeholder.PositionType")}
        selectedKeys={[operation.position?.toString() || ""]}
        size="sm"
        onSelectionChange={(keys) => {
          const key = parseInt(Array.from(keys)[0] as string);

          if (key !== operation.position) {
            handleChangeField("position", key);
          }
        }}
      />
      {operation.position === PositionType.AtPosition && (
        <NumberInput
          className="w-[120px]"
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

export default DeleteOperationFields;
