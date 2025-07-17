"use client";

import type { ValueRendererProps } from "../models";

import { useTranslation } from "react-i18next";

import { Checkbox, Switch } from "@/components/bakaui";

type BooleanValueRendererProps = Omit<
  ValueRendererProps<boolean>,
  "variant"
> & {
  variant: ValueRendererProps<boolean>["variant"] | "switch";
};

export default ({
  value,
  variant,
  editor,
  ...props
}: BooleanValueRendererProps) => {
  const { t } = useTranslation();

  const v = variant ?? "default";

  switch (v) {
    case "default":
    case "light":
      return (
        <Checkbox
          disableAnimation={!editor}
          isSelected={value}
          size={"sm"}
          onValueChange={(v) => editor?.onValueChange?.(v, v)}
        />
      );
    case "switch":
      return (
        <Switch
          disableAnimation={!editor}
          isSelected={value}
          size={"sm"}
          onValueChange={(v) => editor?.onValueChange?.(v, v)}
        />
      );
  }
};
