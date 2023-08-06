import React, { useEffect, useState } from 'react';
import './index.scss';
import { Balloon, Button } from '@alifd/next';
import IceLabel from '@icedesign/label';
import { AcceptTerms, GetAppInfo } from '@/sdk/apis';
import i18n from 'i18next';
import { history } from 'ice';

export default () => {
  const [info, setInfo] = useState({});

  useEffect(() => {
    GetAppInfo().invoke((a) => {
      setInfo(a.data);
    });
  }, []);

  const renderTerms = () => {
    const tc = (
      <div style={{ width: 400 }}>
        {i18n.t('We will do some data tracking, the anonymous data will help us to improve our product experience, and no personal data will be collected.')}
        <br />
        {i18n.t('You can disable it in system settings.')}
      </div>
    );
    if (i18n.language == 'cn') {
      return (
        <div className={'agreement'}>
          请在使用前仔细阅读
          <Balloon
            align={'r'}
            triggerType={'click'}
            closable={false}
            trigger={
              i18n.t('Terms and Conditions')
          }
          >
            {tc}
          </Balloon>
        </div>
      );
    } else {
      return (
        <div className={'agreement'}>
          Please read the&nbsp;
          <Balloon
            align={'r'}
            triggerType={'click'}
            closable={false}
            trigger={
              i18n.t('Terms and Conditions')
            }
          >
            {tc}
          </Balloon>
          &nbsp;carefully before you start to use this app
        </div>
      );
    }
  };

  return (
    <div className={'welcome-page'}>
      <div className="main">
        <div className="name">
          <div className="title">Inside World</div>
          <IceLabel inverse={false} status={'info'}>v{info.coreVersion}</IceLabel>
        </div>
        {renderTerms()}
        <div className="opt">
          <Button
            type={'primary'}
            onClick={() => {
              AcceptTerms().invoke((a) => {
                history.push('/');
              });
            }}
          >{i18n.t('Accept and start to use')}
          </Button>
        </div>
      </div>
    </div>
  );
};
