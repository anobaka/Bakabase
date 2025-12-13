"use client";

import type { DestroyableProps } from "@/components/bakaui/types";

import { useState } from "react";
import { useTranslation } from "react-i18next";
import { PlusOutlined, DeleteOutlined, ToolOutlined } from "@ant-design/icons";
import { useNavigate } from "react-router-dom";

import { Modal, Input, Button, Switch, Chip, Select, Textarea } from "@/components/bakaui";
import PathAutocomplete from "@/components/PathAutocomplete";
import BApi from "@/sdk/BApi";

interface PathMark {
  type: number; // 1=Resource, 2=Property
  priority: number;
  configJson: string;
}

interface PathRule {
  id?: number;
  path: string;
  name?: string;
  description?: string;
  priority: number;
  isEnabled: boolean;
  marks?: PathMark[];
}

type Props = {
  rule?: PathRule;
  onSaved?: () => void;
} & DestroyableProps;

const PathRuleModal = ({ rule, onSaved, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const isEdit = !!rule?.id;

  const [formData, setFormData] = useState<PathRule>({
    id: rule?.id,
    path: rule?.path || "",
    name: rule?.name || "",
    description: rule?.description || "",
    priority: rule?.priority ?? 0,
    isEnabled: rule?.isEnabled ?? true,
    marks: rule?.marks || [],
  });

  const [saving, setSaving] = useState(false);
  const [pathType, setPathType] = useState<"file" | "folder" | undefined>("folder");

  const updateField = <K extends keyof PathRule>(field: K, value: PathRule[K]) => {
    setFormData((prev) => ({ ...prev, [field]: value }));
  };

  const addMark = (type: number) => {
    const newMark: PathMark = {
      type,
      priority: (formData.marks?.length || 0) + 1,
      configJson: type === 1 ? JSON.stringify({ layer: 1 }) : JSON.stringify({}),
    };
    updateField("marks", [...(formData.marks || []), newMark]);
  };

  const removeMark = (index: number) => {
    const newMarks = [...(formData.marks || [])];
    newMarks.splice(index, 1);
    updateField("marks", newMarks);
  };

  const updateMark = (index: number, updates: Partial<PathMark>) => {
    const newMarks = [...(formData.marks || [])];
    newMarks[index] = { ...newMarks[index], ...updates };
    updateField("marks", newMarks);
  };

  const handleSubmit = async () => {
    if (!formData.path.trim()) {
      return;
    }

    setSaving(true);
    try {
      if (isEdit && formData.id) {
        // @ts-ignore - API will be available after SDK regeneration
        await BApi.pathRule.updatePathRule(formData.id, formData);
      } else {
        // @ts-ignore
        await BApi.pathRule.addPathRule(formData);
      }
      onSaved?.();
      onDestroyed?.();
    } catch (e) {
      console.error("Failed to save path rule", e);
    } finally {
      setSaving(false);
    }
  };

  const isValid = formData.path.trim() !== "";

  return (
    <Modal
      defaultVisible
      size="2xl"
      title={isEdit ? t("Edit Path Rule") : t("Add Path Rule")}
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
            label={t("Name")}
            placeholder={t("Optional name for this rule")}
            value={formData.name || ""}
            onValueChange={(v) => updateField("name", v)}
          />
          <Input
            label={t("Priority")}
            type="number"
            placeholder="0"
            value={String(formData.priority)}
            onValueChange={(v) => updateField("priority", parseInt(v) || 0)}
            description={t("Higher priority rules are applied first")}
          />
        </div>

        <PathAutocomplete
          isRequired
          label={t("Path")}
          placeholder={t("Select the root path for this rule")}
          pathType="folder"
          value={formData.path}
          onChange={(value, type) => {
            updateField("path", value);
            setPathType(type);
          }}
          isInvalid={!formData.path.trim()}
          errorMessage={!formData.path.trim() ? t("Path is required") : ""}
        />

        <Textarea
          label={t("Description")}
          placeholder={t("Optional description")}
          value={formData.description || ""}
          onValueChange={(v) => updateField("description", v)}
          minRows={2}
        />

        <div className="flex items-center gap-2">
          <Switch
            isSelected={formData.isEnabled}
            onValueChange={(v) => updateField("isEnabled", v)}
          />
          <span>{t("Enabled")}</span>
        </div>

        {/* Marks Section */}
        <div className="border-t pt-4">
          <div className="flex items-center justify-between mb-3">
            <h4 className="font-medium">{t("Path Marks")}</h4>
            <div className="flex gap-2">
              <Button
                size="sm"
                color="success"
                variant="flat"
                startContent={<ToolOutlined />}
                onPress={() => {
                  if (formData.path) {
                    onDestroyed?.();
                    navigate(`/path-rule-config?path=${encodeURIComponent(formData.path)}`);
                  }
                }}
                isDisabled={!formData.path}
              >
                {t("Use Visual Config Tool")}
              </Button>
              <Button
                size="sm"
                color="primary"
                variant="flat"
                startContent={<PlusOutlined />}
                onPress={() => addMark(1)}
              >
                {t("Add Resource Mark")}
              </Button>
              <Button
                size="sm"
                color="secondary"
                variant="flat"
                startContent={<PlusOutlined />}
                onPress={() => addMark(2)}
              >
                {t("Add Property Mark")}
              </Button>
            </div>
          </div>

          {(!formData.marks || formData.marks.length === 0) ? (
            <div className="text-center text-default-400 py-4 border rounded-lg border-dashed">
              <div className="mb-2">
                {t("No marks configured. Add marks to define how paths are processed.")}
              </div>
              <Button
                size="sm"
                color="primary"
                variant="bordered"
                startContent={<ToolOutlined />}
                onPress={() => {
                  if (formData.path) {
                    onDestroyed?.();
                    navigate(`/path-rule-config?path=${encodeURIComponent(formData.path)}`);
                  }
                }}
                isDisabled={!formData.path}
              >
                {t("Use Visual Config Tool to get started")}
              </Button>
            </div>
          ) : (
            <div className="flex flex-col gap-3">
              {formData.marks.map((mark, index) => (
                <div
                  key={index}
                  className="border rounded-lg p-3 flex flex-col gap-2"
                >
                  <div className="flex items-center justify-between">
                    <div className="flex items-center gap-2">
                      <Chip
                        size="sm"
                        color={mark.type === 1 ? "primary" : "secondary"}
                        variant="flat"
                      >
                        {mark.type === 1 ? t("Resource") : t("Property")}
                      </Chip>
                      <span className="text-sm text-default-500">
                        {t("Priority")}: {mark.priority}
                      </span>
                    </div>
                    <Button
                      isIconOnly
                      size="sm"
                      color="danger"
                      variant="light"
                      onPress={() => removeMark(index)}
                    >
                      <DeleteOutlined />
                    </Button>
                  </div>

                  <div className="grid grid-cols-2 gap-2">
                    <Input
                      size="sm"
                      label={t("Priority")}
                      type="number"
                      value={String(mark.priority)}
                      onValueChange={(v) => updateMark(index, { priority: parseInt(v) || 0 })}
                    />
                    <Textarea
                      size="sm"
                      label={t("Config JSON")}
                      value={mark.configJson}
                      onValueChange={(v) => updateMark(index, { configJson: v })}
                      minRows={1}
                    />
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    </Modal>
  );
};

PathRuleModal.displayName = "PathRuleModal";

export default PathRuleModal;
