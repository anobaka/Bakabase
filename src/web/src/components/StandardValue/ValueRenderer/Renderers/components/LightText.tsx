"use client";

import { useTranslation } from "react-i18next";

// Map size prop to text class matching HeroUI component sizes
export const sizeTextClassMap: Record<"sm" | "md" | "lg", string> = {
  sm: "text-small",
  md: "text-medium",
  lg: "text-large",
};

type LightTextProps = {
  children?: React.ReactNode;
  onClick?: () => any;
  size?: "sm" | "md" | "lg";
  /** Apply faded style (opacity-40) for "not set" state */
  faded?: boolean;
  /** Custom text color */
  color?: string;
};

/**
 * Text component for light variant rendering with consistent size styling.
 * Can be used for both normal text and "not set" indicators.
 */
export const LightText = (props: LightTextProps) => {
  const { children, onClick, size, faded, color } = props;

  const textClass = size ? sizeTextClassMap[size] : sizeTextClassMap.md;
  const className = [
    textClass,
    faded ? "opacity-40" : "",
    onClick ? "cursor-pointer" : "",
  ].filter(Boolean).join(" ");

  const style = color ? { color } : undefined;

  return (
    <span className={className} style={style} onClick={onClick}>
      {children}
    </span>
  );
};

LightText.displayName = "LightText";

type NotSetProps = {
  onClick?: () => any;
  size?: "sm" | "md" | "lg";
};

/**
 * "Not set" indicator using LightText with faded style.
 */
const NotSet = (props: NotSetProps) => {
  const { t } = useTranslation();
  const { onClick, size } = props;

  return (
    <LightText faded onClick={onClick} size={size}>
      {onClick ? t<string>("common.label.clickToSet") : t<string>("common.label.notSet")}
    </LightText>
  );
};

NotSet.displayName = "NotSet";

export default NotSet;
