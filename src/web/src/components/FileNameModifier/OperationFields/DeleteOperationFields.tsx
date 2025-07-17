"use client";

import React from "react";

import { Input, NumberInput, Select } from "../../bakaui";

import {
  FileNameModifierPosition,
  fileNameModifierPositions,
} from "@/sdk/constants";
const PositionType = FileNameModifierPosition;
const PositionTypeOptions = fileNameModifierPositions.map((opt) => ({
  label: "FileNameModifier.PositionType." + FileNameModifierPosition[opt.value],
  value: opt.value,
}));

const DeleteOperationFields: React.FC<any> = ({ operation, t, onChange }) => {
  const handleChangeField = (key: string, value: any) =>
    onChange({ ...operation, [key]: value });

  return (
    <>
      {[
        ["deleteCount", "DeleteCount", 120],
        ["deleteStartPosition", "DeleteStartPosition", 120],
      ].map(([key, label, w]) => (
        <NumberInput
          key={key as string}
          className={`w-[${w}px]`}
          isRequired={operation[key as keyof typeof operation] == null}
          label={t<string>(`FileNameModifier.Label.${label}`)}
          placeholder={t<string>(`FileNameModifier.Placeholder.${label}`)}
          size="sm"
          value={operation[key as keyof typeof operation]}
          onValueChange={(e) => handleChangeField(key as string, e)}
        />
      ))}
      <Input
        className="w-[180px]"
        isRequired={false}
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
        isRequired={!operation.position}
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
          isRequired={true}
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
