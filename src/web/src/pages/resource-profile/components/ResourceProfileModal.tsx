"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { SearchFilterGroup } from "@/components/Filter";

import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";

import { Modal, Input, Chip } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import {
  FilterProvider,
  FilterGroup,
  GroupCombinator,
  createDefaultFilterConfig,
} from "@/components/Filter";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

interface SearchCriteria {
  mediaLibraryIds?: number[];
  filterGroup?: SearchFilterGroup;
  pathPattern?: string;
  tagFilter?: any;
}

interface ResourceProfile {
  id?: number;
  name: string;
  searchCriteria: SearchCriteria;
  nameTemplate?: string;
  enhancerSettings?: any;
  playableFileSettings?: any;
  playerSettings?: any;
  priority: number;
}

type Props = {
  profile?: ResourceProfile;
  onSaved?: () => void;
} & DestroyableProps;

const ResourceProfileModal = ({ profile, onSaved, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const isEdit = !!profile?.id;

  const [formData, setFormData] = useState<ResourceProfile>({
    id: profile?.id,
    name: profile?.name || "",
    searchCriteria: profile?.searchCriteria || {},
    nameTemplate: profile?.nameTemplate || "",
    enhancerSettings: profile?.enhancerSettings,
    playableFileSettings: profile?.playableFileSettings,
    playerSettings: profile?.playerSettings,
    priority: profile?.priority ?? 0,
  });

  const [saving, setSaving] = useState(false);

  const filterConfig = useMemo(
    () => createDefaultFilterConfig(createPortal),
    [createPortal],
  );

  const updateField = <K extends keyof ResourceProfile>(field: K, value: ResourceProfile[K]) => {
    setFormData((prev) => ({ ...prev, [field]: value }));
  };

  const updateCriteria = <K extends keyof SearchCriteria>(field: K, value: SearchCriteria[K]) => {
    setFormData((prev) => ({
      ...prev,
      searchCriteria: { ...prev.searchCriteria, [field]: value },
    }));
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

  // Initialize filter group if not present
  const filterGroup: SearchFilterGroup = formData.searchCriteria.filterGroup ?? {
    combinator: GroupCombinator.And,
    disabled: false,
    filters: [],
    groups: [],
  };

  return (
    <Modal
      defaultVisible
      size="3xl"
      title={isEdit ? t("Edit Resource Profile") : t("Add Resource Profile")}
      okProps={{
        isDisabled: !isValid,
        isLoading: saving,
      }}
      onOk={handleSubmit}
      onDestroyed={onDestroyed}
    >
      <div className="flex flex-col gap-4">
        {/* Basic Info */}
        <div className="grid grid-cols-2 gap-4">
          <Input
            isRequired
            label={t("Name")}
            placeholder={t("Profile name")}
            value={formData.name}
            onValueChange={(v) => updateField("name", v)}
            isInvalid={!formData.name.trim()}
            errorMessage={!formData.name.trim() ? t("Name is required") : ""}
          />
          <Input
            label={t("Priority")}
            type="number"
            placeholder="0"
            value={String(formData.priority)}
            onValueChange={(v) => updateField("priority", parseInt(v) || 0)}
            description={t("Higher priority profiles take precedence")}
          />
        </div>

        <Input
          label={t("Name Template")}
          placeholder={t("e.g., {Name} - {Layer1}")}
          value={formData.nameTemplate || ""}
          onValueChange={(v) => updateField("nameTemplate", v)}
          description={t("Use {Name}, {Layer1}, {Layer2} etc. as placeholders")}
        />

        {/* Search Criteria Section */}
        <div className="border-t pt-4">
          <h4 className="font-medium mb-3">{t("Search Criteria")}</h4>

          <div className="flex flex-col gap-3">
            <Input
              label={t("Path Pattern")}
              placeholder={t("Regex pattern to match resource paths")}
              value={formData.searchCriteria.pathPattern || ""}
              onValueChange={(v) => updateCriteria("pathPattern", v || undefined)}
            />

            {/* Property Filters using FilterGroup */}
            <div>
              <span className="text-sm font-medium mb-2 block">{t("Property Filters")}</span>
              <FilterProvider config={filterConfig}>
                <FilterGroup
                  isRoot
                  group={filterGroup}
                  onChange={(group) => {
                    updateCriteria("filterGroup", group);
                  }}
                />
              </FilterProvider>
            </div>
          </div>
        </div>

        {/* Settings Summary */}
        <div className="border-t pt-4">
          <h4 className="font-medium mb-3">{t("Additional Settings")}</h4>
          <div className="flex gap-2 flex-wrap">
            <Chip
              size="sm"
              variant={formData.enhancerSettings ? "solid" : "flat"}
              color={formData.enhancerSettings ? "primary" : "default"}
            >
              {t("Enhancers")}: {formData.enhancerSettings ? t("Configured") : t("Not set")}
            </Chip>
            <Chip
              size="sm"
              variant={formData.playableFileSettings ? "solid" : "flat"}
              color={formData.playableFileSettings ? "secondary" : "default"}
            >
              {t("Playable Files")}: {formData.playableFileSettings ? t("Configured") : t("Not set")}
            </Chip>
            <Chip
              size="sm"
              variant={formData.playerSettings ? "solid" : "flat"}
              color={formData.playerSettings ? "success" : "default"}
            >
              {t("Players")}: {formData.playerSettings ? t("Configured") : t("Not set")}
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
