"use client";

import React from "react";

import { Input, Checkbox } from "../../bakaui";

const ReplaceOperationFields: React.FC<any> = ({ operation, t, onChange }) => {
  const handleChangeField = (key: string, value: any) =>
    onChange({ ...operation, [key]: value });

  return (
    <>
      {[
        ["text", "Text"],
        ["targetText", "TargetText"],
      ].map(([key, label]) => (
        <Input
          key={key as string}
          className="w-[180px]"
          isRequired={!operation.text && !operation.targetText}
          label={t<string>(`FileNameModifier.Label.${label}`)}
          placeholder={t<string>(`FileNameModifier.Placeholder.${label}`)}
          size="sm"
          value={operation[key as keyof typeof operation] || ""}
          onValueChange={(e) => handleChangeField(key as string, e)}
        />
      ))}
      <div className="flex items-center gap-1">
        <Checkbox
          checked={operation.replaceEntire}
          onChange={(e) => handleChangeField("replaceEntire", e.target.checked)}
        />
        <span className="text-xs text-gray-500">
          {t<string>("FileNameModifier.ReplaceEntire")}
        </span>
      </div>
    </>
  );
};

export default ReplaceOperationFields;
