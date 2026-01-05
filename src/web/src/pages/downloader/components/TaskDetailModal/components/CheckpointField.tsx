"use client";

import { useTranslation } from "react-i18next";
import { Textarea } from "@heroui/react";

type Props = {
  checkpoint?: string;
  onChange: (checkpoint?: string) => void;
};
const CheckpointField = ({ checkpoint, onChange }: Props) => {
  const { t } = useTranslation();

  return (
    <Textarea
      description={
        <div>
          <div>{t<string>("downloader.tip.checkpointManual")}</div>
          <div>{t<string>("downloader.tip.checkpointAuto")}</div>
          <div>{t<string>("downloader.tip.checkpointFormat")}</div>
        </div>
      }
      label={t("common.label.checkpoint")}
      size={"sm"}
      value={checkpoint}
      onValueChange={onChange}
    />
  );
};

CheckpointField.displayName = "CheckpointField";

export default CheckpointField;
