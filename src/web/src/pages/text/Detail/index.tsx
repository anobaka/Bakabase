"use client";

import type { SpecialText } from "@/pages/text/models";

import { useTranslation } from "react-i18next";
import React, { useState } from "react";

import { Input } from "@/components/bakaui";
import { SpecialTextType } from "@/sdk/constants";

interface Props {
  value: SpecialText;
  onChange: (value: SpecialText) => any;
}
const DetailPage = ({ value: propsValue, onChange }: Props) => {
  const { t } = useTranslation();
  const [value, setValue] = useState<SpecialText>(
    JSON.parse(JSON.stringify(propsValue)),
  );

  const change = (patches: Partial<SpecialText>) => {
    const nv = {
      ...value,
      ...patches,
    };

    setValue(nv);
    onChange(nv);
  };

  switch (value.type) {
    case SpecialTextType.DateTime:
    case SpecialTextType.Volume:
    case SpecialTextType.Useless:
    case SpecialTextType.Trim:
      return (
        <Input
          key="0"
          required
          className={"w-full"}
          label={t<string>("text.label.text")}
          placeholder={t<string>("text.placeholder.text")}
          value={value.value1}
          onValueChange={(value1) => {
            change({ value1 });
          }}
        />
      );
    case SpecialTextType.Language:
    case SpecialTextType.Standardization:
      return (
        <>
          <Input
            key="0"
            required
            label={t<string>("text.label.sourceText")}
            placeholder={t<string>("text.placeholder.sourceText")}
            value={value.value1}
            onValueChange={(value1) => {
              change({ value1 });
            }}
          />
          <Input
            key="1"
            required
            label={t<string>("text.label.convertTo")}
            placeholder={t<string>("text.placeholder.convertTo")}
            value={value.value2}
            onValueChange={(value2) => {
              change({ value2 });
            }}
          />
        </>
      );
    case SpecialTextType.Wrapper:
      return (
        <>
          <Input
            key="0"
            required
            label={t<string>("text.label.leftWrapper")}
            placeholder={t<string>("text.placeholder.leftWrapper")}
            value={value.value1}
            onValueChange={(value1) => {
              change({ value1 });
            }}
          />
          <Input
            key="1"
            required
            label={t<string>("text.label.rightWrapper")}
            placeholder={t<string>("text.placeholder.rightWrapper")}
            value={value.value2}
            onValueChange={(value2) => {
              change({ value2 });
            }}
          />
        </>
      );
    default:
      return null;
  }
};

DetailPage.displayName = "DetailPage";

export default DetailPage;
