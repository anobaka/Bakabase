import type { TFunction } from "i18next";
import type { DataSyncDisplayValue } from "../api";

import { useTranslation } from "react-i18next";

/*
 * A value the way people read it: an option as a chip in its colour, a tag with its group, a
 * multilevel option with its path, a flag as yes or no. Ids never appear (spec §11.3).
 */

/** A colour the server sent, if it is one a style can take as it is. */
const safeColor = (color?: string | null) =>
  color && /^(#[0-9a-f]{3,8}|rgba?\([\d\s.,%]+\)|hsla?\([\d\s.,%deg]+\))$/i.test(color.trim())
    ? color.trim()
    : undefined;

/** The value's words, as one string: for accessible names and plain lines. */
export const displayText = (t: TFunction, value?: DataSyncDisplayValue | null): string => {
  if (!value) return t("dataSync.value.none");
  if (value.path?.length) return value.path.join(" / ");
  if (value.text != null && value.text !== "")
    return value.group ? `${value.group}: ${value.text}` : value.text;
  if (value.flag != null) return t(value.flag ? "dataSync.value.yes" : "dataSync.value.no");
  if (value.number != null) return String(value.number);
  if (value.color) return value.color;

  return t("dataSync.value.empty");
};

export default function DisplayValue({
  value,
  faint = false,
}: {
  value?: DataSyncDisplayValue | null;
  /** The last agreed value, drawn faint above the two sides. */
  faint?: boolean;
}) {
  const { t } = useTranslation();
  const color = safeColor(value?.color);
  const text = displayText(t, value);
  const chip = !!value && (!!color || value.group != null || !!value.path?.length);

  return (
    <span
      className={`inline-flex max-w-full items-center gap-1.5 ${
        chip ? "rounded-md border border-default-200 px-1.5 py-0.5" : ""
      } ${faint ? "opacity-60" : ""} ${value ? "" : "italic text-default-400"}`}
      data-testid="data-sync-value"
    >
      {color && (
        <span
          aria-hidden
          className="inline-block h-2.5 w-2.5 shrink-0 rounded-full border border-default-300"
          data-color={color}
          style={{ backgroundColor: color }}
        />
      )}
      <span className="min-w-0 break-words [overflow-wrap:anywhere]">{text}</span>
    </span>
  );
}
