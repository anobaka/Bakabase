import type { SubscriptionProviderUI } from "../types";
import type { PixivFollowLatestMode, PixivFollowLatestTarget } from "./types";

import Form from "./Form";
import Summary from "./Summary";

import { ThirdPartyId } from "@/sdk/constants";

const ALLOWED_MODES: ReadonlySet<PixivFollowLatestMode> = new Set(["all", "r18"]);

export const PixivFollowLatestUI: SubscriptionProviderUI<PixivFollowLatestTarget> = {
  kind: "pixiv.followLatest",
  thirdPartyId: ThirdPartyId.Pixiv,
  defaultTarget: () => ({ mode: "all" }),
  parseTarget: (json: string) => {
    if (!json) return { mode: "all" };
    try {
      const parsed = JSON.parse(json) as Partial<PixivFollowLatestTarget>;
      const mode = (parsed.mode ?? "all") as PixivFollowLatestMode;
      return { mode: ALLOWED_MODES.has(mode) ? mode : "all" };
    } catch {
      return { mode: "all" };
    }
  },
  isValid: (target) => ALLOWED_MODES.has(target.mode),
  Form,
  Summary,
};
