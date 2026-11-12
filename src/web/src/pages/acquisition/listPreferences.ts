import envConfig from "@/config/env";
import { acquisitionStages, type AcquisitionStageFilter } from "@/components/Acquire/taskStage";
import { acquisitionFactFilters, normalizeAcquisitionFilters, type AcquisitionFilterSelection } from "./acquisitionFilters";

export const acquisitionSorts = ["newest", "lastProcessed", "sizeAsc", "sizeDesc"] as const;
export type AcquisitionListPreferences = AcquisitionFilterSelection & {
  stage: AcquisitionStageFilter;
  keyword: string;
  sort: (typeof acquisitionSorts)[number];
};
export const acquisitionListPreferencesKey = () => `bakabase:acquisition:list:${encodeURIComponent(
  `${typeof window === "undefined" ? "" : window.location.origin}${envConfig.apiEndpoint}`,
)}`;
const defaults: AcquisitionListPreferences = { stage: "all", keyword: "", sort: "newest", attentionTags: [], facts: [] };

export function readAcquisitionListPreferences(): AcquisitionListPreferences {
  try {
    const value = JSON.parse(localStorage.getItem(acquisitionListPreferencesKey()) || "null");
    if (value?.version !== 1) return { ...defaults };
    return {
      stage: ["all", ...acquisitionStages].includes(value.stage) ? value.stage : "all",
      keyword: typeof value.keyword === "string" ? value.keyword.slice(0, 2048) : "",
      sort: acquisitionSorts.includes(value.sort) ? value.sort : "newest",
      ...normalizeAcquisitionFilters({
        attentionTags: Array.isArray(value.attentionTags) ? value.attentionTags.filter(
          (tag: unknown) => typeof tag === "string" && /^[a-zA-Z][a-zA-Z0-9]{0,63}$/.test(tag),
        ).slice(0, 30) : [],
        facts: Array.isArray(value.facts) ? value.facts.filter((fact: unknown) => acquisitionFactFilters.includes(fact as any)) : [],
      }),
    };
  } catch { return { ...defaults }; }
}

export function writeAcquisitionListPreferences(value: AcquisitionListPreferences) {
  try {
    // Explicit fields only: never restore selections, pages or task deep links.
    localStorage.setItem(acquisitionListPreferencesKey(), JSON.stringify({ version: 1,
      stage: value.stage, keyword: value.keyword, sort: value.sort,
      ...normalizeAcquisitionFilters(value),
    }));
  } catch { /* Storage-disabled browsers keep the current session usable. */ }
}
