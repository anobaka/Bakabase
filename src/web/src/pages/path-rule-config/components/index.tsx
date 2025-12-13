"use client";

import type { Entry } from "@/core/models/FileExplorer/Entry";
import type { BakabaseAbstractionsModelsDomainPathRule } from "@/sdk/Api";

import React from "react";
import { useTranslation } from "react-i18next";
import { RiRulerLine } from "react-icons/ri";
import { SettingOutlined, DeleteOutlined } from "@ant-design/icons";

import { Chip, Tooltip, Button, Popover } from "@/components/bakaui";

type Props = {
  entry: Entry;
  pathRule?: BakabaseAbstractionsModelsDomainPathRule;
  onConfigure?: (entry: Entry, pathRule: BakabaseAbstractionsModelsDomainPathRule) => void;
  onDelete?: (pathRule: BakabaseAbstractionsModelsDomainPathRule) => void;
};

const PathRuleMarks = ({ entry, pathRule, onConfigure, onDelete }: Props) => {
  const { t } = useTranslation();

  if (!pathRule || !pathRule.marks || pathRule.marks.length === 0) {
    return null;
  }

  const resourceMarks = pathRule.marks.filter(m => m.type === 1);
  const propertyMarks = pathRule.marks.filter(m => m.type === 2);

  const chipContent = (
    <div className="flex items-center gap-1">
      {resourceMarks.length > 0 && (
        <Chip size="sm" color="primary" variant="flat">
          <div className="flex items-center gap-1">
            <RiRulerLine className="text-xs" />
            <span>R×{resourceMarks.length}</span>
          </div>
        </Chip>
      )}
      {propertyMarks.length > 0 && (
        <Chip size="sm" color="secondary" variant="flat">
          <div className="flex items-center gap-1">
            <RiRulerLine className="text-xs" />
            <span>P×{propertyMarks.length}</span>
          </div>
        </Chip>
      )}
    </div>
  );

  return (
    <div className="flex items-center gap-1 ml-2">
      <Popover
        placement="bottom"
        trigger={chipContent}
      >
        <div className="p-3 min-w-[300px] max-w-[500px]">
          <div className="flex items-center justify-between mb-3">
            <div className="font-semibold">{t("Path Rule")}: {pathRule.name || pathRule.path}</div>
            <div className="flex gap-1">
              {onConfigure && (
                <Button
                  isIconOnly
                  size="sm"
                  color="primary"
                  variant="light"
                  onPress={(e) => {
                    e.stopPropagation();
                    onConfigure(entry, pathRule);
                  }}
                >
                  <SettingOutlined />
                </Button>
              )}
              {onDelete && (
                <Button
                  isIconOnly
                  size="sm"
                  color="danger"
                  variant="light"
                  onPress={(e) => {
                    e.stopPropagation();
                    onDelete(pathRule);
                  }}
                >
                  <DeleteOutlined />
                </Button>
              )}
            </div>
          </div>

          {resourceMarks.length > 0 && (
            <div className="mb-2">
              <div className="text-sm font-medium mb-1">{t("Resource Marks")} ({resourceMarks.length})</div>
              <div className="flex flex-col gap-1">
                {resourceMarks.map((mark, idx) => {
                  try {
                    const config = JSON.parse(mark.configJson || "{}");
                    return (
                      <div key={idx} className="text-xs bg-primary-50 dark:bg-primary-950 rounded px-2 py-1">
                        <div>{t("Priority")}: {mark.priority}</div>
                        <div>{t("Layer")}: {config.layer || "N/A"}</div>
                        {config.regex && <div>{t("Regex")}: {config.regex}</div>}
                      </div>
                    );
                  } catch {
                    return (
                      <div key={idx} className="text-xs bg-primary-50 dark:bg-primary-950 rounded px-2 py-1">
                        {t("Priority")}: {mark.priority}
                      </div>
                    );
                  }
                })}
              </div>
            </div>
          )}

          {propertyMarks.length > 0 && (
            <div>
              <div className="text-sm font-medium mb-1">{t("Property Marks")} ({propertyMarks.length})</div>
              <div className="flex flex-col gap-1">
                {propertyMarks.map((mark, idx) => {
                  try {
                    const config = JSON.parse(mark.configJson || "{}");
                    return (
                      <div key={idx} className="text-xs bg-secondary-50 dark:bg-secondary-950 rounded px-2 py-1">
                        <div>{t("Priority")}: {mark.priority}</div>
                        {config.propertyId && <div>{t("Property ID")}: {config.propertyId}</div>}
                        {config.valueRegex && <div>{t("Value Regex")}: {config.valueRegex}</div>}
                      </div>
                    );
                  } catch {
                    return (
                      <div key={idx} className="text-xs bg-secondary-50 dark:bg-secondary-950 rounded px-2 py-1">
                        {t("Priority")}: {mark.priority}
                      </div>
                    );
                  }
                })}
              </div>
            </div>
          )}
        </div>
      </Popover>
    </div>
  );
};

PathRuleMarks.displayName = "PathRuleMarks";

export default PathRuleMarks;
