import React from "react";
import { useTranslation } from "react-i18next";

import type { SubscriptionProviderFormProps } from "../types";
import type { ExHentaiGalleryTarget } from "./types";

import { Input } from "@/components/bakaui";

const ExHentaiGalleryForm: React.FC<SubscriptionProviderFormProps<ExHentaiGalleryTarget>> = ({
  value,
  onChange,
}) => {
  const { t } = useTranslation();

  return (
    <Input
      isRequired
      description={t<string>("subscription.provider.exhentai.gallery.url.description")}
      label={t<string>("subscription.provider.exhentai.gallery.url.label")}
      placeholder={t<string>("subscription.provider.exhentai.gallery.url.placeholder")}
      value={value.url}
      onValueChange={(url) => onChange({ ...value, url })}
    />
  );
};

export default ExHentaiGalleryForm;
