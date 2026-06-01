import React from "react";
import { useTranslation } from "react-i18next";

import type { SubscriptionProviderSummaryProps } from "../types";
import type { PixivFollowLatestTarget } from "./types";

const PixivFollowLatestSummary: React.FC<
  SubscriptionProviderSummaryProps<PixivFollowLatestTarget>
> = ({ target }) => {
  const { t } = useTranslation();
  const label = t<string>(`subscription.provider.pixiv.followLatest.mode.${target.mode}`);

  return <span className="text-default-500 text-xs">{label}</span>;
};

export default PixivFollowLatestSummary;
