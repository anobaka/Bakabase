import type { Dayjs } from "dayjs";
import type { Duration } from "dayjs/plugin/duration";
import type { LinkValue, TagValue } from "@/components/StandardValue/models";
import type { SearchFilter } from "@/components/ResourceFilter/models";

import { PropertyType } from "@/sdk/constants";
import { deserializeStandardValue } from "@/components/StandardValue/helpers";
import { getBizValueType } from "@/components/Property/PropertySystem";

/**
 * Renders a filter's biz value as a short piece of plain text.
 *
 * Shared by the auto-generated tab name and the tab's search-summary tooltip,
 * so both describe a filter the same way.
 */
export function filterValueToText(filter: SearchFilter): string {
  if (!filter.bizValue) return "";
  const type = filter.property?.type;

  if (type == undefined) return "";

  const bizType = getBizValueType(type);
  const bv = deserializeStandardValue(filter.bizValue, bizType);

  if (bv == null) return "";

  switch (type) {
    case PropertyType.SingleLineText:
    case PropertyType.MultilineText:
    case PropertyType.SingleChoice:
    case PropertyType.Formula:
      return String(bv);
    case PropertyType.MultipleChoice:
    case PropertyType.Attachment:
      return (bv as string[]).filter(Boolean).join(", ");
    case PropertyType.Number:
    case PropertyType.Rating:
      return String(bv);
    case PropertyType.Percentage:
      return `${bv}%`;
    case PropertyType.Boolean:
      return (bv as boolean) ? "✓" : "✗";
    case PropertyType.Link: {
      const link = bv as LinkValue;

      return link.text || link.url || "";
    }
    case PropertyType.Date:
      return (bv as Dayjs).format("YYYY-MM-DD");
    case PropertyType.DateTime:
      return (bv as Dayjs).format("YYYY-MM-DD HH:mm:ss");
    case PropertyType.Time:
      return (bv as Duration).format("HH:mm:ss");
    case PropertyType.Multilevel:
      return (bv as string[][]).map((path) => path.join("/")).join(", ");
    case PropertyType.Tags:
      return (bv as TagValue[])
        .map((t) => (t.group ? `${t.group}/${t.name}` : t.name))
        .filter(Boolean)
        .join(", ");
  }

  return "";
}
