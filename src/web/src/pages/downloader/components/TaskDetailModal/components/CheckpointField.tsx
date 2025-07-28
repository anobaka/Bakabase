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
          <div>
            {t<string>(
              "You can set the previous downloading checkpoint manually to make the downloader start the downloading task from it.",
            )}
          </div>
          <div>
            {t<string>(
              "In most cases, you should let this field set by downloader automatically.",
            )}
          </div>
          <div>
            {t<string>(
              "Each downloader has its own checkpoint format, and invalid checkpoint data will be ignored. You can find samples on our online document.",
            )}
          </div>
        </div>
      }
      label={t("Checkpoint")}
      size={"sm"}
      value={checkpoint}
      onValueChange={onChange}
    />
  );
};

CheckpointField.displayName = "CheckpointField";

export default CheckpointField;
