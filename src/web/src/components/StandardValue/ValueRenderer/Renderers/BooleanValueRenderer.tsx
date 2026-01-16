"use client";

import type { ValueRendererProps } from "../models";

import { useTranslation } from "react-i18next";

import { Checkbox, Switch, Chip } from "@/components/bakaui";

type BooleanValueRendererProps = Omit<
  ValueRendererProps<boolean>,
  "variant"
> & {
  variant: ValueRendererProps<boolean>["variant"] | "switch";
  size?: "sm" | "md" | "lg";
};
const BooleanValueRenderer = ({
  value,
  variant,
  editor,
  size,
  isReadonly: propsIsReadonly,
  ...props
}: BooleanValueRendererProps) => {
  const { t } = useTranslation();

  // Default isReadonly to true if no editor is provided
  const isReadonly = propsIsReadonly ?? !editor;

  // When not readonly, show Yes/No chips with a visual NotSet indicator at the beginning
  if (!isReadonly && editor) {
    const isNotSet = value === undefined;
    return (
      <div className="inline-flex gap-1 flex-wrap min-w-[138px]">
        {/* Fake NotSet indicator - visual only, helps user understand nothing is selected */}
        <Chip
          size={size}
          color={isNotSet ? "primary" : "default"}
          variant={isNotSet ? "solid" : "bordered"}
          className="flex-shrink-0"
        >
          {t("common.label.notSet")}
        </Chip>
        <Chip
          size={size}
          color={value === true ? "success" : "default"}
          variant={value === true ? "solid" : "bordered"}
          className="cursor-pointer flex-shrink-0"
          onClick={() => editor?.onValueChange?.(value === true ? undefined : true, value === true ? undefined : true)}
        >
          {t("common.label.yes")}
        </Chip>
        <Chip
          size={size}
          color={value === false ? "danger" : "default"}
          variant={value === false ? "solid" : "bordered"}
          className="cursor-pointer flex-shrink-0"
          onClick={() => editor?.onValueChange?.(value === false ? undefined : false, value === false ? undefined : false)}
        >
          {t("common.label.no")}
        </Chip>
      </div>
    );
  }

  const v = variant ?? "default";

  switch (v) {
    case "default":
    case "light":
      return (
        <Checkbox
          disableAnimation={!editor}
          isSelected={value}
          size={size}
          onValueChange={(v) => editor?.onValueChange?.(v, v)}
        />
      );
    case "switch":
      return (
        <Switch
          disableAnimation={!editor}
          isSelected={value}
          size={size}
          onValueChange={(v) => editor?.onValueChange?.(v, v)}
        />
      );
  }
};

BooleanValueRenderer.displayName = "BooleanValueRenderer";

export default BooleanValueRenderer;
