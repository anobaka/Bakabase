import type { SubscriptionProviderUI } from "../types";
import type { VndbSeriesTarget } from "./types";

import Form from "./Form";
import Summary from "./Summary";

import { ThirdPartyId } from "@/sdk/constants";

const EMPTY: VndbSeriesTarget = {
  visualNovel: "",
  relations: [],
  officialOnly: true,
  includeSelf: true,
};

/**
 * A visual novel id, however the user gave it — the id, or the page they copied.
 *
 * Anchored on purpose: a bare "v1" reads as "volume 1" in half the file names in a library, so it
 * only counts as an id when the whole of what was typed is one.
 */
const visualNovelIdOf = (value: string): string | undefined =>
  /^(?:https?:\/\/vndb\.org\/)?(v\d+)\/?$/i.exec(value.trim())?.[1]?.toLowerCase();

export const VndbSeriesUI: SubscriptionProviderUI<VndbSeriesTarget> = {
  kind: "vndb.series",
  thirdPartyId: ThirdPartyId.Vndb,
  defaultTarget: () => ({ ...EMPTY }),
  parseTarget: (json: string) => {
    if (!json) return { ...EMPTY };
    try {
      const parsed = JSON.parse(json) as Partial<VndbSeriesTarget>;

      return {
        visualNovel: parsed.visualNovel ?? "",
        relations: parsed.relations ?? [],
        officialOnly: parsed.officialOnly ?? true,
        includeSelf: parsed.includeSelf ?? true,
      };
    } catch {
      return { ...EMPTY };
    }
  },
  isValid: (target) => !!visualNovelIdOf(target.visualNovel ?? ""),
  Form,
  Summary,
};
