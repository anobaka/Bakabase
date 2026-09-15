"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { PropertyValueScope } from "@/sdk/constants";

import { useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineGlobal } from "react-icons/ai";

import ScopePriorityList from "../ScopePriorityEditor/ScopePriorityList";
import { checkProfileResponse } from "../../profileUtils";

import { Modal } from "@/components/bakaui";
import { propertyValueScopes } from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import { useResourceOptionsStore } from "@/stores/options";

export default function GlobalScopePriorityModal({ onDestroyed }: DestroyableProps) {
  const { t } = useTranslation();
  const resourceOptionsStore = useResourceOptionsStore();
  const [saving, setSaving] = useState(false);
  const [scopes, setScopes] = useState<PropertyValueScope[]>(() => [
    ...new Set([
      ...(resourceOptionsStore.data?.propertyValueScopePriority ?? []),
      ...propertyValueScopes.map((scope) => scope.value),
    ]),
  ]);
  const save = async () => {
    setSaving(true);
    try {
      const update = { propertyValueScopePriority: scopes };
      const response = await BApi.options.patchResourceOptions(update);

      checkProfileResponse(response, t<string>("resourceProfile.error.save"));
      resourceOptionsStore.update(update);
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["ok", "cancel"],
        okProps: { children: t<string>("resourceProfile.globalScopePriority.save") },
        cancelProps: { children: t<string>("common.action.cancel"), isDisabled: saving },
      }}
      hideCloseButton={saving}
      isDismissable={!saving}
      isKeyboardDismissDisabled={saving}
      size="md"
      title={t<string>("resourceProfile.globalScopePriority.title")}
      onDestroyed={onDestroyed}
      onOk={save}
    >
      <div className="mb-2 flex items-start gap-3 rounded-xl bg-primary/5 p-3">
        <AiOutlineGlobal aria-hidden className="mt-0.5 shrink-0 text-lg text-primary" />
        <p className="text-sm leading-6 text-default-600">
          {t<string>("resourceProfile.globalScopePriority.scopeHint")}
        </p>
      </div>
      <p className="mb-2 text-xs leading-5 text-default-500">
        {t<string>("resourceProfile.scopePriority.orderHint")}
      </p>
      <ScopePriorityList isDisabled={saving} scopes={scopes} onChange={setScopes} />
    </Modal>
  );
}
