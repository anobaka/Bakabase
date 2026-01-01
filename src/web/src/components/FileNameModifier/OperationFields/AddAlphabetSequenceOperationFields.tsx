"use client";

import React from "react";

import { Input, NumberInput } from "../../bakaui";

import { FileNameModifierPosition } from "@/sdk/constants";
import { getFieldRequirements } from "../validation";

const PositionType = FileNameModifierPosition;

const AddAlphabetSequenceOperationFields: React.FC<any> = ({
  operation,
  t,
  onChange,
}) => {
  const requirements = getFieldRequirements(operation);

  return (
    <>
      <Input
        className="w-[100px]"
        isRequired={requirements.alphabetStartChar}
        label={t<string>("FileNameModifier.Label.StartChar")}
        maxLength={1}
        placeholder={t<string>("FileNameModifier.Placeholder.StartChar")}
        size="sm"
        value={operation.alphabetStartChar}
        onValueChange={(e) => onChange({ ...operation, alphabetStartChar: e })}
      />
      <NumberInput
        className="w-[100px]"
        isRequired={requirements.alphabetCount}
        label={t<string>("FileNameModifier.Label.Count")}
        placeholder={t<string>("FileNameModifier.Placeholder.Count")}
        size="sm"
        value={operation.alphabetCount}
        onValueChange={(e) => onChange({ ...operation, alphabetCount: e })}
      />
      {operation.position === PositionType.AtPosition && (
        <NumberInput
          className="w-[120px]"
          isRequired={requirements.positionIndex}
          label={t<string>("FileNameModifier.Label.PositionIndex")}
          placeholder={t<string>("FileNameModifier.Placeholder.PositionIndex")}
          size="sm"
          value={operation.positionIndex}
          onValueChange={(e) => onChange({ ...operation, positionIndex: e })}
        />
      )}
    </>
  );
};

export default AddAlphabetSequenceOperationFields;
