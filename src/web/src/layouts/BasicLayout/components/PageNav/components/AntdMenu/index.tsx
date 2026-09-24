"use client";

import type { MenuProps } from "antd";
import type { IMenuItem } from "./menuConfig";

import React, { useMemo, useRef } from "react";
import { Menu } from "antd";
import { useNavigate, useLocation } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { AiOutlineQuestionCircle } from "react-icons/ai";

import { asideMenuConfig } from "./menuConfig";

import BetaChip from "@/components/Chips/BetaChip";
import DeprecatedChip from "@/components/Chips/DeprecatedChip";
import { useIsPureClient, useRemoteAccessStore } from "@/stores/remoteAccess";

type MenuItem = Required<MenuProps>["items"][number];

interface IProps {
  collapsed: boolean;
}

const Index: React.FC<IProps> = ({ collapsed }: IProps) => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { pathname } = useLocation();
  const isPureClient = useIsPureClient();
  const isLocalNode =
    useRemoteAccessStore((state) => state.initialized && state.isLocal) && !isPureClient;
  // console.log(pathname);

  const onClick: MenuProps["onClick"] = (e) => {
    navigate(e.key as string);
  };

  function getItem(
    label: React.ReactNode,
    key?: React.Key,
    icon?: React.ReactNode,
    children?: MenuItem[],
    type?: "group",
  ): MenuItem {
    return {
      key,
      icon,
      children,
      label,
      type,
    } as MenuItem;
  }

  function convertItem(item: IMenuItem): MenuItem {
    const Icon = item.icon ?? AiOutlineQuestionCircle;

    return getItem(
      collapsed ? (
        <span className={item.isDeprecated ? "line-through" : ""}>{t<string>(item.name)}</span>
      ) : (
        <div className="flex items-center gap-0.5">
          <span className={item.isDeprecated ? "line-through" : ""}>{t<string>(item.name)}</span>
          {item.isBeta && <BetaChip />}
          {item.isDeprecated && <DeprecatedChip />}
        </div>
      ),
      item.path,
      <Icon className={"!text-lg"} />,
      item.children?.map(convertItem),
    );
  }

  // Filtered here rather than where the config is built: whether this window shows a
  // managed server is answered by the context call, which has not happened when that
  // module loads.
  const visibleMenuConfig = useMemo(() => {
    const visible = (m: IMenuItem) =>
      (!m.pureClientOnly || isPureClient) && (!m.localNodeOnly || isLocalNode);
    const filter = (items: IMenuItem[]): IMenuItem[] =>
      items.filter(visible).map((m) => (m.children ? { ...m, children: filter(m.children) } : m));

    return filter(asideMenuConfig);
  }, [isPureClient, isLocalNode]);

  const items: MenuProps["items"] = visibleMenuConfig.map(convertItem);

  const findSelectedKey = (): string => {
    for (const m of visibleMenuConfig) {
      if (m.path === pathname) {
        return m.path;
      }
      for (const c of m.children || []) {
        if (c.path === pathname) {
          return c.path;
        }
      }
    }

    return "";
  };

  const defaultOpenKeysRef = useRef(
    visibleMenuConfig
      .filter((m) => m.children?.some((c) => c.path === pathname))
      .map((m) => m.path!),
  );
  const defaultSelectedKeysRef = useRef([findSelectedKey()]);

  return (
    <Menu
      forceSubMenuRender
      defaultOpenKeys={defaultOpenKeysRef.current}
      defaultSelectedKeys={defaultSelectedKeysRef.current}
      inlineCollapsed={collapsed}
      inlineIndent={12}
      items={items}
      mode="inline"
      selectedKeys={[findSelectedKey()]}
      style={{
        background: "none",
        border: "none",
        width: "100%",
      }}
      onClick={onClick}
    />
  );
};

export default Index;
