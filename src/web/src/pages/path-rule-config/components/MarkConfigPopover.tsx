"use client";

import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";

import React, { useState, useCallback } from "react";
import { useTranslation } from "react-i18next";
import { Input, Select, Button, Popover } from "@/components/bakaui";
import { PathMarkType, PathMatchMode, pathMatchModes, PropertyValueType, propertyValueTypes } from "@/sdk/constants";
import { DeleteOutlined, SaveOutlined } from "@ant-design/icons";

type Props = {
  trigger: React.ReactNode;
  mark?: BakabaseAbstractionsModelsDomainPathMark;
  markType: PathMarkType;
  onSave: (mark: BakabaseAbstractionsModelsDomainPathMark) => void;
  onDelete?: () => void;
  isOpen?: boolean;
  onOpenChange?: (open: boolean) => void;
};

interface MarkConfig {
  matchMode: PathMatchMode;
  layer?: number;
  regex?: string;
  // For Property type
  propertyPool?: string;
  propertyId?: number;
  valueType?: PropertyValueType;
  fixedValue?: string;
  valueLayer?: number;
  valueRegex?: string;
  // For Resource type
  fsTypeFilter?: string;
  extensions?: string[];
}

const parseMarkConfig = (configJson?: string): MarkConfig => {
  try {
    const config = JSON.parse(configJson || "{}");
    return {
      matchMode: config.MatchMode === "Regex" ? PathMatchMode.Regex : PathMatchMode.Layer,
      layer: config.Layer ?? config.layer ?? 0,
      regex: config.Regex ?? config.regex ?? "",
      propertyPool: config.Pool ?? config.propertyPool,
      propertyId: config.PropertyId ?? config.propertyId,
      valueType: config.ValueType === "Dynamic" ? PropertyValueType.Dynamic : PropertyValueType.Fixed,
      fixedValue: config.FixedValue ?? config.fixedValue ?? "",
      valueLayer: config.ValueLayer ?? config.valueLayer ?? 0,
      valueRegex: config.ValueRegex ?? config.valueRegex ?? "",
      fsTypeFilter: config.FsTypeFilter ?? config.fsTypeFilter,
      extensions: config.Extensions ?? config.extensions ?? [],
    };
  } catch {
    return {
      matchMode: PathMatchMode.Layer,
      layer: 0,
      valueType: PropertyValueType.Fixed,
      valueLayer: 0,
    };
  }
};

const buildConfigJson = (config: MarkConfig, markType: PathMarkType): string => {
  if (markType === PathMarkType.Resource) {
    return JSON.stringify({
      MatchMode: config.matchMode === PathMatchMode.Layer ? "Layer" : "Regex",
      Layer: config.layer,
      Regex: config.regex,
      FsTypeFilter: config.fsTypeFilter,
      Extensions: config.extensions,
    });
  } else {
    return JSON.stringify({
      MatchMode: config.matchMode === PathMatchMode.Layer ? "Layer" : "Regex",
      Layer: config.layer,
      Regex: config.regex,
      Pool: config.propertyPool,
      PropertyId: config.propertyId,
      ValueType: config.valueType === PropertyValueType.Fixed ? "Fixed" : "Dynamic",
      FixedValue: config.fixedValue,
      ValueLayer: config.valueLayer,
      ValueRegex: config.valueRegex,
    });
  }
};

