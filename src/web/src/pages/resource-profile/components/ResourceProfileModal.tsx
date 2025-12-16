"use client";

import type { DestroyableProps } from "@/components/bakaui/types";

import { useState } from "react";
import { useTranslation } from "react-i18next";
import { PlusOutlined, DeleteOutlined } from "@ant-design/icons";

import { Modal, Input, Button, Textarea, Chip, Select } from "@/components/bakaui";
import BApi from "@/sdk/BApi";

interface SearchCriteria {
  mediaLibraryIds?: number[];
  propertyFilters?: PropertyFilter[];
  pathPattern?: string;
  tagFilter?: any;
}

interface PropertyFilter {
  pool: number;
  propertyId: number;
  operation: number;
  value?: any;
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

  const updateField = <K extends keyof ResourceProfile>(field: K, value: ResourceProfile[K]) => {
    setFormData((prev) => ({ ...prev, [field]: value }));
  };

  const updateCriteria = <K extends keyof SearchCriteria>(field: K, value: SearchCriteria[K]) => {
    setFormData((prev) => ({
      ...prev,
      searchCriteria: { ...prev.searchCriteria, [field]: value },
    }));
  };

  const addPropertyFilter = () => {
    const newFilter: PropertyFilter = {
      pool: 1, // Custom property pool
      propertyId: 0,
      operation: 1, // Equals
      value: null,
    };
    updateCriteria("propertyFilters", [...(formData.searchCriteria.propertyFilters || []), newFilter]);
  };

  const removePropertyFilter = (index: number) => {
    const filters = [...(formData.searchCriteria.propertyFilters || [])];
    filters.splice(index, 1);
    updateCriteria("propertyFilters", filters);
  };

  const updatePropertyFilter = (index: number, updates: Partial<PropertyFilter>) => {
    const filters = [...(formData.searchCriteria.propertyFilters || [])];
    filters[index] = { ...filters[index], ...updates };
    updateCriteria("propertyFilters", filters);
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

            {/* Property Filters */}
            <div>
              <div className="flex items-center justify-between mb-2">
                <span className="text-sm font-medium">{t("Property Filters")}</span>
                <Button
                  size="sm"
                  variant="flat"
                  startContent={<PlusOutlined />}
                  onPress={addPropertyFilter}
                >
                  {t("Add Filter")}
                </Button>
              </div>

              {(!formData.searchCriteria.propertyFilters || formData.searchCriteria.propertyFilters.length === 0) ? (
                <div className="text-center text-default-400 py-3 border rounded-lg border-dashed text-sm">
                  {t("No property filters. Click 'Add Filter' to add one.")}
                </div>
              ) : (
                <div className="flex flex-col gap-2">
                  {formData.searchCriteria.propertyFilters.map((filter, index) => (
                    <div
                      key={index}
                      className="border rounded-lg p-2 flex items-center gap-2"
                    >
                      <Select
                        size="sm"
                        label={t("Pool")}
                        selectedKeys={[String(filter.pool)]}
                        onSelectionChange={(keys) => {
                          const value = Array.from(keys)[0];
                          updatePropertyFilter(index, { pool: parseInt(value as string) || 1 });
                        }}
                        className="w-32"
                        dataSource={[
                          { value: "1", label: t("Custom") },
                          { value: "2", label: t("Reserved") },
                          { value: "3", label: t("Internal") },
                        ]}
                      />
                      <Input
                        size="sm"
                        label={t("Property ID")}
                        type="number"
                        value={String(filter.propertyId)}
                        onValueChange={(v) => updatePropertyFilter(index, { propertyId: parseInt(v) || 0 })}
                        className="w-32"
                      />
                      <Select
                        size="sm"
                        label={t("Operation")}
                        selectedKeys={[String(filter.operation)]}
                        onSelectionChange={(keys) => {
                          const value = Array.from(keys)[0];
                          updatePropertyFilter(index, { operation: parseInt(value as string) || 1 });
                        }}
                        className="w-32"
                        dataSource={[
                          { value: "1", label: t("Equals") },
                          { value: "2", label: t("Contains") },
                          { value: "3", label: t("StartsWith") },
                          { value: "4", label: t("EndsWith") },
                          { value: "5", label: t("IsEmpty") },
                          { value: "6", label: t("IsNotEmpty") },
                        ]}
                      />
                      <Input
                        size="sm"
                        label={t("Value")}
                        value={filter.value ? String(filter.value) : ""}
                        onValueChange={(v) => updatePropertyFilter(index, { value: v || null })}
                        className="flex-1"
                      />
                      <Button
                        isIconOnly
                        size="sm"
                        color="danger"
                        variant="light"
                        onPress={() => removePropertyFilter(index)}
                      >
                        <DeleteOutlined />
                      </Button>
                    </div>
                  ))}
                </div>
              )}
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
