import type { SearchFilterGroup } from "@/components/ResourceFilter/models";
import type { SearchForm } from "@/pages/resource/models";

/** True when the group, or any group nested under it, holds a filter. */
export const groupHasContent = (group: SearchFilterGroup): boolean =>
  (group.filters?.length ?? 0) > 0 || (group.groups ?? []).some(groupHasContent);

/** True when the form carries anything worth summarizing in the tab tooltip. */
export const hasSearchSummary = (form: SearchForm | undefined): boolean => {
  if (!form) return false;

  return (
    !!form.keyword ||
    !!form.tags?.length ||
    !!form.orders?.length ||
    (!!form.group && groupHasContent(form.group))
  );
};
