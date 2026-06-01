import React from "react";
import { useTranslation } from "react-i18next";

import type { SubscriptionProviderFormProps } from "../types";
import type { PixivFollowLatestMode, PixivFollowLatestTarget } from "./types";

import { Radio, RadioGroup } from "@/components/bakaui";

const PixivFollowLatestForm: React.FC<SubscriptionProviderFormProps<PixivFollowLatestTarget>> = ({
  value,
  onChange,
}) => {
  const { t } = useTranslation();

  // Two fixed options → RadioGroup scans faster than a Select for the user.
  return (
    <RadioGroup
      description={t<string>("subscription.provider.pixiv.followLatest.mode.description")}
      label={t<string>("subscription.provider.pixiv.followLatest.mode.label")}
      orientation="horizontal"
      value={value.mode}
      onValueChange={(v) => onChange({ ...value, mode: v as PixivFollowLatestMode })}
    >
      <Radio value="all">
        {t<string>("subscription.provider.pixiv.followLatest.mode.all")}
      </Radio>
      <Radio value="r18">
        {t<string>("subscription.provider.pixiv.followLatest.mode.r18")}
      </Radio>
    </RadioGroup>
  );
};

export default PixivFollowLatestForm;
