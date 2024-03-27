import React, { useEffect, useState } from 'react';
import { Balloon, Button } from '@alifd/next';
import { history } from 'ice';
import { useTranslation } from 'react-i18next';
import styles from './index.module.scss';
import SimpleLabel from '@/components/SimpleLabel';
import BApi from '@/sdk/BApi';

export default () => {
  const [version, setVersion] = useState<string>();
  const { t } = useTranslation();

  useEffect(() => {
    BApi.app.getAppInfo().then(a => {
      setVersion(a.data?.coreVersion ?? '0.0.0');
    });
  }, []);

  return (
    <div className={styles.welcomePage}>
      <div className={styles.main}>
        <div className={styles.name}>
          <div className={styles.title}>Inside World</div>
          <SimpleLabel>v{version}</SimpleLabel>
        </div>
        <div className={styles.agreement}>
          {t('Please read terms and conditions carefully before you start to use this app')}
          &nbsp;
          <Balloon
            align={'t'}
            triggerType={'click'}
            closable={false}
            trigger={(
              <span>
                {t('Click to check')}
              </span>
            )}
            v2
          >
            <div style={{ width: 400 }}>
              {t('We will do some data tracking, the anonymous data will help us to improve our product experience, and no personal data will be collected.')}
              <br />
              {t('You can disable it in system settings.')}
            </div>
          </Balloon>

        </div>
        <div className={styles.opt}>
          <Button
            type={'primary'}
            onClick={() => {
              BApi.app.acceptTerms().then((a) => {
                history!.push('/');
              });
            }}
          >{t('Accept and start to use')}
          </Button>
        </div>
      </div>
    </div>
  );
};
