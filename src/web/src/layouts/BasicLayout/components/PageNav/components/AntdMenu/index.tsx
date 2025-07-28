"use client";

import type { MenuProps } from "antd";
import type { IMenuItem } from "./menuConfig";

import React, { useRef } from "react";
import { Menu } from "antd";
import { useNavigate, useLocation } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { AiOutlineQuestionCircle } from "react-icons/ai";

import { asideMenuConfig } from "./menuConfig";

import BetaChip from "@/components/Chips/BetaChip";
import DeprecatedChip from "@/components/Chips/DeprecatedChip";

type MenuItem = Required<MenuProps>["items"][number];

interface IProps {
  collapsed: boolean;
}

const Index: React.FC<IProps> = ({ collapsed }: IProps) => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { pathname } = useLocation();
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
        t<string>(item.name)
      ) : (
        <div className="flex items-center gap-0.5">
          {t<string>(item.name)}
          {item.isBeta && <BetaChip />}
          {item.isDeprecated && <DeprecatedChip />}
        </div>
      ),
      item.path,
      <Icon className={"!text-lg"} />,
      item.children?.map(convertItem),
    );
  }

  const items: MenuProps["items"] = asideMenuConfig.map(convertItem);

  const defaultOpenKeysRef = useRef(
    asideMenuConfig
      .filter((m) => m.children?.some((c) => pathname.includes(c.path!)))
      .map((m) => m.path!),
  );
  const defaultSelectedKeysRef = useRef([
    (() => {
      for (const m of asideMenuConfig) {
        if (m.path === pathname) {
          return pathname;
        }
        for (const c of m.children || []) {
          if (pathname.includes(c.path!)) {
            return c.path!;
          }
        }
      }

      return "";
    })(),
  ]);

  return (
    <Menu
      defaultOpenKeys={defaultOpenKeysRef.current}
      defaultSelectedKeys={defaultSelectedKeysRef.current}
      inlineCollapsed={collapsed}
      inlineIndent={12}
      mode="inline"
      selectedKeys={[(() => {
        for (const m of asideMenuConfig) {
          if (m.path === pathname) {
            return pathname;
          }
          for (const c of m.children || []) {
            if (pathname.includes(c.path!)) {
              return c.path!;
            }
          }
        }
        return '';
      })()]}
      style={{
        background: 'none',
        border: 'none',
        width: '100%',
      }}
      onClick={onClick}
      forceSubMenuRender
      // inlineIndent={0}
      items={items}
    />
  );
};

export default Index;
