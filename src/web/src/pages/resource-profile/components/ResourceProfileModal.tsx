"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { BakabaseAbstractionsModelsDomainResourceProfile } from "@/sdk/Api";

import { useState } from "react";
import { useTranslation } from "react-i18next";

import { Modal, Input } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { toSearchInputModel } from "@/components/ResourceFilter";

type ResourceProfile = BakabaseAbstractionsModelsDomainResourceProfile;

type Props = {
  profile?: ResourceProfile;
  existingNames?: string[];
  onSaved?: () => void;
} & DestroyableProps;

// Generate default name like "资源档案 1", "资源档案 2", etc.
const generateDefaultName = (existingNames: string[], t: (key: string) => string): string => {
  const baseNameKey = "Resource Profile";
  const baseName = t(baseNameKey);
  let n = 1;
  while (existingNames.includes(`${baseName} ${n}`)) {
    n++;
  }
  return `${baseName} ${n}`;
};

const ResourceProfileModal = ({ profile, existingNames = [], onSaved, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const isEdit = !!profile?.id;

  const [name, setName] = useState(
    profile?.name || (isEdit ? "" : generateDefaultName(existingNames, t))
  );
  const [priority, setPriority] = useState(profile?.priority ?? 0);

  const [saving, setSaving] = useState(false);

  const handleSubmit = async () => {
    if (!name.trim()) {
      return;
    }

    setSaving(true);
    try {
      // Build input model with proper search format
      const inputModel = {
        name,
        priority,
        // Convert search to input model format (with serialized dbValue)
        search: profile?.search ? toSearchInputModel(profile.search) : undefined,
        nameTemplate: profile?.nameTemplate,
        enhancerOptions: profile?.enhancerOptions,
        playableFileOptions: profile?.playableFileOptions,
        playerOptions: profile?.playerOptions,
      };

      if (isEdit && profile?.id) {
        await BApi.resourceProfile.updateResourceProfile(profile.id, inputModel as any);
      } else {
        await BApi.resourceProfile.addResourceProfile(inputModel as any);
      }
      onSaved?.();
      onDestroyed?.();
    } catch (e) {
      console.error("Failed to save resource profile", e);
    } finally {
      setSaving(false);
    }
  };

  const isValid = name.trim() !== "";

  return (
    <Modal
      defaultVisible
      okProps={{
        isDisabled: !isValid,
        isLoading: saving,
      }}
      size="md"
      title={isEdit ? t("Edit Resource Profile") : t("Add Resource Profile")}
      onDestroyed={onDestroyed}
      onOk={handleSubmit}
    >
      <div className="flex flex-col gap-4">
        <Input
          isRequired
          errorMessage={!name.trim() ? t("Name is required") : ""}
          isInvalid={!name.trim()}
          label={t("Name")}
          placeholder={t("Profile name")}
          value={name}
          onValueChange={setName}
        />
        <Input
          description={t("Higher priority profiles take precedence")}
          label={t("Priority")}
          placeholder="0"
          type="number"
          value={String(priority)}
          onValueChange={(v) => setPriority(parseInt(v) || 0)}
        />
        {!isEdit && (
          <p className="text-xs text-default-400">
            {t("Other settings can be configured directly in the table after creation.")}
          </p>
        )}
      </div>
    </Modal>
  );
};

ResourceProfileModal.displayName = "ResourceProfileModal";

export default ResourceProfileModal;
