"use client";

import type { SettingItem } from "@/pages/configuration/components/SettingsSection";

import React from "react";
import { useTranslation } from "react-i18next";
import { QuestionCircleOutlined } from "@ant-design/icons";

import Component from "./components/Component";

import { useDependentComponentContextsStore } from "@/stores/dependentComponentContexts";
import { Popover, Snippet } from "@/components/bakaui";
import SettingsSection from "@/pages/configuration/components/SettingsSection";

interface DependencyProps {
  query?: string;
}

const Dependency: React.FC<DependencyProps> = ({ query }) => {
  const { t } = useTranslation();
  const componentContexts = useDependentComponentContextsStore((state) => state.contexts);

  const items: SettingItem[] = componentContexts.map((c, i) => ({
    id: String(c.id ?? i),
    // Rendered as a node, so the plain component name is repeated into keywords
    // to keep the row searchable.
    keywords: [c.name, c.description].filter(Boolean) as string[],
    label: (
      <div className={"flex gap-1 items-center"}>
        {c.name}
        <Popover
          showArrow
          placement={"right"}
          trigger={<QuestionCircleOutlined className={"text-base"} />}
        >
          <div className={"px-2 py-4 flex flex-col gap-2"} style={{ userSelect: "text" }}>
            {c.description && <pre>{c.description}</pre>}
            <div className={"flex items-center gap-2"}>
              {t<string>("configuration.dependency.defaultLocation")}
              <Snippet hideSymbol size={"sm"} variant="bordered">
                {c.defaultLocation}
              </Snippet>
            </div>
          </div>
        </Popover>
      </div>
    ),
    render: () => <Component id={c.id} />,
  }));

  return (
    <SettingsSection
      items={items}
      keywords={["dependency", "component", "ffmpeg", "依赖", "组件"]}
      query={query}
      title={t<string>("configuration.dependency.title")}
    />
  );
};

Dependency.displayName = "Dependency";

export default Dependency;
