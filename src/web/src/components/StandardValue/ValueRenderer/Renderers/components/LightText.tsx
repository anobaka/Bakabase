"use client";

import { useTranslation } from "react-i18next";
import { MdEdit } from "react-icons/md";

// Map size prop to text class matching HeroUI component sizes
export const sizeTextClassMap: Record<"sm" | "md" | "lg", string> = {
  sm: "text-small",
  md: "text-medium",
  lg: "text-large",
};

const editIconSizeMap: Record<"sm" | "md" | "lg", number> = {
  sm: 12,
  md: 14,
  lg: 16,
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
  const className = [textClass, faded ? "opacity-40" : "", onClick ? "cursor-pointer" : ""]
    .filter(Boolean)
    .join(" ");

  const style = color ? { color } : undefined;

  return (
    <span
      className={className}
      role={onClick ? "button" : undefined}
      style={style}
      tabIndex={onClick ? 0 : undefined}
      onClick={onClick}
      onKeyDown={
        onClick
          ? (e) => {
              if (e.key === "Enter" || e.key === " ") {
                e.preventDefault();
                onClick();
              }
            }
          : undefined
      }
    >
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
 * Empty-value indicator.
 *
 * Read-only (no onClick): "Not set" text in muted style — explicit informational.
 * Editable (onClick): a small edit icon, dimmed by default and brightened when the
 * surrounding `.group` container is hovered. Replaces the previous "Click to set"
 * text so a screen full of empty properties no longer reads like a wall of labels.
 */
const NotSet = (props: NotSetProps) => {
  const { t } = useTranslation();
  const { onClick, size } = props;

  if (!onClick) {
    return (
      <LightText faded size={size}>
        {t<string>("common.label.notSet")}
      </LightText>
    );
  }

  return (
    <button
      aria-label={t<string>("common.label.clickToSet")}
      className="inline-flex items-center justify-center leading-none p-0 m-0 cursor-pointer outline-none opacity-30 hover:opacity-100 group-hover:opacity-100 focus-visible:opacity-100 transition-opacity"
      title={t<string>("common.label.clickToSet")}
      type="button"
      onClick={onClick}
    >
      <MdEdit size={editIconSizeMap[size ?? "md"]} />
    </button>
  );
};

NotSet.displayName = "NotSet";

export default NotSet;
