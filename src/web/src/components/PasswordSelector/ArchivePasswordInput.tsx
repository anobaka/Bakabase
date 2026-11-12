import { lazy, Suspense, useState } from "react";
import { KeyOutlined } from "@ant-design/icons";
import { useTranslation } from "react-i18next";

import { Button, Input } from "@/components/bakaui";

const PasswordSelector = lazy(() => import("./index"));

interface Props {
  label: string;
  value: string;
  onValueChange: (password: string) => void;
  isDisabled?: boolean;
  size?: "sm" | "md" | "lg";
}

export default function ArchivePasswordInput({
  label,
  value,
  onValueChange,
  isDisabled,
  size,
}: Props) {
  const { t } = useTranslation();
  const [choosing, setChoosing] = useState(false);

  return (
    <div className="grid min-w-0 grid-cols-[minmax(0,1fr)_auto] items-stretch gap-2">
      <Input
        className="min-w-0"
        label={label}
        value={value}
        onValueChange={onValueChange}
        size={size}
        type="text"
        autoComplete="off"
        autoCapitalize="none"
        autoCorrect="off"
        spellCheck="false"
        isDisabled={isDisabled}
      />
      <Button
        type="button"
        size="sm"
        className="h-auto min-w-0"
        variant="flat"
        isDisabled={isDisabled}
        startContent={<KeyOutlined />}
        onPress={() => setChoosing(true)}
      >
        {t("passwordSelector.choose")}
      </Button>
      {choosing && !isDisabled && (
        <Suspense fallback={null}>
          <PasswordSelector
            selectionOnly
            onSelect={onValueChange}
            onDestroyed={() => setChoosing(false)}
          />
        </Suspense>
      )}
    </div>
  );
}
