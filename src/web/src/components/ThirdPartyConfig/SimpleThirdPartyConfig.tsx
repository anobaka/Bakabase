"use client";

import { useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Modal, ModalContent, ModalHeader, ModalBody, ModalFooter } from "@heroui/react";

import { toast } from "@/components/bakaui";
import type { CookieValidatorTarget } from "@/sdk/constants";
import AccountsPanel, { type AccountField } from "./AccountsPanel";

interface SimpleThirdPartyConfigProps {
  title: string;
  isOpen?: boolean;
  onClose?: () => void;
  onDestroyed?: () => void;
  accounts: any[];
  onSave: (accounts: any[]) => Promise<void>;
  cookieValidatorTarget?: CookieValidatorTarget;
  cookieCaptureTarget?: CookieValidatorTarget;
}

export default function SimpleThirdPartyConfig({
  title,
  isOpen,
  onClose,
  onDestroyed,
  accounts,
  onSave,
  cookieValidatorTarget,
  cookieCaptureTarget,
}: SimpleThirdPartyConfigProps) {
  const { t } = useTranslation();
  const [saving, setSaving] = useState(false);
  const pendingAccountsRef = useRef<any[] | null>(null);

  const accountFields: AccountField[] = useMemo(
    () => [
      {
        key: "cookie",
        label: t("resourceSource.accounts.cookie"),
        placeholder: t("resourceSource.accounts.cookiePlaceholder"),
        type: "textarea" as const,
        cookieValidatorTarget,
        cookieCaptureTarget,
      },
    ],
    [t, cookieValidatorTarget, cookieCaptureTarget],
  );

  const handleClose = onClose ?? onDestroyed;

  const handleSave = async () => {
    setSaving(true);
    try {
      if (pendingAccountsRef.current !== null) {
        await onSave(pendingAccountsRef.current);
      }
      toast.success(t("thirdPartyConfig.success.saved"));
      handleClose?.();
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal isOpen={isOpen ?? true} scrollBehavior="inside" size="3xl" onClose={handleClose}>
      <ModalContent>
        <ModalHeader>{title}</ModalHeader>
        <ModalBody>
          <AccountsPanel
            accounts={accounts}
            fields={accountFields}
            hideFooter
            onAccountsChange={(accs) => { pendingAccountsRef.current = accs; }}
            onSave={async () => {}}
          />
        </ModalBody>
        <ModalFooter>
          <Button variant="light" onPress={handleClose}>
            {t("common.action.cancel")}
          </Button>
          <Button color="primary" isLoading={saving} onPress={handleSave}>
            {t("common.action.save")}
          </Button>
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}
