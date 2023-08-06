import React, { useEffect, useState } from 'react';
import { Notification } from '@alifd/next';
import { GetAppInfo } from '@/sdk/apis';
import './index.scss';
import i18n from 'i18next';
import AppInfo from '@/pages/Configuration/components/AppInfo';
import ContactUs from '@/pages/Configuration/components/ContactUs';
import Functional from '@/pages/Configuration/components/Functional';
import Others from '@/pages/Configuration/components/Others';

const ConfigurationPage = function (props) {
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
          title: i18n.t('Saved'),
        });
        success(a);
      }
    });
  };

  return (
    <div className={'configuration-page'}>
      <Functional applyPatches={applyPatches} />
      <Others applyPatches={applyPatches} />
      <AppInfo appInfo={appInfo} />
      <ContactUs />
    </div>
  );
};

export default ConfigurationPage;
