"use client";

import { useState, useEffect } from "react";
import { useTranslation } from "react-i18next";
import {
  Button,
  Input,
  Textarea,
  Chip,
  Divider,
} from "@heroui/react";
import { AiOutlinePlus, AiOutlineDelete, AiOutlineCheck, AiOutlineClose } from "react-icons/ai";
import type { CookieValidatorTarget } from "@/sdk/constants";
import { RuntimeMode } from "@/sdk/constants";
import { useAppContextStore } from "@/stores/appContext";
import BApi from "@/sdk/BApi";

export interface AccountField {
  key: string;
  label: string;
  placeholder?: string;
  type?: "text" | "password" | "textarea";
  cookieValidatorTarget?: CookieValidatorTarget;
  cookieCaptureTarget?: CookieValidatorTarget;
  description?: React.ReactNode;
}

interface Account {
  name?: string;
  [key: string]: any;
}

interface AccountsPanelProps {
  accounts: Account[];
  fields: AccountField[];
  onSave: (accounts: Account[]) => Promise<void>;
  hideFooter?: boolean;
  onAccountsChange?: (accounts: Account[]) => void;
}

export default function AccountsPanel({
  accounts: initialAccounts,
  fields,
  onSave,
  hideFooter,
  onAccountsChange,
}: AccountsPanelProps) {
  const { t } = useTranslation();
  const runtimeMode = useAppContextStore((s) => s.runtimeMode);
  const isDesktopApp = runtimeMode !== RuntimeMode.Docker;
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [saving, setSaving] = useState(false);
  const [validationStatus, setValidationStatus] = useState<
    Record<string, "loading" | "succeed" | "failed">
  >({});
  const [captureStatus, setCaptureStatus] = useState<
    Record<string, "loading" | "succeed" | "failed">
  >({});

  useEffect(() => {
    setAccounts(
      initialAccounts.length > 0
        ? initialAccounts.map((a) => ({ ...a }))
        : [],
    );
  }, [initialAccounts]);

  useEffect(() => {
    onAccountsChange?.(accounts);
  }, [accounts]);

  const addAccount = () => {
    const newAccount: Account = { name: t("resourceSource.accounts.defaultName", { index: accounts.length + 1 }) };
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

  const handleValidateCookie = async (
    index: number,
    field: AccountField,
  ) => {
    const cookie = accounts[index]?.[field.key];
    if (!cookie || !field.cookieValidatorTarget) return;
    const key = `${index}-${field.key}`;
    setValidationStatus((prev) => ({ ...prev, [key]: "loading" }));
    try {
      const rsp = await BApi.tool.validateCookie({
        target: field.cookieValidatorTarget,
        cookie,
      });
      setValidationStatus((prev) => ({
        ...prev,
        [key]: rsp.code ? "failed" : "succeed",
      }));
    } catch {
      setValidationStatus((prev) => ({ ...prev, [key]: "failed" }));
    }
  };

  const handleCaptureCookie = async (
    index: number,
    field: AccountField,
  ) => {
    if (!field.cookieCaptureTarget) return;
    const key = `${index}-${field.key}`;
    setCaptureStatus((prev) => ({ ...prev, [key]: "loading" }));
    try {
      const rsp = await BApi.tool.captureCookie({ target: field.cookieCaptureTarget });
      if (!rsp.code && rsp.data) {
        updateAccount(index, field.key, rsp.data);
        setCaptureStatus((prev) => ({ ...prev, [key]: "succeed" }));
      } else {
        setCaptureStatus((prev) => ({ ...prev, [key]: "failed" }));
      }
    } catch {
      setCaptureStatus((prev) => ({ ...prev, [key]: "failed" }));
    }
  };

  const handleSave = async () => {
    setSaving(true);
    try {
      await onSave(accounts);
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="space-y-4">
      {accounts.length > 0 && (
        <p className="text-xs text-default-400">
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
                  <AiOutlineDelete className="text-lg" />
                </Button>
              </div>

              <Input
                isRequired
                label={t("resourceSource.accounts.name")}
                placeholder={t("resourceSource.accounts.namePlaceholder")}
                size="sm"
                value={account.name || ""}
                onValueChange={(v) => updateAccount(index, "name", v)}
              />

              {fields.map((field) => {
                const vKey = `${index}-${field.key}`;
                const vStatus = validationStatus[vKey];
                return field.type === "textarea" ? (
                  <div key={field.key}>
                    <Textarea
                      label={field.label}
                      maxRows={3}
                      placeholder={field.placeholder}
                      size="sm"
                      value={account[field.key] || ""}
                      onValueChange={(v) =>
                        updateAccount(index, field.key, v)
                      }
                    />
                    {(field.cookieValidatorTarget != null || (isDesktopApp && field.cookieCaptureTarget != null)) && (
                      <div className="flex items-center gap-2 mt-1">
                        {field.cookieValidatorTarget != null && (
                          <Button
                            color={vStatus === "succeed" ? "success" : vStatus === "failed" ? "danger" : "primary"}
                            isDisabled={!account[field.key]}
                            isLoading={vStatus === "loading"}
                            size="sm"
                            variant="flat"
                            startContent={vStatus === "succeed" ? <AiOutlineCheck /> : vStatus === "failed" ? <AiOutlineClose /> : undefined}
                            onPress={() =>
                              handleValidateCookie(index, field)
                            }
                          >
                            {t("common.action.validate")}
                          </Button>
                        )}
                        {isDesktopApp && field.cookieCaptureTarget != null && (
                          <Button
                            color="secondary"
                            isLoading={captureStatus[`${index}-${field.key}`] === "loading"}
                            size="sm"
                            variant="flat"
                            onPress={() => handleCaptureCookie(index, field)}
                          >
                            {t("resourceSource.accounts.loginToImport")}
                          </Button>
                        )}
                      </div>
                    )}
                    {field.description && (
                      <div className="mt-1 text-xs text-default-400">
                        {field.description}
                      </div>
                    )}
                  </div>
                ) : (
                  <div key={field.key}>
                    <Input
                      label={field.label}
                      placeholder={field.placeholder}
                      size="sm"
                      type={
                        field.type === "password" ? "password" : "text"
                      }
                      value={account[field.key] || ""}
                      onValueChange={(v) =>
                        updateAccount(index, field.key, v)
                      }
                    />
                    {field.description && (
                      <div className="mt-1 text-xs text-default-400">
                        {field.description}
                      </div>
                    )}
                  </div>
                );
              })}

              {index < accounts.length - 1 && <Divider />}
            </div>
          ))}
        </div>
      )}

      <div className="flex gap-2">
        <Button
          size="sm"
          startContent={<AiOutlinePlus />}
          variant="flat"
          onPress={addAccount}
        >
          {t("resourceSource.accounts.add")}
        </Button>
        {!hideFooter && (
          <>
            <div className="flex-1" />
            <Button
              color="primary"
              isLoading={saving}
              size="sm"
              onPress={handleSave}
            >
              {t("thirdPartyConfig.action.save")}
            </Button>
          </>
        )}
      </div>
    </div>
  );
}
