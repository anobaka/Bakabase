"use client";

import React, { useEffect, useRef, useState } from "react";
import { Link, useLocation } from "react-router-dom";
import { useTranslation } from "react-i18next";
import {
  MenuFoldOutlined,
  MenuUnfoldOutlined,
  MoonOutlined,
  SunOutlined,
  TranslationOutlined,
} from "@ant-design/icons";

import AntdMenu from "./components/AntdMenu";
import styles from "./index.module.scss";

import { Button, Divider } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { useAppOptionsStore, useUiOptionsStore } from "@/stores/options";
import { UiTheme } from "@/sdk/constants";

const OptIconStyle = { fontSize: 20 };

const Navigation = () => {
  const { t } = useTranslation();
  const { pathname } = useLocation();

  const appOptions = useAppOptionsStore((state) => state.data);
  const uiOptionsStore = useUiOptionsStore();
  const isDarkMode = appOptions.uiTheme == UiTheme.Dark;
  const isEnglish = appOptions.language == "en";

  const [loading, setLoading] = useState(false);
  const prevPathRef = useRef<string>(pathname);
  const isCollapsed = uiOptionsStore.data.isMenuCollapsed;

  useEffect(() => {
    if (pathname != prevPathRef.current) {
      setLoading(false);
      prevPathRef.current = pathname;
    }
  }, [pathname]);

  console.log("PageNav", pathname);

  return (
    <div className={`${styles.nav} ${isCollapsed ? `${styles.collapsed}` : ""}`}>
      {/* {loading && (
        <div style={{
          position: 'fixed',
          top: 0,
          left: 0,
          width: '100vw',
          height: '100vh',
          backgroundColor: 'rgba(0, 0, 0, 0.5)',
          display: 'flex',
          justifyContent: 'center',
          alignItems: 'center',
          zIndex: 9999
        }}>
          <Spinner size="lg" />
        </div>
      )} */}
      <div className={styles.top}>
        <Link to="/">{isCollapsed ? "B" : "Bakabase"}</Link>
      </div>
      <div className={styles.menu}>
        <AntdMenu collapsed={isCollapsed} />
      </div>
      <div className={"px-2"}>
        <Divider orientation={"horizontal"} />
      </div>
      <div className={styles.opts}>
        <Button
          isIconOnly
          color={"default"}
          variant={"light"}
          onPress={() => {
            setLoading(true);
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
        <Button
          isIconOnly
          color={"default"}
          variant={"light"}
          onPress={() => {
            setLoading(true);
            BApi.options
              .patchAppOptions({
                language: isEnglish ? "cn" : "en",
              })
              .then(() => {
                location.reload();
              });
          }}
        >
          <TranslationOutlined style={OptIconStyle} />
        </Button>
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
    </div>
  );
};

export default Navigation;
