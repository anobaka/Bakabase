import type { SubscriptionProviderUI } from "../types";

import React from "react";
import { useTranslation } from "react-i18next";

import { ThirdPartyId } from "@/sdk/constants";

/** These take no configuration: the account lives in the platform's own settings. */
interface EmptyTarget {}

const NoConfiguration: React.FC<{ hint: string }> = ({ hint }) => {
  const { t } = useTranslation();

  return <span className="text-xs text-default-500">{t<string>(hint)}</span>;
};

/**
 * A platform-holding source: no target to fill in, because "everything I own here" is the whole
 * of it. Asking for the account again would be a second place for it to be wrong.
 */
const holdingProvider = (
  kind: string,
  thirdPartyId: ThirdPartyId,
  hint: string,
): SubscriptionProviderUI<EmptyTarget> => ({
  kind,
  thirdPartyId,
  defaultTarget: () => ({}),
  parseTarget: () => ({}),
  isValid: () => true,
  Form: () => <NoConfiguration hint={hint} />,
  Summary: () => null,
});

export const DLsitePurchasesUI = holdingProvider(
  "dlsite.purchases",
  ThirdPartyId.DLsite,
  "subscription.provider.platformHolding.hint",
);

export const SteamOwnedGamesUI = holdingProvider(
  "steam.ownedGames",
  ThirdPartyId.Steam,
  "subscription.provider.platformHolding.hint",
);

export const ExHentaiFavoritesUI = holdingProvider(
  "exhentai.favorites",
  ThirdPartyId.ExHentai,
  "subscription.provider.platformHolding.hint",
);
