"use client";

import { useTranslation } from "react-i18next";
import { CheckboxGroup } from "@heroui/react";

import { Checkbox } from "@/components/bakaui";

type Props = {
  preferTorrent: boolean;
  onChange: (preferTorrent: boolean) => void;
};
const PreferTorrentField = ({ preferTorrent, onChange }: Props) => {
  const { t } = useTranslation();

  return (
    <CheckboxGroup
      description={t<string>("downloader.tip.preferTorrentDesc")}
      label={t<string>("downloader.label.preferTorrent")}
      orientation="horizontal"
      size="sm"
      value={preferTorrent ? ["yes"] : []}
      onValueChange={(v) => onChange(v.includes("yes"))}
    >
      <Checkbox value="yes">
        {t("common.label.yes")}
      </Checkbox>
    </CheckboxGroup>
  );
};

PreferTorrentField.displayName = "PreferTorrentField";

export default PreferTorrentField;
