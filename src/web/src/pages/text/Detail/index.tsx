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
          placeholder={t<string>("Text")}
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
            placeholder={t<string>("Source text")}
            value={value.value1}
            onValueChange={(value1) => {
              change({ value1 });
            }}
          />
          <Input
            key="1"
            required
            placeholder={t<string>("Convert to")}
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
            placeholder={t<string>("Left wrapper")}
            value={value.value1}
            onValueChange={(value1) => {
              change({ value1 });
            }}
          />
          <Input
            key="1"
            required
            placeholder={t<string>("Right wrapper")}
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
