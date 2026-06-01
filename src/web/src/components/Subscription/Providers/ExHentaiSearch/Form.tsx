import React from "react";
import { useTranslation } from "react-i18next";

import type { SubscriptionProviderFormProps } from "../types";
import type { ExHentaiSearchTarget } from "./types";

import { Input } from "@/components/bakaui";

const ExHentaiSearchForm: React.FC<SubscriptionProviderFormProps<ExHentaiSearchTarget>> = ({
  value,
  onChange,
}) => {
  const { t } = useTranslation();

  return (
    <Input
      isRequired
      description={t<string>("subscription.provider.exhentai.search.url.description")}
      label={t<string>("subscription.provider.exhentai.search.url.label")}
      placeholder={t<string>("subscription.provider.exhentai.search.url.placeholder")}
      value={value.url}
      onValueChange={(url) => onChange({ ...value, url })}
    />
  );
};

export default ExHentaiSearchForm;
