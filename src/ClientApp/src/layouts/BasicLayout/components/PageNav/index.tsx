import React, { useEffect, useRef, useState } from 'react';
import { Link, useLocation } from 'ice';
import { Loading } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import {
  MenuFoldOutlined,
  MenuUnfoldOutlined,
  MoonOutlined,
  SunOutlined,
  TranslationOutlined,
} from '@ant-design/icons';
import AntdMenu from './components/AntdMenu';

import styles from './index.module.scss';
import { Button } from '@/components/bakaui';
import BApi from '@/sdk/BApi';
import store from '@/store';
import { UiTheme } from '@/sdk/constants';

const OptIconStyle = { fontSize: 20 };

const Navigation = () => {
  const { t } = useTranslation();
  const { pathname } = useLocation();

  const appOptions = store.useModelState('appOptions');
  const isDarkMode = appOptions.uiTheme == UiTheme.Dark;
  const isEnglish = appOptions.language == 'en';

  const [loading, setLoading] = useState(false);
  const prevPathRef = useRef<string>(pathname);
  const [isCollapsed, setIsCollapsed] = useState(false);

  useEffect(() => {
    if (pathname != prevPathRef.current) {
      setLoading(false);
      prevPathRef.current = pathname;
    }
  }, [pathname]);

  return (
    <div className={`${styles.nav} ${isCollapsed ? `${styles.collapsed}` : ''}`}>
      <Loading fullScreen visible={loading} />
      <Link to={'/'} className={styles.top}>{isCollapsed ? 'B' : 'Bakabase'}</Link>
      <div className={styles.menu}>
        <AntdMenu collapsed={isCollapsed} />
      </div>
      <div className={styles.opts}>
        <Button
          isIconOnly
          color={'default'}
          onClick={() => {
            setLoading(true);
            BApi.options.patchAppOptions({
              uiTheme: isDarkMode ? UiTheme.Light : UiTheme.Dark,
            }).then(() => {
              location.reload();
            });
          }}
        >
          {isDarkMode ? <SunOutlined style={OptIconStyle} /> : <MoonOutlined style={OptIconStyle} />}
        </Button>
        <Button
          isIconOnly
          color={'default'}
          onClick={() => {
            setLoading(true);
            BApi.options.patchAppOptions({
              language: isEnglish ? 'cn' : 'en',
            }).then(() => {
              location.reload();
            });
          }}
        >
          <TranslationOutlined style={OptIconStyle} />
        </Button>
        <Button
          isIconOnly
          color={'default'}
          onClick={() => {
            setIsCollapsed(!isCollapsed);
          }}
        >
          {isCollapsed ? <MenuUnfoldOutlined style={OptIconStyle} /> : <MenuFoldOutlined style={OptIconStyle} />}
        </Button>
      </div>
    </div>

  );
};
export default Navigation;
