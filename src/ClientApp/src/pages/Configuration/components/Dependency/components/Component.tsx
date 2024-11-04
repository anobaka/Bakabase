import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Button, Dialog, Icon } from '@alifd/next';
import { usePrevious } from 'react-use';
import { CheckCircleOutlined } from '@ant-design/icons';
import BApi from '@/sdk/BApi';
import SimpleLabel from '@/components/SimpleLabel';
import CustomIcon from '@/components/CustomIcon';
import store from '@/store';
import { DependentComponentStatus } from '@/sdk/constants';
import ClickableIcon from '@/components/ClickableIcon';
import { Chip } from '@/components/bakaui';

export default ({ id }: { id: string }) => {
  const { t } = useTranslation();
  const context = store.useModelState('dependentComponentContexts').find(a => a.id == id);
  const [latestVersion, setLatestVersion] = useState<{ version?: string; canUpdate: boolean; error?: string | null }>();
  // const prevInstallationProgress = usePrevious(context);
  const [discovering, setDiscovering] = useState(true);
  const [findingNewVersion, setFindingNewVersion] = useState(false);

  const prevStatus = usePrevious(context?.status);

  useEffect(() => {
    if (context?.status == DependentComponentStatus.Installed && prevStatus == DependentComponentStatus.Installing) {
      init();
    }
  }, [context]);

  const init = useCallback(async () => {
    try {
      await BApi.component.discoverDependentComponent({ id });
    } finally {
      setDiscovering(false);
    }

    if (context?.isRequired || context?.status == DependentComponentStatus.NotInstalled) {
      setFindingNewVersion(true);
      try {
        const latestVersionRsp = await BApi.component.getDependentComponentLatestVersion({ id });
        if (!latestVersionRsp.code) {
          // @ts-ignore
          setLatestVersion(latestVersionRsp.data);
        } else {
          setLatestVersion({
            canUpdate: false,
            error: latestVersionRsp.message,
          });
        }
      } catch (e) {
        setLatestVersion({
          canUpdate: false,
          error: e.toString(),
        });
      } finally {
        setFindingNewVersion(false);
      }
    }
  }, []);

  useEffect(() => {
    init();
  }, []);

  console.log(context?.name, latestVersion, discovering, context);

  const renderNewVersionInner = useCallback(() => {
    const elements: any[] = [];

    // new version
    if (latestVersion) {
      if (latestVersion.error) {
        elements.push(
          <ClickableIcon
            type={'error'}
            colorType={'danger'}
            useInBuildIcon
            onClick={() => {
              Dialog.error({
                title: t('Failed to get information of new version'),
                content: latestVersion.error,
                v2: true,
                width: 'auto',
                closeMode: ['close', 'esc', 'mask'],
              });
            }}
          />,
        );
      } else {
        if (latestVersion.canUpdate) {
          if (context?.status != DependentComponentStatus.Installing) {
            elements.push(
              <Button
                text
                type={'primary'}
                size={'small'}
                onClick={() => {
                  BApi.component.installDependentComponent({ id });
                }}
              >
                {t('Click to update to version')}: {latestVersion.version}
              </Button>,
            );
          }
        } else {
          elements.push(
            <CheckCircleOutlined className={'text-base text-success'} />,
          );
        }
      }
    } else {
      if (findingNewVersion) {
        elements.push(
          <Icon type={'loading'} size={'small'} title={t('Checking new version')} />,
        );
      }
    }

    // current status
    if (context && context.status == DependentComponentStatus.Installing) {
      elements.push(
        <>
          {t('Updating')}: {context.installationProgress}%
          <Icon type={'loading'} size={'small'} />
        </>,
      );
    }
    if (context?.error) {
      elements.push(
        <ClickableIcon
          type={'error'}
          colorType={'danger'}
          useInBuildIcon
          onClick={() => {
            Dialog.error({
              title: t('Error'),
              content: context.error,
              v2: true,
              width: 'auto',
              closeMode: ['close', 'esc', 'mask'],
            });
          }}
        />,
      );
    }
    return elements;
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
            <Chip
              size={'sm'}
              radius={'sm'}
              title={context?.location ?? undefined}
            >{context?.version ?? t('Not installed')}</Chip>
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
