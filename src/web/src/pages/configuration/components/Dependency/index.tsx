"use client";

import type { SettingItem } from "@/pages/configuration/components/SettingsSection";

import React from "react";
import { useTranslation } from "react-i18next";
import { QuestionCircleOutlined } from "@ant-design/icons";

import Component from "./components/Component";

import { useDependentComponentContextsStore } from "@/stores/dependentComponentContexts";
import { Popover, Snippet } from "@/components/bakaui";
import SettingsSection from "@/pages/configuration/components/SettingsSection";
import { useIsPureClient } from "@/stores/remoteAccess";

interface DependencyProps {
  query?: string;
}

/// The one component the desktop app uses itself when showing a server it manages.
const LocaleEmulatorId = "locale-emulator-component-service";

const Dependency: React.FC<DependencyProps> = ({ query }) => {
  const { t } = useTranslation();
  const componentContexts = useDependentComponentContextsStore((state) => state.contexts);
  // These are installed by the machine that uses them. For a managed server shown in
  // this window that is the server for all but one of them, and the install button
  // here installs there.
  const isPureClient = useIsPureClient();

  const items: SettingItem[] = componentContexts.map((c, i) => ({
    id: String(c.id ?? i),
    // Rendered as a node, so the plain component name is repeated into keywords
    // to keep the row searchable.
    keywords: [c.name, c.description].filter(Boolean) as string[],
    label: (
      <div className={"flex gap-1 items-center"}>
        {c.name}
        {/*
          Locale Emulator is the exception, and the exception matters: launching a
          work happens on the machine the user is sitting at, so the copy that gets
          used is the client's own — this row, forwarded, is the server's.
        */}
        {isPureClient && c.id === LocaleEmulatorId && (
          <span className="text-xs text-warning">
            {t<string>("configuration.dependency.localeEmulatorRunsOnThisMachine")}
          </span>
        )}
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
      header={
        isPureClient ? (
          <span className="text-xs text-foreground-400">
            {t<string>("configuration.dependency.installedOnTheServer")}
          </span>
        ) : undefined
      }
      items={items}
      keywords={["dependency", "component", "ffmpeg", "依赖", "组件"]}
      query={query}
      title={t<string>("configuration.dependency.title")}
    />
  );
};

Dependency.displayName = "Dependency";

export default Dependency;
