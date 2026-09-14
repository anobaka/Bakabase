"use client";

import type { Operation, OperationFieldsProps } from "./types";

import React from "react";

import { Input, Checkbox } from "../../bakaui";
import { getFieldRequirements } from "../validation";

const ReplaceOperationFields: React.FC<OperationFieldsProps> = ({ operation, t, onChange }) => {
  const handleChangeField = <K extends keyof Operation>(key: K, value: Operation[K]) =>
    onChange({ ...operation, [key]: value });

  const requirements = getFieldRequirements(operation);

  const isReplaceEntire = operation.replaceEntire || false;

  return (
    <>
      <Input
        className="w-full min-w-0"
        isDisabled={isReplaceEntire}
        isRequired={!isReplaceEntire && requirements.targetText}
        label={t<string>("FileNameModifier.Label.TargetText")}
        placeholder={t<string>("FileNameModifier.Placeholder.TargetText")}
        size="sm"
        value={isReplaceEntire ? "" : operation.targetText || ""}
        onValueChange={(e) => handleChangeField("targetText", e)}
      />
      <Input
        className="w-full min-w-0"
        isRequired={requirements.text}
        label={t<string>("FileNameModifier.Label.Text")}
        placeholder={t<string>("FileNameModifier.Placeholder.Text")}
        size="sm"
        value={operation.text || ""}
        onValueChange={(e) => handleChangeField("text", e)}
      />
      <div className="col-span-2 flex flex-wrap items-center gap-3">
        <Checkbox
          isDisabled={isReplaceEntire}
          isSelected={isReplaceEntire ? false : operation.regex || false}
          size="sm"
          onValueChange={(value) => handleChangeField("regex", value)}
        >
          {t<string>("FileNameModifier.UseRegex")}
        </Checkbox>
        <Checkbox
          isSelected={isReplaceEntire}
          size="sm"
          onValueChange={(value) => handleChangeField("replaceEntire", value)}
        >
          {t<string>("FileNameModifier.ReplaceEntire")}
        </Checkbox>
      </div>
    </>
  );
};

export default ReplaceOperationFields;
