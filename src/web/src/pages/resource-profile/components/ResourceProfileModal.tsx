"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { SearchCriteria } from "@/components/ResourceFilter";

import { useState } from "react";
import { useTranslation } from "react-i18next";

import { Modal, Input, Chip } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { ResourceSearchPanel } from "@/components/ResourceFilter";

interface ResourceProfile {
  id?: number;
  name: string;
  searchCriteria: SearchCriteria;
  nameTemplate?: string;
  enhancerOptions?: any;
  playableFileOptions?: any;
  playerOptions?: any;
  priority: number;
}

type Props = {
  profile?: ResourceProfile;
  onSaved?: () => void;
} & DestroyableProps;

const ResourceProfileModal = ({ profile, onSaved, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const isEdit = !!profile?.id;

  const [formData, setFormData] = useState<ResourceProfile>({
    id: profile?.id,
    name: profile?.name || "",
    searchCriteria: profile?.searchCriteria || {},
    nameTemplate: profile?.nameTemplate || "",
    enhancerOptions: profile?.enhancerOptions,
    playableFileOptions: profile?.playableFileOptions,
    playerOptions: profile?.playerOptions,
    priority: profile?.priority ?? 0,
  });

  const [saving, setSaving] = useState(false);

  const updateField = <K extends keyof ResourceProfile>(field: K, value: ResourceProfile[K]) => {
    setFormData((prev) => ({ ...prev, [field]: value }));
  };

  const handleSubmit = async () => {
    if (!formData.name.trim()) {
      return;
    }

    setSaving(true);
    try {
      if (isEdit && formData.id) {
        await BApi.resourceProfile.updateResourceProfile(formData.id, formData as any);
      } else {
        await BApi.resourceProfile.addResourceProfile(formData as any);
      }
      onSaved?.();
      onDestroyed?.();
    } catch (e) {
      console.error("Failed to save resource profile", e);
    } finally {
      setSaving(false);
    }
  };

  const isValid = formData.name.trim() !== "";

  return (
    <Modal
      defaultVisible
      okProps={{
        isDisabled: !isValid,
        isLoading: saving,
      }}
      size="3xl"
      title={isEdit ? t("Edit Resource Profile") : t("Add Resource Profile")}
      onDestroyed={onDestroyed}
      onOk={handleSubmit}
    >
      <div className="flex flex-col gap-4">
        {/* Basic Info */}
        <div className="grid grid-cols-2 gap-4">
          <Input
            isRequired
            errorMessage={!formData.name.trim() ? t("Name is required") : ""}
            isInvalid={!formData.name.trim()}
            label={t("Name")}
            placeholder={t("Profile name")}
            value={formData.name}
            onValueChange={(v) => updateField("name", v)}
          />
          <Input
            description={t("Higher priority profiles take precedence")}
            label={t("Priority")}
            placeholder="0"
            type="number"
            value={String(formData.priority)}
            onValueChange={(v) => updateField("priority", parseInt(v) || 0)}
          />
        </div>

        <Input
          description={t("Use {Name}, {Layer1}, {Layer2} etc. as placeholders")}
          label={t("Name Template")}
          placeholder={t("e.g., {Name} - {Layer1}")}
          value={formData.nameTemplate || ""}
          onValueChange={(v) => updateField("nameTemplate", v)}
        />

        {/* Search Criteria Section */}
        <div className="border-t pt-4">
          <h4 className="font-medium mb-3">{t("Search Criteria")}</h4>
          <p className="text-xs text-default-400 mb-3">
            {t("Define which resources this profile applies to")}
          </p>
          <ResourceSearchPanel
            compact
            criteria={formData.searchCriteria}
            showKeyword
            showQuickFilters
            showRecentFilters
            showTags
            onChange={(criteria) => {
              setFormData((prev) => ({ ...prev, searchCriteria: criteria }));
            }}
          />
        </div>

        {/* Settings Summary */}
        <div className="border-t pt-4">
          <h4 className="font-medium mb-3">{t("Additional Settings")}</h4>
          <div className="flex gap-2 flex-wrap">
            <Chip
              color={formData.enhancerOptions ? "primary" : "default"}
              size="sm"
              variant={formData.enhancerOptions ? "solid" : "flat"}
            >
              {t("Enhancers")}: {formData.enhancerOptions ? t("Configured") : t("Not set")}
            </Chip>
            <Chip
              color={formData.playableFileOptions ? "secondary" : "default"}
              size="sm"
              variant={formData.playableFileOptions ? "solid" : "flat"}
            >
              {t("Playable Files")}:{" "}
              {formData.playableFileOptions ? t("Configured") : t("Not set")}
            </Chip>
            <Chip
              color={formData.playerOptions ? "success" : "default"}
              size="sm"
              variant={formData.playerOptions ? "solid" : "flat"}
            >
              {t("Players")}: {formData.playerOptions ? t("Configured") : t("Not set")}
            </Chip>
          </div>
          <p className="text-xs text-default-400 mt-2">
            {t("Advanced settings can be configured after creation.")}
          </p>
        </div>
      </div>
    </Modal>
  );
};

ResourceProfileModal.displayName = "ResourceProfileModal";

export default ResourceProfileModal;
