import type { SubscriptionProviderUI } from "../types";
import type { SoulPlusSearchTarget } from "./types";

import Form from "./Form";
import Summary from "./Summary";

import { ThirdPartyId } from "@/sdk/constants";

const EMPTY: SoulPlusSearchTarget = { url: "", keywords: [], maxPages: 1 };

export const SoulPlusSearchUI: SubscriptionProviderUI<SoulPlusSearchTarget> = {
  kind: "soulplus.search",
  thirdPartyId: ThirdPartyId.SoulPlus,
  defaultTarget: () => ({ ...EMPTY }),
  parseTarget: (json: string) => {
    if (!json) return { ...EMPTY };
    try {
      const parsed = JSON.parse(json) as Partial<SoulPlusSearchTarget>;

      return {
        url: parsed.url ?? "",
        keywords: parsed.keywords ?? [],
        maxPages: parsed.maxPages ?? 1,
      };
    } catch {
      return { ...EMPTY };
    }
  },
  isValid: (target) => {
    if (!target.url) return false;
    try {
      const u = new URL(target.url);

      return u.protocol === "http:" || u.protocol === "https:";
    } catch {
      return false;
    }
  },
  Form,
  Summary,
};
