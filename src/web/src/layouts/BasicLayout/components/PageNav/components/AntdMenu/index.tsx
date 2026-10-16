"use client";

import type { MenuProps } from "antd";
import type { NavigationMenuItem } from "./navigationLayout";

import React, { useEffect, useMemo, useState } from "react";
import { Menu } from "antd";
import { useNavigate, useLocation } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { AiOutlineFolder, AiOutlineQuestionCircle } from "react-icons/ai";

import { asideMenuConfig } from "./menuConfig";
import { buildVisibleNavigationMenu, mergeNavigationLayout } from "./navigationLayout";

import BetaChip from "@/components/Chips/BetaChip";
import DeprecatedChip from "@/components/Chips/DeprecatedChip";
import { useIsPureClient, useRemoteAccessStore } from "@/stores/remoteAccess";
import { useUiOptionsStore } from "@/stores/options";

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
    useRemoteAccessStore(
      (state) => state.initialized && (state.federationAvailable ?? state.isLocal),
    ) && !isPureClient;
  const savedLayout = useUiOptionsStore((state) => state.data.navigationLayout);
  const visibleMenuConfig = useMemo(() => {
    const translate = (key: string) => t<string>(key);
    const layout = mergeNavigationLayout(asideMenuConfig, savedLayout, translate);

    return buildVisibleNavigationMenu(
      asideMenuConfig,
      layout,
      { isPureClient, isLocalNode: Boolean(isLocalNode) },
      translate,
    );
  }, [savedLayout, isPureClient, isLocalNode, t]);

  const onClick: MenuProps["onClick"] = (e) => {
    const item = visibleMenuConfig
      .flatMap((node) => [node, ...(node.children ?? [])])
      .find((node) => node.id === e.key);

    if (item?.path) navigate(item.path);
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

  function convertItem(item: NavigationMenuItem): MenuItem {
    const Icon =
      item.icon ?? (item.kind === "directory" ? AiOutlineFolder : AiOutlineQuestionCircle);

    return getItem(
      collapsed ? (
        <span className={item.isDeprecated ? "line-through" : ""}>{item.label}</span>
      ) : (
        <div className="flex items-center gap-0.5">
          <span className={item.isDeprecated ? "line-through" : ""}>{item.label}</span>
          {item.isBeta && <BetaChip iconOnly />}
          {item.isDeprecated && <DeprecatedChip />}
        </div>
      ),
      item.id,
      <Icon className={"!text-lg"} />,
      item.children?.map(convertItem),
    );
  }

  const items: MenuProps["items"] = visibleMenuConfig.map(convertItem);

  const findSelectedKey = (): string => {
    for (const m of visibleMenuConfig) {
      if (m.path === pathname) {
        return m.id;
      }
      for (const c of m.children || []) {
        if (c.path === pathname) {
          return c.id;
        }
      }
    }

    return "";
  };

  const selectedParent = visibleMenuConfig.find((item) =>
    item.children?.some((child) => child.path === pathname),
  )?.id;
  const [openKeys, setOpenKeys] = useState<string[]>(selectedParent ? [selectedParent] : []);

  useEffect(() => {
    if (selectedParent && !collapsed) {
      setOpenKeys((previous) =>
        previous.includes(selectedParent) ? previous : [...previous, selectedParent],
      );
    }
  }, [selectedParent, collapsed]);

  return (
    <Menu
      forceSubMenuRender
      inlineCollapsed={collapsed}
      inlineIndent={12}
      items={items}
      mode="inline"
      openKeys={collapsed ? undefined : openKeys}
      selectedKeys={[findSelectedKey()]}
      style={{
        background: "none",
        border: "none",
        width: "100%",
      }}
      onClick={onClick}
      onOpenChange={(keys) => setOpenKeys(keys.map(String))}
    />
  );
};

export default Index;
