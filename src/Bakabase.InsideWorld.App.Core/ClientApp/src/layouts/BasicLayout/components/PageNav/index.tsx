import React, { useEffect, useRef, useState } from 'react';
import PropTypes from 'prop-types';
import { history, Link, useLocation } from 'ice';
import { Badge, Loading, Nav } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import { asideMenuConfig } from '../../menuConfig';
import CustomIcon from '@/components/CustomIcon';
import { CheckAppInitialized } from '@/sdk/apis';
import { BackgroundTaskStatus, InitializationContentType } from '@/sdk/constants';
import store from '@/store';
import AntdMenu from '@/layouts/BasicLayout/components/AntdMenu';

const { Item } = Nav;

const { SubNav } = Nav;
const NavItem = Nav.Item;

export interface IMenuItem {
  name: string;
  path: string;
  icon?: string;
  children?: IMenuItem[];
}

function getNavMenuItems(menusData: any[], initIndex?: number | string) {
  if (!menusData) {
    return [];
  }

  return menusData
    .map((item, index) => {
      return getSubMenuOrItem(item, `${initIndex}-${index}`);
    });
}

function getSubMenuOrItem(item: IMenuItem, index?: number | string) {
  if (item.children && item.children.some((child) => child.name)) {
    const childrenItems = getNavMenuItems(item.children, index);
    if (childrenItems && childrenItems.length > 0) {
      const subNav = (
        <SubNav
          key={item.name}
          icon={(
            <CustomIcon type={item.icon} />
          )}
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
    <NavItem
      key={item.path}
      icon={(
        <CustomIcon type={item.icon} />
      )}
    >
      {item.name}
    </NavItem>
  );

  return navItem;
}


const Navigation = () => {
  const [openKeys, setOpenKeys] = useState<string[]>([]);
  const [loading, setLoading] = useState(false);
  const location = useLocation();
  const { pathname } = location;
  const prevPathRef = useRef<string>(pathname);

  const { t } = useTranslation();

  const [isCollapsed, setIsCollapsed] = useState(false);

  const backgroundTasks = store.useModelState('backgroundTasks');
  const activeTaskCount = backgroundTasks.filter((t) => t.status == BackgroundTaskStatus.Running).length;

  const localizedAsideMenuConfig = asideMenuConfig.map((c) => {
    const { path } = c;
    let name: any = t(c.name);
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
    console.log('444444444', pathname, prevPathRef.current);
    if (pathname != prevPathRef.current) {
      setLoading(false);
      prevPathRef.current = pathname;
    }

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
      <Loading fullScreen visible={loading} />
      <div className="nav">
        <Link to={'/'} className="top">{isCollapsed ? 'IW' : 'Inside World'}</Link>
        <div className="menu">
          <AntdMenu />
          {/* <Nav */}
          {/*   openMode={'multiple'} */}
          {/*   type="normal" */}
          {/*   openKeys={openKeys} */}
          {/*   selectedKeys={[pathname]} */}
          {/*   defaultSelectedKeys={[pathname]} */}
          {/*   embeddable */}
          {/*   iconOnly={isCollapsed} */}
          {/*   hasTooltip={isCollapsed} */}
          {/*   mode={isCollapsed ? 'popup' : 'inline'} */}
          {/*   onOpen={(keys) => { */}
          {/*     console.log(keys); */}
          {/*     setOpenKeys(keys); */}
          {/*   }} */}
          {/*   onItemClick={(key, object, event) => { */}
          {/*     setLoading(true); */}
          {/*     history!.push(key); */}
          {/*   }} */}
          {/* > */}
          {/*   {getNavMenuItems(localizedAsideMenuConfig, 0)} */}
          {/* </Nav> */}
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
