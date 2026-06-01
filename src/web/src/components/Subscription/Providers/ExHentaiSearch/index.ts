import type { SubscriptionProviderUI } from "../types";
import type { ExHentaiSearchTarget } from "./types";

import Form from "./Form";
import Summary from "./Summary";

import { ThirdPartyId } from "@/sdk/constants";

export const ExHentaiSearchUI: SubscriptionProviderUI<ExHentaiSearchTarget> = {
  kind: "exhentai.search",
  thirdPartyId: ThirdPartyId.ExHentai,
  defaultTarget: () => ({ url: "" }),
  parseTarget: (json: string) => {
    if (!json) return { url: "" };
    try {
      const parsed = JSON.parse(json) as Partial<ExHentaiSearchTarget>;
      return { url: parsed.url ?? "" };
    } catch {
      return { url: "" };
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
