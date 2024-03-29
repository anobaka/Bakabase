import React from 'react';
import type { MenuProps } from 'antd';
import { Menu } from 'antd';
import { history } from 'ice';
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
    return getItem(t(item.name), item.path, <Icon type={item.icon} style={IconStyle} />, item.children?.map(convertItem));
  }

  const items: MenuProps['items'] = asideMenuConfig.map(convertItem);


  return (
    <Menu
      style={{ background: 'none', border: 'none', width: '100%' }}
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
