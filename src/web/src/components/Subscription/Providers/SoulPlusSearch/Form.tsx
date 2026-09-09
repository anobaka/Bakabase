import type { SubscriptionProviderFormProps } from "../types";
import type { SoulPlusSearchTarget } from "./types";

import React from "react";
import { useTranslation } from "react-i18next";

import { Input, NumberInput } from "@/components/bakaui";

const SoulPlusSearchForm: React.FC<SubscriptionProviderFormProps<SoulPlusSearchTarget>> = ({
  value,
  onChange,
}) => {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col gap-3">
      <Input
        isRequired
        description={t<string>("subscription.provider.soulplus.search.url.description")}
        label={t<string>("subscription.provider.soulplus.search.url.label")}
        placeholder={t<string>("subscription.provider.soulplus.search.url.placeholder")}
        value={value.url}
        onValueChange={(url) => onChange({ ...value, url })}
      />
      <Input
        description={t<string>("subscription.provider.soulplus.search.keywords.description")}
        label={t<string>("subscription.provider.soulplus.search.keywords.label")}
        value={value.keywords.join(", ")}
        onValueChange={(raw) =>
          onChange({
            ...value,
            keywords: raw
              .split(/[,，]/)
              .map((k) => k.trim())
              .filter(Boolean),
          })
        }
      />
      <NumberInput
        className="w-48"
        description={t<string>("subscription.provider.soulplus.search.maxPages.description")}
        label={t<string>("subscription.provider.soulplus.search.maxPages.label")}
        maxValue={20}
        minValue={1}
        value={value.maxPages}
        onValueChange={(maxPages) => onChange({ ...value, maxPages: Number(maxPages ?? 1) })}
      />
    </div>
  );
};

export default SoulPlusSearchForm;
