import React, { useRef } from 'react';
import type { MenuProps } from 'antd';
import { Menu } from 'antd';
import { history, useLocation } from 'ice';
import { useTranslation } from 'react-i18next';
import type { IMenuItem } from '../../menuConfig';
import { asideMenuConfig } from '../../menuConfig';
import { Icon } from '@/components/bakaui';

type MenuItem = Required<MenuProps>['items'][number];

interface IProps {
  collapsed: boolean;
}

const IconStyle = { fontSize: 16 };

const Index: React.FC<IProps> = ({ collapsed }: IProps) => {
  const { t } = useTranslation();
  const { pathname } = useLocation();
  // console.log(pathname);

  const onClick: MenuProps['onClick'] = (e) => {
    history!.push(e.key);
  };

  function getItem(
    label: React.ReactNode,
    key?: React.Key,
    icon?: React.ReactNode,
    children?: MenuItem[],
    type?: 'group',
  ): MenuItem {
    return {
      key,
      icon,
      children,
      label,
      type,
    } as MenuItem;
  }

  function convertItem(item: IMenuItem) {
    return getItem(t(item.name), item.path, <Icon
      type={item.icon}
      style={IconStyle}
    />, item.children?.map(convertItem));
  }

  const items: MenuProps['items'] = asideMenuConfig.map(convertItem);

  const defaultOpenKeysRef = useRef(asideMenuConfig.filter(m => m.children?.some(c => pathname.includes(c.path!))).map(m => m.path!));
  const defaultSelectedKeysRef = useRef([(() => {
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
  })()]);

  return (
    <Menu
      defaultOpenKeys={defaultOpenKeysRef.current}
      defaultSelectedKeys={defaultSelectedKeysRef.current}
      style={{
        background: 'none',
        border: 'none',
        width: '100%',
      }}
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
      inlineCollapsed={collapsed}
      onClick={onClick}
      mode="inline"
      forceSubMenuRender
      // inlineIndent={0}
      items={items}
    />
  );
};

export default Index;
