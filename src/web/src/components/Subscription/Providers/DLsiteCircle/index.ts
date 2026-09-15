import type { SubscriptionProviderUI } from "../types";
import type { DLsiteCircleTarget } from "./types";

import Form from "./Form";
import Summary from "./Summary";

import { ThirdPartyId } from "@/sdk/constants";

const EMPTY: DLsiteCircleTarget = { url: "", maxPages: 1 };

export const DLsiteCircleUI: SubscriptionProviderUI<DLsiteCircleTarget> = {
  kind: "dlsite.circle",
  thirdPartyId: ThirdPartyId.DLsite,
  defaultTarget: () => ({ ...EMPTY }),
  parseTarget: (json: string) => {
    if (!json) return { ...EMPTY };
    try {
      const parsed = JSON.parse(json) as Partial<DLsiteCircleTarget>;

      return { url: parsed.url ?? "", maxPages: parsed.maxPages ?? 1 };
    } catch {
      return { ...EMPTY };
    }
  },
  isValid: (target) => {
    if (!target.url) return false;
    try {
      const u = new URL(target.url);

      return (
        (u.protocol === "http:" || u.protocol === "https:") && u.hostname.endsWith("dlsite.com")
      );
    } catch {
      return false;
    }
  },
  Form,
  Summary,
};
