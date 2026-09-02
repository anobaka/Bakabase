"use client";

import type { SettingItem } from "@/pages/configuration/components/SettingsSection";

import { useTranslation } from "react-i18next";
import { Radio, RadioGroup } from "@heroui/react";

import { CloseBehavior, startupPages } from "@/sdk/constants";
import { useAppOptionsStore, useUiOptionsStore } from "@/stores/options";
import BApi from "@/sdk/BApi";
import SettingsSection from "@/pages/configuration/components/SettingsSection";

interface FunctionalProps {
  applyPatches: <T>(api: (patches: T) => Promise<{ code?: number }>, patches: T) => void;
  query?: string;
}

const Functional: React.FC<FunctionalProps> = ({ applyPatches, query }) => {
  const { t } = useTranslation();

  const appOptions = useAppOptionsStore((state) => state.data);
  const uiOptions = useUiOptionsStore((state) => state.data);

  const functionSettings: SettingItem[] = [
    {
      id: "startupPage",
      label: t("configuration.functional.startupPage"),
      keywords: ["startup", "home", "启动页"],
      render: () => {
        return (
          <RadioGroup
            orientation={"horizontal"}
            size={"sm"}
            value={String(uiOptions.startupPage)}
            onValueChange={(v) => {
              applyPatches(BApi.options.patchUiOptions, {
                startupPage: Number(v),
              });
            }}
          >
            {startupPages.map((s) => {
              return (
                <Radio key={s.value} value={String(s.value)}>
                  {t<string>(`configuration.functional.startupPage.${s.label.toLowerCase()}`)}
                </Radio>
              );
            })}
          </RadioGroup>
        );
      },
    },
    {
      id: "exitBehavior",
      label: t("configuration.functional.exitBehavior"),
      tip: t<string>("configuration.functional.exitBehavior.tip"),
      keywords: ["close", "quit", "exit", "tray", "关闭", "退出", "托盘"],
      render: () => {
        return (
          <RadioGroup
            orientation={"horizontal"}
            size={"sm"}
            value={String(appOptions.closeBehavior)}
            onValueChange={(v) => {
              applyPatches(BApi.options.patchAppOptions, {
                closeBehavior: Number(v),
              });
            }}
          >
            {[CloseBehavior.Minimize, CloseBehavior.Exit, CloseBehavior.Prompt].map((c) => (
              <Radio key={c} value={String(c)}>
                {t<string>(
                  `configuration.functional.exitBehavior.${CloseBehavior[c].toLowerCase()}`,
                )}
              </Radio>
            ))}
          </RadioGroup>
        );
      },
    },
  ];

  return (
    <SettingsSection
      items={functionSettings}
      keywords={["functional", "behaviour", "behavior"]}
      query={query}
      title={t<string>("configuration.functional.title")}
    />
  );
};

Functional.displayName = "Functional";

export default Functional;
