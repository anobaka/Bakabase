"use client";

import type { ValueRendererProps } from "../models";

import { useTranslation } from "react-i18next";

type FormulaValueRendererProps = Omit<ValueRendererProps<string>, "variant"> & {
  variant: ValueRendererProps<string>["variant"];
};

export default ({
  value,
  variant,
  editor,
  ...props
}: FormulaValueRendererProps) => {
  const { t } = useTranslation();

  const v = variant ?? "default";

  return <span>{t<string>("Not supported")}</span>;
};
