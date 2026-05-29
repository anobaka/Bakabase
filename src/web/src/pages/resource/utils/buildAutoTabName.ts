import type { Dayjs } from "dayjs";
import type { Duration } from "dayjs/plugin/duration";
import type { LinkValue, TagValue } from "@/components/StandardValue/models";
import type { SearchFilter, SearchFilterGroup } from "@/components/ResourceFilter/models";
import type { SearchForm } from "@/pages/resource/models";

import { PropertyType } from "@/sdk/constants";
import { deserializeStandardValue } from "@/components/StandardValue/helpers";
import { getBizValueType } from "@/components/Property/PropertySystem";

function filterValueToText(filter: SearchFilter): string {
  if (filter.disabled) return "";
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

function collectFromGroup(group: SearchFilterGroup, out: string[]): void {
  if (group.disabled) return;
  for (const f of group.filters ?? []) {
    const text = filterValueToText(f);

    if (text) out.push(text);
  }
  for (const g of group.groups ?? []) {
    collectFromGroup(g, out);
  }
}

export function buildAutoTabName(form: SearchForm | undefined): string {
  if (!form?.group) return "";
  const texts: string[] = [];

  collectFromGroup(form.group, texts);

  return texts.join(", ");
}
