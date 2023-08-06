import React, { useEffect, useState } from 'react';
import PropTypes from 'prop-types';
import { Link, useLocation, history } from 'ice';
import { Badge, Balloon, Nav } from '@alifd/next';
import i18next from 'i18next';
import i18n from 'i18next';
import { useTranslation } from 'react-i18next';
import { asideMenuConfig } from '../../menuConfig';
import CustomIcon from '@/components/CustomIcon';
import { CheckAppInitialized } from '@/sdk/apis';
import { BackgroundTaskStatus, InitializationContentType } from '@/sdk/constants';
import store from '@/store';


const { SubNav } = Nav;
const NavItem = Nav.Item;

// mock the auth object
// Ref: https://ice.work/docs/guide/advance/auth#%E5%88%9D%E5%A7%8B%E5%8C%96%E6%9D%83%E9%99%90%E6%95%B0%E6%8D%AE
const AUTH_CONFIG = {
  admin: true,
  guest: false,
};

export interface IMenuItem {
  name: string;
  path: string;
  icon?: string;
  children?: IMenuItem[];
}

function getNavMenuItems(menusData: any[], initIndex?: number | string, auth?: any) {
  if (!menusData) {
    return [];
  }

  return menusData
    .filter((item) => {
      let roleAuth = true;
      // if item.roles is [] or undefined, roleAuth is true
      if (auth && item.auth && item.auth instanceof Array) {
        if (item.auth.length) {
          roleAuth = item.auth.some((key) => auth[key]);
        }
      }
      return item.name && !item.hideInMenu && roleAuth;
    })
    .map((item, index) => {
      return getSubMenuOrItem(item, `${initIndex}-${index}`, auth);
    });
}

function getSubMenuOrItem(item: IMenuItem, index?: number | string, auth?: any) {
  if (item.children && item.children.some((child) => child.name)) {
    const childrenItems = getNavMenuItems(item.children, index, auth);
    if (childrenItems && childrenItems.length > 0) {
      const subNav = (
        <SubNav
          key={item.name}
          icon={<CustomIcon type={item.icon} />}
          label={item.name}
        >
          {childrenItems}
        </SubNav>
      );

      return subNav;
    }
    return null;
  }
  const navItem = (
    <NavItem key={item.path} icon={<CustomIcon type={item.icon} />}>
      {/* <Link to={item.path}> */}
      {item.name}
      {/* </Link> */}
    </NavItem>
  );

  return navItem;
}


const Navigation = () => {
  const [openKeys, setOpenKeys] = useState<string[]>([]);
  const location = useLocation();
  const { pathname } = location;

  const { t } = useTranslation();

  const [isCollapsed, setIsCollapsed] = useState(false);

  const backgroundTasks = store.useModelState('backgroundTasks');
  const activeTaskCount = backgroundTasks.filter((t) => t.status == BackgroundTaskStatus.Running).length;

  const localizedAsideMenuConfig = asideMenuConfig.map((c) => {
    const { path } = c;
    let name = t(c.name);
    if (path == '/backgroundtask') {
      if (activeTaskCount > 0) {
        name = (
          <div>
            {t(c.name)}
          &emsp;
            <Badge count={activeTaskCount} />
          </div>
        );
      }
    }
    return (
      {
        ...c,
        name,
        children: c.children?.map((s) => (
          {
            ...s,
            name: t(s.name),
          }
        )),
      }
    );
  });

  useEffect(() => {
    CheckAppInitialized().invoke((a) => {
      switch (a.data) {
        case InitializationContentType.AppDataDirectory:
          history.push('/configuration');
          break;
      }
    });

    const curSubNav = localizedAsideMenuConfig.find((menuConfig) => {
      return menuConfig.children && checkChildPathExists(menuConfig);
    });

    function checkChildPathExists(menuConfig) {
      return menuConfig.children.some((child) => {
        return child.children ? checkChildPathExists(child) : child.path === pathname;
      });
    }

    if (curSubNav && !openKeys.includes(curSubNav.name)) {
      setOpenKeys([...openKeys, curSubNav.name]);
    }
  }, [pathname]);

  return (
    <div className={`nav-container ${isCollapsed ? 'collapsed' : ''}`}>
      <div className="nav">
        <Link to={'/'} className="top">{isCollapsed ? 'IW' : 'Inside World'}</Link>
        <div className="menu">
          <Nav
            type="normal"
            openKeys={openKeys}
            selectedKeys={[pathname]}
            defaultSelectedKeys={[pathname]}
            embeddable
            activeDirection="right"
            iconOnly={isCollapsed}
            hasTooltip={isCollapsed}
            mode={isCollapsed ? 'popup' : 'inline'}
            onOpen={(keys) => {
              setOpenKeys(keys);
            }}
            onItemClick={(key, object, event) => {
              history.push(key);
            }}
          >
            {getNavMenuItems(localizedAsideMenuConfig, 0, AUTH_CONFIG, isCollapsed)}
          </Nav>
        </div>
      </div>
      <div
        className="shrinker"
        onClick={() => {
          setIsCollapsed(!isCollapsed);
        }}
      >
        <CustomIcon type={isCollapsed ? 'caret-right' : 'caret-left'} size={'xs'} />
      </div>
    </div>

  );
};

Navigation.contextTypes = {
  isCollapse: PropTypes.bool,
};

// const PageNav = withRouter(Navigation);

export default Navigation;
