import { Balloon, Box, Button, Divider, Icon, Message, Progress, Table } from '@alifd/next';
import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  GetNewAppVersion,
  OpenFileOrDirectory,
  RestartAndUpdateApp,
  StartUpdatingApp,
  StartUpdatingUpdater,
} from '@/sdk/apis';
import { DependentComponentStatus, UpdaterStatus } from '@/sdk/constants';
import ExternalLink from '@/components/ExternalLink';
import { bytesToSize } from '@/components/utils';
import Title from '@/components/Title';
import CustomIcon from '@/components/CustomIcon';
import store from '@/store';
import DependentComponentIds from '@/core/models/Constants/DependentComponentIds';

export default ({ appInfo }) => {
  const { t } = useTranslation();
  const [newVersion, setNewVersion] = useState();
  const appUpdaterState = store.useModelState('appUpdaterState');
  // const appUpdaterState = {
  //   status: UpdaterStatus.Running,
  //   percentage: 10,
  //   error: '1232',
  // };
  const updaterContext = store.useModelState('dependentComponentContexts')?.find(s => s.id == DependentComponentIds.BakabaseUpdater);
  const checkNewAppVersion = () => {
    GetNewAppVersion().invoke((a) => {
      setNewVersion(a.data || {});
    });
  };

  useEffect(() => {
    checkNewAppVersion();

    return () => {
    };
  }, []);

  const renderNewVersion = () => {
    if (!updaterContext || updaterContext.status == DependentComponentStatus.NotInstalled) {
      return t('Updater is required to auto-update app');
    }

    if (updaterContext.status == DependentComponentStatus.Installing) {
      return t('We\'re installing updater, please wait');
    }

    switch (appUpdaterState.status) {
      case UpdaterStatus.UpToDate:
        return t('Up-to-date');
      case UpdaterStatus.Idle:
        if (newVersion) {
          if (newVersion.version) {
            return (
              <Box direction={'row'} style={{ alignItems: 'center' }}>
                {newVersion.version}
                <Divider direction={'ver'} />
                <Button
                  onClick={() => {
                    StartUpdatingApp().invoke();
                  }}
                  type={'primary'}
                  size={'small'}
                >{t('Click to auto-update')}
                </Button>
                {
                  newVersion.installers?.length > 0 ? (
                    <>
                      <Divider direction={'ver'} />
                      <Balloon triggerType={'click'} align={'t'} trigger={<Button text type={'primary'}>{t('Auto-Update Fails? Click to download complete installers')}</Button>}>
                        {newVersion.installers.map((i) => (
                          <div key={i.url}>
                            <ExternalLink to={i.url} >{i.name}({bytesToSize(i.size)})</ExternalLink>
                          </div>
                        ))}
                      </Balloon>
                    </>
                  ) : undefined
                }
              </Box>
            );
          } else {
            return t('Up-to-date');
          }
        } else {
          return t('Failed to get latest version');
        }
      case UpdaterStatus.Running:
        return (
          <div style={{ display: 'flex', alignItems: 'center' }}>
            {newVersion?.version}
            &nbsp;
            {t('Updating')}
            &nbsp;
            <div style={{ width: 400 }}>
              <Progress progressive percent={appUpdaterState.percentage} />
            </div>
          </div>
        );
      case UpdaterStatus.PendingRestart:
        return (
          <Button
            size={'small'}
            type={'primary'}
            onClick={() => {
              RestartAndUpdateApp().invoke();
            }}
          >{t('Restart to update')}
          </Button>
        );
      case UpdaterStatus.Failed:
        return (
          <>
            {t('Failed to update app')}: {t(appUpdaterState.error)}
            &nbsp;
            <Button
              text
              type={'primary'}
              onClick={() => {
                StartUpdatingApp().invoke();
              }}
            >{t('Click here to retry')}
            </Button>
          </>
        );
      default:
        return (
          <Icon type={'loading'} />
        );
    }
  };
  const buildAppInfoDataSource = () => [
    {
      label: 'App Data Path',
      tip: 'This is where core data files stored and DO NOT change them if not necessary.',
      value: <Button text type={'primary'} onClick={() => OpenFileOrDirectory({ path: appInfo.appDataPath }).invoke()}>{appInfo.appDataPath}</Button>,
    },
    {
      label: 'Temporary files path',
      tip: 'It\'s a directory where temporary files stored, such as cover files, etc.',
      value: <Button text type={'primary'} onClick={() => OpenFileOrDirectory({ path: appInfo.tempFilesPath }).invoke()}>{appInfo.tempFilesPath}</Button>,
    },
    {
      label: 'Log Path',
      tip: 'Detailed information which describing the running states of app.' +
        ' You can send log files to developer if the app is not running normally, and you can delete them also if everything is ok.',
      value: <Button text type={'primary'} onClick={() => OpenFileOrDirectory({ path: appInfo.logPath }).invoke()}>{appInfo.logPath}</Button>,
    },
    {
      label: 'Backup Path',
      tip: 'A data backup will be created when using the new version of app first time, you can delete them if everything is ok.',
      value: <Button text type={'primary'} onClick={() => OpenFileOrDirectory({ path: appInfo.backupPath }).invoke()}>{appInfo.backupPath}</Button>,
    },
    {
      label: 'Core Version',
      value: appInfo.coreVersion,
    },
    {
      label: 'Latest version',
      value: renderNewVersion(),
    },
  ].map((x) => ({ ...x, label: t(x.label), tip: t(x.tip) }));

  return (
    <div className="group">
      <Title title={t('System information')} />
      <div className="settings">
        <Table
          dataSource={buildAppInfoDataSource()}
          size={'small'}
          hasHeader={false}
          cellProps={(r, c) => {
            return {
              className: c == 0 ? 'key' : c == 1 ? 'value' : '',
            };
          }}
        >
          <Table.Column
            dataIndex={'label'}
            width={300}
            cell={(l, i, r) => {
              return (
                <div style={{ display: 'flex', alignItems: 'center' }}>
                  {t(l)}
                  {r.tip && (
                    <>
                      &nbsp;
                      <Balloon.Tooltip
                        align={'r'}
                        trigger={<CustomIcon type={'question-circle'} />}
                      >
                        {t(r.tip)}
                      </Balloon.Tooltip>
                    </>
                  )}
                </div>
              );
            }}
          />
          <Table.Column dataIndex={'value'} />
        </Table>
      </div>
    </div>
  );
};
