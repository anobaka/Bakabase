import React, { useEffect, useState } from 'react';
import { Notification } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import Dependency from './components/Dependency';
import { GetAppInfo } from '@/sdk/apis';
import './index.scss';
import AppInfo from '@/pages/Configuration/components/AppInfo';
import ContactUs from '@/pages/Configuration/components/ContactUs';
import Functional from '@/pages/Configuration/components/Functional';
import Others from '@/pages/Configuration/components/Others';

const ConfigurationPage = function (props) {
  const { t } = useTranslation();
  const [appInfo, setAppInfo] = useState({});

  useEffect(() => {
    GetAppInfo().invoke((a) => {
      setAppInfo(a.data);
    });
  }, []);

  const applyPatches = (API, patches = {}, success = (rsp) => {}) => {
    API({
      model: patches,
    }).invoke((a) => {
      if (!a.code) {
        Notification.success({
          title: t('Saved'),
        });
        success(a);
      }
    });
  };

  return (
    <div className={'configuration-page'}>
      <Dependency />
      <Functional applyPatches={applyPatches} />
      <Others applyPatches={applyPatches} />
      <AppInfo appInfo={appInfo} />
      <ContactUs />
    </div>
  );
};

export default ConfigurationPage;
