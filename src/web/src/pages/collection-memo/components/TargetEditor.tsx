import type { DestroyableProps } from "@/components/bakaui/types";
import type { CollectionMemoTarget } from "../helpers";

import { useState } from "react";
import { useTranslation } from "react-i18next";

import { Input, Modal } from "@/components/bakaui";

interface Props extends DestroyableProps {
  target?: CollectionMemoTarget;
  onSave: (name: string) => Promise<void>;
}

const TargetEditor = ({ target, onSave, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const [name, setName] = useState(target?.name ?? "");
  const [error, setError] = useState<string>();

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["ok", "cancel"],
        okProps: { children: t<string>("common.action.save") },
        cancelProps: { children: t<string>("common.action.cancel") },
      }}
      title={t<string>(
        target ? "collectionMemo.action.editTarget" : "collectionMemo.action.addTarget",
      )}
      onDestroyed={onDestroyed}
      onOk={async () => {
        const trimmed = name.trim();

        if (!trimmed || trimmed.length > 200) {
          const message = t<string>("collectionMemo.validation.name");

          setError(message);
          throw new Error(message);
        }

        setError(undefined);
        try {
          await onSave(trimmed);
        } catch (cause) {
          setError(t<string>("collectionMemo.error.save"));
          throw cause;
        }
      }}
    >
      <Input
        isRequired
        errorMessage={error}
        isInvalid={!!error}
        label={t<string>("collectionMemo.target.label")}
        maxLength={200}
        placeholder={t<string>("collectionMemo.target.placeholder")}
        value={name}
        onValueChange={setName}
      />
    </Modal>
  );
};

export default TargetEditor;
