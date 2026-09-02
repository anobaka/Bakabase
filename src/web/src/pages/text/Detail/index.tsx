"use client";

import type { TextEntry } from "@/pages/text/models";

import { useTranslation } from "react-i18next";
import React, { useState } from "react";

import { Input } from "@/components/bakaui";
import { TextTypeShape } from "@/sdk/constants";

interface Props {
  value: TextEntry;
  shape: TextTypeShape;
  onChange: (value: TextEntry) => any;
}

/**
 * Which inputs an entry needs follows the type's shape, not the type itself — so a user-defined
 * type gets the same editor as the builtin with the same shape.
 */
const DetailPage = ({ value: propsValue, shape, onChange }: Props) => {
  const { t } = useTranslation();
  const [value, setValue] = useState<TextEntry>(JSON.parse(JSON.stringify(propsValue)));

  const change = (patches: Partial<TextEntry>) => {
    const nv = { ...value, ...patches };

    setValue(nv);
    onChange(nv);
  };

  const firstLabels: Record<TextTypeShape, { label: string; placeholder: string }> = {
    [TextTypeShape.Values]: { label: "text.label.text", placeholder: "text.placeholder.text" },
    [TextTypeShape.DelimiterPair]: {
      label: "text.label.leftWrapper",
      placeholder: "text.placeholder.leftWrapper",
    },
    [TextTypeShape.MappingPair]: {
      label: "text.label.sourceText",
      placeholder: "text.placeholder.sourceText",
    },
  };

  const secondLabels: Partial<Record<TextTypeShape, { label: string; placeholder: string }>> = {
    [TextTypeShape.DelimiterPair]: {
      label: "text.label.rightWrapper",
      placeholder: "text.placeholder.rightWrapper",
    },
    [TextTypeShape.MappingPair]: {
      label: "text.label.convertTo",
      placeholder: "text.placeholder.convertTo",
    },
  };

  const first = firstLabels[shape];
  const second = secondLabels[shape];

  return (
    <div className={"flex flex-col gap-2 w-full"}>
      <Input
        key="0"
        required
        className={"w-full"}
        label={t<string>(first.label)}
        placeholder={t<string>(first.placeholder)}
        value={value.value1}
        onValueChange={(value1) => change({ value1 })}
      />
      {second && (
        <Input
          key="1"
          required
          className={"w-full"}
          label={t<string>(second.label)}
          placeholder={t<string>(second.placeholder)}
          value={value.value2}
          onValueChange={(value2) => change({ value2 })}
        />
      )}
    </div>
  );
};

DetailPage.displayName = "DetailPage";

export default DetailPage;
