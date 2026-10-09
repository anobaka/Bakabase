"use client";

import type { SettingItem } from "@/pages/configuration/components/SettingsSection";

import React from "react";
import { useTranslation } from "react-i18next";
import {
  AppstoreOutlined,
  FileZipOutlined,
  GlobalOutlined,
  VideoCameraOutlined,
} from "@ant-design/icons";

import Component from "./components/Component";

import { useDependentComponentContextsStore } from "@/stores/dependentComponentContexts";
import { Spinner } from "@/components/bakaui";
import SettingsSection from "@/pages/configuration/components/SettingsSection";
import { useIsPureClient } from "@/stores/remoteAccess";

interface DependencyProps {
  query?: string;
}

/// The one component the desktop app uses itself when showing a server it manages.
const LocaleEmulatorId = "locale-emulator-component-service";
const componentPresentation: Record<string, { icon: React.ReactNode; purpose: string }> = {
  "364e3884-4c6f-446f-b72c-1ec84e8da2c2": {
    icon: <VideoCameraOutlined />,
    purpose: "videoPurpose",
  },
  "7z-archiver-component-service": { icon: <FileZipOutlined />, purpose: "archivePurpose" },
  [LocaleEmulatorId]: { icon: <GlobalOutlined />, purpose: "localePurpose" },
};

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
    keywords: [
      c.name,
      c.description,
      componentPresentation[c.id] &&
        t(`configuration.dependency.${componentPresentation[c.id].purpose}`),
    ].filter(Boolean) as string[],
    label: (
      <div className="flex min-w-0 items-start gap-3 py-1">
        <span
          aria-hidden
          className="mt-0.5 flex h-9 w-9 shrink-0 items-center justify-center rounded-xl border border-default-200/60 bg-default-100/70 text-lg text-foreground-500"
        >
          {componentPresentation[c.id]?.icon || <AppstoreOutlined />}
        </span>
        <div className="min-w-0">
          <div className="font-medium text-foreground">{c.name}</div>
          <p className="mt-1 text-xs leading-relaxed text-foreground-400">
            {componentPresentation[c.id]
              ? t(`configuration.dependency.${componentPresentation[c.id].purpose}`)
              : c.description}
          </p>
          {/*
          Locale Emulator is the exception, and the exception matters: launching a
          work happens on the machine the user is sitting at, so the copy that gets
          used is the client's own — this row, forwarded, is the server's.
        */}
          {isPureClient && c.id === LocaleEmulatorId && (
            <span className="mt-1 block text-xs text-warning">
              {t<string>("configuration.dependency.localeEmulatorRunsOnThisMachine")}
            </span>
          )}
        </div>
      </div>
    ),
    render: () => <Component id={c.id} />,
  }));

  if (componentContexts.length === 0) {
    items.push({
      id: "loading",
      label: t("configuration.dependency.loadingComponents"),
      render: () => <Spinner size="sm" />,
    });
  }

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
