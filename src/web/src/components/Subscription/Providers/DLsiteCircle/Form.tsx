import type { SubscriptionProviderFormProps } from "../types";
import type { DLsiteCircleTarget } from "./types";

import React from "react";
import { useTranslation } from "react-i18next";

import { Input, NumberInput } from "@/components/bakaui";

const DLsiteCircleForm: React.FC<SubscriptionProviderFormProps<DLsiteCircleTarget>> = ({
  value,
  onChange,
}) => {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col gap-3">
      <Input
        isRequired
        description={t<string>("subscription.provider.dlsite.circle.url.description")}
        label={t<string>("subscription.provider.dlsite.circle.url.label")}
        placeholder="https://www.dlsite.com/maniax/circle/profile/=/maker_id/RG…"
        value={value.url}
        onValueChange={(url) => onChange({ ...value, url })}
      />
      <NumberInput
        className="w-48"
        description={t<string>("subscription.provider.dlsite.circle.maxPages.description")}
        label={t<string>("subscription.provider.dlsite.circle.maxPages.label")}
        maxValue={20}
        minValue={1}
        value={value.maxPages}
        onValueChange={(maxPages) => onChange({ ...value, maxPages: Number(maxPages ?? 1) })}
      />
    </div>
  );
};

export default DLsiteCircleForm;
