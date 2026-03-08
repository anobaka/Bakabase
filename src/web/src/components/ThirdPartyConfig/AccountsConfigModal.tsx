"use client";

import { useState, useEffect } from "react";
import { useTranslation } from "react-i18next";
import {
  Modal,
  ModalContent,
  ModalHeader,
  ModalBody,
  ModalFooter,
  Button,
  Input,
  Textarea,
  Chip,
  Divider,
} from "@heroui/react";
import { AiOutlinePlus, AiOutlineDelete } from "react-icons/ai";

export interface AccountField {
  key: string;
  label: string;
  placeholder?: string;
  type?: "text" | "password" | "textarea";
}

interface Account {
  name?: string;
  [key: string]: any;
}

interface AccountsConfigModalProps {
  isOpen: boolean;
  onClose: () => void;
  platform: string;
  accounts: Account[];
  fields: AccountField[];
  onSave: (accounts: Account[]) => Promise<void>;
}

export default function AccountsConfigModal({
  isOpen,
  onClose,
  platform,
  accounts: initialAccounts,
  fields,
  onSave,
}: AccountsConfigModalProps) {
  const { t } = useTranslation();
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (isOpen) {
      setAccounts(
        initialAccounts.length > 0
          ? initialAccounts.map((a) => ({ ...a }))
          : [],
      );
    }
  }, [isOpen, initialAccounts]);

  const addAccount = () => {
    const newAccount: Account = { name: "" };
    for (const field of fields) {
      newAccount[field.key] = "";
    }
    setAccounts([...accounts, newAccount]);
  };

  const removeAccount = (index: number) => {
    setAccounts(accounts.filter((_, i) => i !== index));
  };

  const updateAccount = (index: number, key: string, value: string) => {
    const updated = [...accounts];
    updated[index] = { ...updated[index], [key]: value };
    setAccounts(updated);
  };

  const handleSave = async () => {
    setSaving(true);
    try {
      await onSave(accounts);
      onClose();
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      isOpen={isOpen}
      scrollBehavior="inside"
      size="2xl"
      onClose={onClose}
    >
      <ModalContent>
        <ModalHeader>
          {t("resourceSource.accounts.title", { platform })}
        </ModalHeader>
        <ModalBody>
          {accounts.length > 0 && (
            <p className="text-xs text-default-400 mb-2">
              {t("resourceSource.accounts.defaultTip")}
            </p>
          )}

          {accounts.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-8 text-default-400">
              <p>{t("resourceSource.accounts.empty")}</p>
            </div>
          ) : (
            <div className="space-y-4">
              {accounts.map((account, index) => (
                <div
                  key={index}
                  className="border-small border-default-200 rounded-lg p-3 space-y-3"
                >
                  <div className="flex items-center justify-between">
                    <div className="flex items-center gap-2">
                      {index === 0 && (
                        <Chip color="primary" size="sm" variant="flat">
                          {t("resourceSource.accounts.default")}
                        </Chip>
                      )}
                      <span className="text-sm text-default-500">
                        #{index + 1}
                      </span>
                    </div>
                    <Button
                      color="danger"
                      isIconOnly
                      size="sm"
                      variant="light"
                      onPress={() => removeAccount(index)}
                    >
                      <AiOutlineDelete />
                    </Button>
                  </div>

                  <Input
                    label={t("resourceSource.accounts.name")}
                    placeholder={t("resourceSource.accounts.namePlaceholder")}
                    size="sm"
                    value={account.name || ""}
                    onValueChange={(v) => updateAccount(index, "name", v)}
                  />

                  {fields.map((field) =>
                    field.type === "textarea" ? (
                      <Textarea
                        key={field.key}
                        label={field.label}
                        maxRows={3}
                        placeholder={field.placeholder}
                        size="sm"
                        value={account[field.key] || ""}
                        onValueChange={(v) =>
                          updateAccount(index, field.key, v)
                        }
                      />
                    ) : (
                      <Input
                        key={field.key}
                        label={field.label}
                        placeholder={field.placeholder}
                        size="sm"
                        type={field.type === "password" ? "password" : "text"}
                        value={account[field.key] || ""}
                        onValueChange={(v) =>
                          updateAccount(index, field.key, v)
                        }
                      />
                    ),
                  )}

                  {index < accounts.length - 1 && <Divider />}
                </div>
              ))}
            </div>
          )}
        </ModalBody>
        <ModalFooter>
          <Button
            size="sm"
            startContent={<AiOutlinePlus />}
            variant="flat"
            onPress={addAccount}
          >
            {t("resourceSource.accounts.add")}
          </Button>
          <div className="flex-1" />
          <Button size="sm" variant="flat" onPress={onClose}>
            {t("common.action.cancel")}
          </Button>
          <Button
            color="primary"
            isLoading={saving}
            size="sm"
            onPress={handleSave}
          >
            {t("thirdPartyConfig.action.save")}
          </Button>
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}
