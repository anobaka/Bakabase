"use client";

import React from "react";

import { Input, Checkbox } from "../../bakaui";
import { getFieldRequirements } from "../validation";

const ReplaceOperationFields: React.FC<any> = ({ operation, t, onChange }) => {
  const handleChangeField = (key: string, value: any) =>
    onChange({ ...operation, [key]: value });

  const requirements = getFieldRequirements(operation);

  const isReplaceEntire = operation.replaceEntire || false;

  return (
    <>
      <Input
        className="w-[180px]"
        isDisabled={isReplaceEntire}
        isRequired={!isReplaceEntire && requirements.targetText}
        label={t<string>("FileNameModifier.Label.TargetText")}
        placeholder={t<string>("FileNameModifier.Placeholder.TargetText")}
        size="sm"
        value={isReplaceEntire ? "" : (operation.targetText || "")}
        onValueChange={(e) => handleChangeField("targetText", e)}
      />
      <Input
        className="w-[180px]"
        isRequired={requirements.text}
        label={t<string>("FileNameModifier.Label.Text")}
        placeholder={t<string>("FileNameModifier.Placeholder.Text")}
        size="sm"
        value={operation.text || ""}
        onValueChange={(e) => handleChangeField("text", e)}
      />
      <div className="flex items-center gap-3">
        <div className="flex items-center gap-1">
          <Checkbox
            isDisabled={isReplaceEntire}
            checked={isReplaceEntire ? false : (operation.regex || false)}
            onChange={(e) => handleChangeField("regex", e.target.checked)}
          />
          <span className={`text-xs ${isReplaceEntire ? "text-gray-300" : "text-gray-500"}`}>
            {t<string>("FileNameModifier.UseRegex")}
          </span>
        </div>
        <div className="flex items-center gap-1">
          <Checkbox
            checked={isReplaceEntire}
            onChange={(e) => handleChangeField("replaceEntire", e.target.checked)}
          />
          <span className="text-xs text-gray-500">
            {t<string>("FileNameModifier.ReplaceEntire")}
          </span>
        </div>
      </div>
    </>
  );
};

export default ReplaceOperationFields;