const MarkConfigPopover = ({ trigger, mark, markType, onSave, onDelete, isOpen, onOpenChange }: Props) => {
  const { t } = useTranslation();

  const initialConfig = parseMarkConfig(mark?.configJson);
  const [priority, setPriority] = useState(mark?.priority ?? 10);
  const [config, setConfig] = useState<MarkConfig>(initialConfig);

  const handleSave = useCallback(() => {
    const newMark: BakabaseAbstractionsModelsDomainPathMark = {
      type: markType,
      priority,
      configJson: buildConfigJson(config, markType),
    };
    onSave(newMark);
    onOpenChange?.(false);
  }, [markType, priority, config, onSave, onOpenChange]);

  const updateConfig = useCallback((updates: Partial<MarkConfig>) => {
    setConfig(prev => ({ ...prev, ...updates }));
  }, []);

  return (
    <Popover
      placement="bottom-start"
      isOpen={isOpen}
      onOpenChange={onOpenChange}
      trigger={trigger}
    >
      <div className="p-4 min-w-[400px] max-w-[500px]">
        <div className="flex flex-col gap-3">
          {/* Header */}
          <div className="flex items-center justify-between">
            <h4 className="font-semibold">
              {mark ? t("Edit Mark") : t("Add Mark")} - {markType === PathMarkType.Resource ? t("Resource") : t("Property")}
            </h4>
            {mark && onDelete && (
              <Button
                size="sm"
                color="danger"
                variant="light"
                isIconOnly
                onPress={onDelete}
              >
                <DeleteOutlined />
              </Button>
            )}
          </div>

          {/* Priority */}
          <Input
            label={t("Priority")}
            type="number"
            size="sm"
            value={String(priority)}
            onValueChange={(v) => setPriority(parseInt(v) || 10)}
          />

          {/* Match Mode */}
          <Select
            label={t("Match Mode")}
            size="sm"
            selectedKeys={[String(config.matchMode)]}
            onSelectionChange={(keys) => {
              const value = Array.from(keys)[0];
              updateConfig({ matchMode: Number(value) });
            }}
            dataSource={pathMatchModes as any}
          />

          {/* Layer or Regex */}
          {config.matchMode === PathMatchMode.Layer ? (
            <Input
              label={t("Layer (0 = current item)")}
              type="number"
              size="sm"
              value={String(config.layer ?? 0)}
              onValueChange={(v) => updateConfig({ layer: parseInt(v) || 0 })}
            />
          ) : (
            <Input
              label={t("Regex Pattern")}
              size="sm"
              value={config.regex ?? ""}
              onValueChange={(v) => updateConfig({ regex: v })}
            />
          )}

          {/* Property-specific fields */}
          {markType === PathMarkType.Property && (
            <>
              <div className="flex gap-2">
                <Input
                  className="flex-1"
                  label={t("Property Pool")}
                  size="sm"
                  value={config.propertyPool ?? ""}
                  onValueChange={(v) => updateConfig({ propertyPool: v })}
                />
                <Input
                  className="w-32"
                  label={t("Property ID")}
                  type="number"
                  size="sm"
                  value={String(config.propertyId ?? 0)}
                  onValueChange={(v) => updateConfig({ propertyId: parseInt(v) || 0 })}
                />
              </div>

              <Select
                label={t("Value Type")}
                size="sm"
                selectedKeys={[String(config.valueType ?? PropertyValueType.Fixed)]}
                onSelectionChange={(keys) => {
                  const value = Array.from(keys)[0];
                  updateConfig({ valueType: Number(value) });
                }}
                dataSource={propertyValueTypes as any}
              />

              {config.valueType === PropertyValueType.Fixed ? (
                <Input
                  label={t("Fixed Value")}
                  size="sm"
                  value={config.fixedValue ?? ""}
                  onValueChange={(v) => updateConfig({ fixedValue: v })}
                />
              ) : (
                <div className="flex gap-2">
                  <Input
                    className="w-32"
                    label={t("Value Layer")}
                    type="number"
                    size="sm"
                    value={String(config.valueLayer ?? 0)}
                    onValueChange={(v) => updateConfig({ valueLayer: parseInt(v) || 0 })}
                  />
                  <Input
                    className="flex-1"
                    label={t("Value Regex")}
                    size="sm"
                    value={config.valueRegex ?? ""}
                    onValueChange={(v) => updateConfig({ valueRegex: v })}
                  />
                </div>
              )}
            </>
          )}

          {/* Resource-specific fields */}
          {markType === PathMarkType.Resource && (
            <>
              <Input
                label={t("File Type Filter (optional)")}
                size="sm"
                value={config.fsTypeFilter ?? ""}
                onValueChange={(v) => updateConfig({ fsTypeFilter: v })}
              />
              <Input
                label={t("Extensions (comma-separated, optional)")}
                size="sm"
                value={(config.extensions ?? []).join(", ")}
                onValueChange={(v) => updateConfig({ extensions: v.split(",").map(e => e.trim()).filter(Boolean) })}
              />
            </>
          )}

          {/* Actions */}
          <div className="flex gap-2 justify-end pt-2">
            <Button
              size="sm"
              variant="flat"
              onPress={() => onOpenChange?.(false)}
            >
              {t("Cancel")}
            </Button>
            <Button
              size="sm"
              color="primary"
              startContent={<SaveOutlined />}
              onPress={handleSave}
            >
              {t("Save")}
            </Button>
          </div>
        </div>
      </div>
    </Popover>
  );
};

MarkConfigPopover.displayName = "MarkConfigPopover";

export default MarkConfigPopover;
