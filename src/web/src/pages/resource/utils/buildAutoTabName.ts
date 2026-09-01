import type { SearchFilterGroup } from "@/components/ResourceFilter/models";
import type { SearchForm } from "@/pages/resource/models";

import { filterValueToText } from "./filterValueToText";

function collectFromGroup(group: SearchFilterGroup, out: string[]): void {
  if (group.disabled) return;
  for (const f of group.filters ?? []) {
    if (f.disabled) continue;
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
