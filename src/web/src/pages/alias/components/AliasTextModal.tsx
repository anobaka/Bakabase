import type { DestroyableProps } from "@/components/bakaui/types";

import { useState } from "react";
import { useTranslation } from "react-i18next";

import { Input, Modal } from "@/components/bakaui";

type Props = DestroyableProps & {
  title: string;
  description?: string;
  initialValue?: string;
  onSubmit: (text: string) => Promise<void>;
};

export default function AliasTextModal({
  title,
  description,
  initialValue = "",
  onSubmit,
  onDestroyed,
}: Props) {
  const { t } = useTranslation();
  const [value, setValue] = useState(initialValue);

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["ok", "cancel"],
        okProps: { children: t("common.action.save"), isDisabled: !value.trim() },
      }}
      size="sm"
      title={title}
      onDestroyed={onDestroyed}
      onOk={() => onSubmit(value.trim())}
    >
      {description && <p className="text-sm leading-relaxed text-default-500">{description}</p>}
      <Input
        aria-label={t("alias.input.textLabel")}
        placeholder={t("alias.input.textPlaceholder")}
        value={value}
        onValueChange={setValue}
      />
    </Modal>
  );
}
