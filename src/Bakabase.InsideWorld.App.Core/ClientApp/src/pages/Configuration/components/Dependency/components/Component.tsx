import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Button, Icon } from '@alifd/next';
import { usePrevious } from 'react-use';
import BApi from '@/sdk/BApi';
import SimpleLabel from '@/components/SimpleLabel';
import CustomIcon from '@/components/CustomIcon';
import store from '@/store';
import { DependentComponentStatus } from '@/sdk/constants';

export default ({ id }: { id: string }) => {
  const { t } = useTranslation();
  const context = store.useModelState('dependentComponentContexts').find(a => a.id == id);
  const [latestVersion, setLatestVersion] = useState<{version: string; canUpdate: boolean}>();
  // const prevInstallationProgress = usePrevious(context);
  const [discovering, setDiscovering] = useState(true);

  useEffect(() => {

  }, [context]);

  const init = useCallback(async () => {
    try {
      await BApi.component.discoverDependentComponent({ id });
    } finally {
      setDiscovering(false);
    }

    const latestVersionRsp = await BApi.component.getDependentComponentLatestVersion({ id });
    // @ts-ignore
    setLatestVersion(latestVersionRsp.data);
  }, []);

  useEffect(() => {
    init();
  }, []);

  const renderNewVersionInner = useCallback(() => {
    if (!latestVersion) {
      return (
        <Icon type={'loading'} size={'small'} />
      );
    } else {
      if (!latestVersion.canUpdate) {
        return (
          <CustomIcon style={{ color: 'green' }} type={'check-circle'} />
        );
      } else {
        if (context && context.status == DependentComponentStatus.Installing) {
          return (
            <>
              {t('Updating')}: {context.installationProgress}%
              <Icon type={'loading'} size={'small'} />
            </>
          );
        } else {
          return (
            <>
              {latestVersion.canUpdate && (
                <Button
                  text
                  type={'primary'}
                  size={'small'}
                  onClick={() => {
                    BApi.component.installDependentComponent({ id });
                  }}
                >
                  {t('Click to update to version')}: {latestVersion.version}
                </Button>
              )}
              {context?.error && (
                <span style={{
                  color: 'red',
                  marginLeft: 5,
                }}
                >{context.error}</span>
              )}
            </>
          );
        }
      }
    }
  }, [latestVersion, context, discovering]);

  return (
    <div
      className={'third-party-component'}
      style={{
      display: 'flex',
      gap: 10,
      alignItems: 'center',
    }}
    >
      <div className={'installed'}>
        {
          discovering ? (
            <Icon type={'loading'} size={'small'} />
          ) : (
            <SimpleLabel title={context?.location ?? undefined}>{context?.version ?? t('Not installed')}</SimpleLabel>
          )
        }
      </div>
      {!discovering && (
        <div className="new-version" style={{ display: 'flex', alignItems: 'center', gap: 5 }}>
          {renderNewVersionInner()}
        </div>
      )}
    </div>
  );
};
