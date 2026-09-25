"use client";

import React, { useState } from "react";
import { useTranslation } from "react-i18next";
import { MenuFoldOutlined, MenuUnfoldOutlined, MoonOutlined, SunOutlined } from "@ant-design/icons";
import { AiOutlineQuestionCircle } from "react-icons/ai";

import AntdMenu from "./components/AntdMenu";
import ServerSwitcher from "./components/ServerSwitcher";
import styles from "./index.module.scss";

import AppUpdateBanner from "@/layouts/BasicLayout/components/AppUpdateBanner";
import { Button, Divider, Tooltip } from "@/components/bakaui";
import { HelpCenterModal } from "@/components/HelpCenter";
import BApi from "@/sdk/BApi";
import { useAppOptionsStore, useUiOptionsStore } from "@/stores/options";
import { UiTheme } from "@/sdk/constants";
import LanguageSwitcher from "@/components/LanguageSwitcher";
import NotificationCenter from "@/components/NotificationCenter";
import DataSyncStatusIndicator from "@/features/data-sync/components/DataSyncStatusIndicator";

const OptIconStyle = { fontSize: 20 };

const Navigation = () => {
  const { t } = useTranslation();

  const appOptions = useAppOptionsStore((state) => state.data);
  const uiOptionsStore = useUiOptionsStore();
  const isDarkMode = appOptions.uiTheme == UiTheme.Dark;

  const [helpVisible, setHelpVisible] = useState(false);
  const isCollapsed = uiOptionsStore.data.isMenuCollapsed;

  return (
    <div className={`${styles.nav} ${isCollapsed ? `${styles.collapsed}` : ""}`}>
      <div className={styles.top}>
        {/* Which server this window shows, and the way to another. A plain brand link
            wherever there is nothing to switch to. */}
        <ServerSwitcher collapsed={isCollapsed} />
      </div>
      <div className={styles.menu}>
        <AntdMenu collapsed={isCollapsed} />
      </div>
      <AppUpdateBanner collapsed={isCollapsed} />
      <div className={"px-2"}>
        <Divider orientation={"horizontal"} />
      </div>
      <div className={styles.opts}>
        <Button
          isIconOnly
          color={"default"}
          variant={"light"}
          onPress={() => {
            BApi.options
              .patchAppOptions({
                uiTheme: isDarkMode ? UiTheme.Light : UiTheme.Dark,
              })
              .then(() => {
                location.reload();
              });
          }}
        >
          {isDarkMode ? (
            <SunOutlined style={OptIconStyle} />
          ) : (
            <MoonOutlined style={OptIconStyle} />
          )}
        </Button>
        <DataSyncStatusIndicator />
        <NotificationCenter />
        <LanguageSwitcher />
        {/*
          The global doorway into the help center. Every other entry point is
          contextual (a "?" next to the feature it explains); this one is how you
          get there when you do not already know which feature you need.
        */}
        <Tooltip content={t("helpCenter.button.tooltip")}>
          <Button
            isIconOnly
            aria-label={t<string>("helpCenter.button.tooltip")}
            color={"default"}
            variant={"light"}
            onPress={() => setHelpVisible(true)}
          >
            <AiOutlineQuestionCircle style={OptIconStyle} />
          </Button>
        </Tooltip>
        <Button
          isIconOnly
          color={"default"}
          variant={"light"}
          onPress={() => {
            uiOptionsStore.patch({
              isMenuCollapsed: !isCollapsed,
            });
          }}
        >
          {isCollapsed ? (
            <MenuUnfoldOutlined style={OptIconStyle} />
          ) : (
            <MenuFoldOutlined style={OptIconStyle} />
          )}
        </Button>
      </div>

      {helpVisible && (
        <HelpCenterModal visible={helpVisible} onClose={() => setHelpVisible(false)} />
      )}
    </div>
  );
};

export default Navigation;
