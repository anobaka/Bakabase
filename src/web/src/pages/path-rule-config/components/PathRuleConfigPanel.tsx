import React, { useState, useCallback } from "react";
import { useTranslation } from "react-i18next";
import {
  Modal,
  Button,
  Input,
  Select,
  Chip,
  toast,
} from "@/components/bakaui";
import type { DestroyableProps } from "@/components/bakaui/types";
import {
  CopyOutlined,
  PlusOutlined,
  DeleteOutlined,
  SettingOutlined,
} from "@ant-design/icons";
import BApi from "@/sdk/BApi";
import type {
  BakabaseAbstractionsModelsDomainPathMark,
} from "@/sdk/Api";
import type { Entry } from "@/core/models/FileExplorer/Entry";
import { PathMarkType, pathMarkTypes, PathMatchMode, pathMatchModes, PropertyValueType, propertyValueTypes } from "@/sdk/constants";

interface PathRuleConfigPanelProps extends DestroyableProps {
  /** Selected entries to configure */
  selectedEntries: Entry[];
  /** Callback when configuration is complete */
  onComplete?: () => void;
}

interface MarkConfig {
  type: PathMarkType;
  priority: number;
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

/**
 * Modal for batch configuring path rules on selected paths
 */
const PathRuleConfigPanel: React.FC<PathRuleConfigPanelProps> = ({
  selectedEntries,
  onComplete,
  onDestroyed,
}) => {
  const { t } = useTranslation();
  const [loading, setLoading] = useState(false);
  const [marks, setMarks] = useState<MarkConfig[]>([]);
  const [copiedConfig, setCopiedConfig] = useState<string | null>(null);

  const selectedPaths = selectedEntries.map((e) => e.path);

  const addMark = useCallback(() => {
    setMarks([
      ...marks,
      {
        type: PathMarkType.Resource,
        priority: 10,
        matchMode: PathMatchMode.Layer,
        layer: 1,
      },
    ]);
  }, [marks]);

  const removeMark = useCallback(
    (index: number) => {
      setMarks(marks.filter((_, i) => i !== index));
    },
    [marks]
  );

  const updateMark = useCallback(
    (index: number, updates: Partial<MarkConfig>) => {
      setMarks(
        marks.map((mark, i) => (i === index ? { ...mark, ...updates } : mark))
      );
    },
    [marks]
  );

  const buildConfigJson = (mark: MarkConfig): string => {
    if (mark.type === PathMarkType.Resource) {
      return JSON.stringify({
        MatchMode:
          mark.matchMode === PathMatchMode.Layer
            ? "Layer"
            : "Regex",
        Layer: mark.layer,
        Regex: mark.regex,
        FsTypeFilter: mark.fsTypeFilter,
        Extensions: mark.extensions,
      });
    } else {
      return JSON.stringify({
        MatchMode:
          mark.matchMode === PathMatchMode.Layer
            ? "Layer"
            : "Regex",
        Layer: mark.layer,
        Regex: mark.regex,
        Pool: mark.propertyPool,
        PropertyId: mark.propertyId,
        ValueType:
          mark.valueType === PropertyValueType.Fixed
            ? "Fixed"
            : "Dynamic",
        FixedValue: mark.fixedValue,
        ValueLayer: mark.valueLayer,
        ValueRegex: mark.valueRegex,
      });
    }
  };

  const applyConfig = useCallback(async () => {
    if (selectedPaths.length === 0) {
      toast.warning(t("No paths selected"));
      return;
    }

    if (marks.length === 0) {
      toast.warning(t("No marks configured"));
      return;
    }

    setLoading(true);
    try {
      const pathMarks: BakabaseAbstractionsModelsDomainPathMark[] = marks.map(
        (mark) => ({
          type: mark.type,
          priority: mark.priority,
          configJson: buildConfigJson(mark),
        })
      );

      const marksJson = JSON.stringify(pathMarks);

      const rsp = await BApi.pathRule.applyPathRuleConfig({
        marksJson,
        targetPaths: selectedPaths,
      });

      if (!rsp.code) {
        toast.success(
          t("Successfully applied rules to {{count}} path(s)", {
            count: selectedPaths.length,
          })
        );
        onComplete?.();
        return true; // Signal modal to close
      }
      return false;
    } catch (error) {
      console.error("Failed to apply config:", error);
      toast.danger(t("Failed to apply configuration"));
      return false;
    } finally {
      setLoading(false);
    }
  }, [selectedPaths, marks, t, onComplete]);

  const copyConfigFromPath = useCallback(async (path: string) => {
    try {
      const rsp = await BApi.pathRule.copyPathRuleConfig(path);
      if (!rsp.code && rsp.data) {
        setCopiedConfig(rsp.data);
        toast.success(t("Configuration copied"));
      }
    } catch (error) {
      console.error("Failed to copy config:", error);
      toast.danger(t("Failed to copy configuration"));
    }
  }, [t]);

  const pasteConfig = useCallback(async () => {
    if (!copiedConfig) {
      toast.warning(t("No configuration copied"));
      return false;
    }

    setLoading(true);
    try {
      const rsp = await BApi.pathRule.applyPathRuleConfig({
        marksJson: copiedConfig,
        targetPaths: selectedPaths,
      });

      if (!rsp.code) {
        toast.success(
          t("Successfully pasted rules to {{count}} path(s)", {
            count: selectedPaths.length,
          })
        );
        onComplete?.();
        return true;
      }
      return false;
    } catch (error) {
      console.error("Failed to paste config:", error);
      toast.danger(t("Failed to paste configuration"));
      return false;
    } finally {
      setLoading(false);
    }
  }, [copiedConfig, selectedPaths, t, onComplete]);

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["ok", "cancel"],
        okProps: {
          children: t("Apply Rules"),
          isLoading: loading,
          isDisabled: marks.length === 0 || selectedPaths.length === 0,
          startContent: <SettingOutlined />,
        },
      }}
      size="2xl"
      title={t("Configure Path Rules")}
      onDestroyed={onDestroyed}
      onOk={applyConfig}
    >
      <div className="flex flex-col gap-4">
        <div className="flex items-center justify-between">
          <Chip color="secondary" size="sm">
            {t("{{count}} path(s) selected", { count: selectedPaths.length })}
          </Chip>
        </div>

        {/* Selected paths preview */}
        <div className="max-h-32 overflow-y-auto bg-default-100 rounded p-2">
          {selectedPaths.slice(0, 5).map((path) => (
            <div key={path} className="text-sm text-default-600 truncate">
              {path}
            </div>
          ))}
          {selectedPaths.length > 5 && (
            <div className="text-sm text-default-400">
              {t("... and {{count}} more", { count: selectedPaths.length - 5 })}
            </div>
          )}
        </div>

        {/* Copy/Paste actions */}
        <div className="flex gap-2">
          <Button
            color="default"
            isDisabled={selectedPaths.length !== 1}
            size="sm"
            startContent={<CopyOutlined />}
            variant="flat"
            onPress={() => copyConfigFromPath(selectedPaths[0])}
          >
            {t("Copy from selected")}
          </Button>
          <Button
            color="primary"
            isDisabled={!copiedConfig || selectedPaths.length === 0}
            isLoading={loading}
            size="sm"
            variant="flat"
            onPress={pasteConfig}
          >
            {t("Paste configuration")}
          </Button>
        </div>

        {/* Marks configuration */}
        <div className="flex flex-col gap-2">
          <div className="flex items-center justify-between">
            <span className="font-medium">{t("Marks")}</span>
            <Button
              color="primary"
              size="sm"
              startContent={<PlusOutlined />}
              variant="flat"
              onPress={addMark}
            >
              {t("Add Mark")}
            </Button>
          </div>

          {marks.map((mark, index) => (
            <div
              key={index}
              className="border border-default-200 rounded p-3 flex flex-col gap-2"
            >
              <div className="flex items-center justify-between gap-2">
                <Select
                  className="w-40"
                  label={t("Type")}
                  selectedKeys={[String(mark.type)]}
                  size="sm"
                  onSelectionChange={(keys) => {
                    const value = Array.from(keys)[0];
                    updateMark(index, { type: Number(value) });
                  }}
                  dataSource={pathMarkTypes as any}
                >
                </Select>
                <Input
                  className="w-24"
                  label={t("Priority")}
                  size="sm"
                  type="number"
                  value={String(mark.priority)}
                  onValueChange={(v) =>
                    updateMark(index, { priority: parseInt(v) || 10 })
                  }
                />
                <Button
                  color="danger"
                  isIconOnly
                  size="sm"
                  variant="light"
                  onPress={() => removeMark(index)}
                >
                  <DeleteOutlined />
                </Button>
              </div>

              <div className="flex gap-2">
                <Select
                  className="w-32"
                  label={t("Match Mode")}
                  selectedKeys={[String(mark.matchMode)]}
                  size="sm"
                  dataSource={pathMatchModes as any}
                  onSelectionChange={(keys) => {
                    const value = Array.from(keys)[0];
                    updateMark(index, { matchMode: Number(value) });
                  }}
                >
                </Select>

                {mark.matchMode ===
                PathMatchMode.Layer ? (
                  <Input
                    className="w-24"
                    label={t("Layer")}
                    size="sm"
                    type="number"
                    value={String(mark.layer ?? 1)}
                    onValueChange={(v) =>
                      updateMark(index, { layer: parseInt(v) || 1 })
                    }
                  />
                ) : (
                  <Input
                    className="flex-1"
                    label={t("Regex Pattern")}
                    size="sm"
                    value={mark.regex ?? ""}
                    onValueChange={(v) => updateMark(index, { regex: v })}
                  />
                )}
              </div>

              {mark.type ===
                PathMarkType.Property && (
                <div className="flex flex-col gap-2">
                  <div className="flex gap-2">
                    <Input
                      className="flex-1"
                      label={t("Property Pool")}
                      size="sm"
                      value={mark.propertyPool ?? ""}
                      onValueChange={(v) =>
                        updateMark(index, { propertyPool: v })
                      }
                    />
                    <Input
                      className="w-32"
                      label={t("Property ID")}
                      size="sm"
                      type="number"
                      value={String(mark.propertyId ?? 0)}
                      onValueChange={(v) =>
                        updateMark(index, { propertyId: parseInt(v) || 0 })
                      }
                    />
                  </div>
                  <Select
                    label={t("Value Type")}
                    selectedKeys={[String(mark.valueType ?? 1)]}
                    size="sm"
                    dataSource={propertyValueTypes as any}
                    onSelectionChange={(keys) => {
                      const value = Array.from(keys)[0];
                      updateMark(index, { valueType: Number(value) });
                    }}
                  >
                  </Select>
                  {mark.valueType ===
                  PropertyValueType.Fixed ? (
                    <Input
                      label={t("Fixed Value")}
                      size="sm"
                      value={mark.fixedValue ?? ""}
                      onValueChange={(v) =>
                        updateMark(index, { fixedValue: v })
                      }
                    />
                  ) : (
                    <div className="flex gap-2">
                      <Input
                        className="w-32"
                        label={t("Value Layer")}
                        size="sm"
                        type="number"
                        value={String(mark.valueLayer ?? 1)}
                        onValueChange={(v) =>
                          updateMark(index, { valueLayer: parseInt(v) || 1 })
                        }
                      />
                      <Input
                        className="flex-1"
                        label={t("Value Regex (optional)")}
                        size="sm"
                        value={mark.valueRegex ?? ""}
                        onValueChange={(v) =>
                          updateMark(index, { valueRegex: v })
                        }
                      />
                    </div>
                  )}
                </div>
              )}
            </div>
          ))}

          {marks.length === 0 && (
            <div className="text-center text-default-400 py-4">
              {t("No marks configured. Click 'Add Mark' to start.")}
            </div>
          )}
        </div>
      </div>
    </Modal>
  );
};

PathRuleConfigPanel.displayName = "PathRuleConfigPanel";

export default PathRuleConfigPanel;
