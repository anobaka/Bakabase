import React from 'react';
import { AppstoreOutlined, MailOutlined, SettingOutlined } from '@ant-design/icons';
import type { MenuProps } from 'antd';
import { Menu } from 'antd';
import { history } from 'ice';

import { asideMenuConfig } from '../menuConfig';
import type { IMenuItem } from '@/layouts/BasicLayout/components/PageNav';
import CustomIcon from '@/components/CustomIcon';

type MenuItem = Required<MenuProps>['items'][number];

function getItem(
  label: React.ReactNode,
  key: React.Key,
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

function createItem(item: IMenuItem) {
  return getItem(item.name, item.path, <CustomIcon type={item.icon} />, item.children?.map(createItem));
}

const items: MenuProps['items'] = asideMenuConfig.map(createItem);

const App: React.FC = () => {
  const onClick: MenuProps['onClick'] = (e) => {
    console.log('click ', e);
    history!.push(e.key);
  };

  return (
    <Menu
      onClick={onClick}
      style={{ width: 256 }}
      defaultSelectedKeys={['1']}
      defaultOpenKeys={['sub1']}
      mode="inline"
      items={items}
    />
  );
};

export default App;
