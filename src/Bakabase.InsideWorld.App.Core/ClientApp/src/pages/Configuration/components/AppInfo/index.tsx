import { Balloon, Box, Button, Divider, Icon, Message, Progress, Table } from '@alifd/next';
import i18n from 'i18next';
import React, { useEffect, useRef, useState } from 'react';
import {
  GetAppUpdaterState,
  GetNewAppVersion,
  GetUpdaterUpdaterState,
  OpenFileOrDirectory,
  RestartAndUpdateApp,
  StartUpdatingApp,
  StartUpdatingUpdater,
} from '@/sdk/apis';
import { UpdaterStatus } from '@/sdk/constants';
import ExternalLink from '@/components/ExternalLink';
import { bytesToSize } from '@/components/utils';
import Title from '@/components/Title';
import CustomIcon from '@/components/CustomIcon';

export default ({ appInfo }) => {
  const [appUpdaterState, setAppUpdaterState] = useState({});
  const [newVersion, setNewVersion] = useState();
  const [updaterUpdaterState, setUpdaterUpdaterState] = useState({});
  const updaterUpdaterStateRef = useRef<any>({});
  const checkingUpdaterStatusHandlerRef = useRef<NodeJS.Timer>();
  const gettingStateRef = useRef(false);
  const checkNewAppVersion = () => {
    GetNewAppVersion().invoke((a) => {
      setNewVersion(a.data || {});
    });
  };

  useEffect(() => {
    updaterUpdaterStateRef.current = updaterUpdaterState;
  }, [updaterUpdaterState]);

  useEffect(() => {
    checkingUpdaterStatusHandlerRef.current = setInterval(() => {
      if (gettingStateRef.current) {
        return;
      }
      gettingStateRef.current = true;
      if (updaterUpdaterStateRef.current.status == UpdaterStatus.UpToDate) {
        GetAppUpdaterState().invoke((a) => {
          setAppUpdaterState(a.data || {});
          // setAppUpdaterState({
          //   percentage: 60,
          //   isUpdating: true,
          //   pendingRestart: true,
          //   totalFileCount: 100,
          //   downloadedFileCount: 70,
          // });
          gettingStateRef.current = false;
        });
      } else {
        GetUpdaterUpdaterState().invoke((a) => {
          setUpdaterUpdaterState(a.data || {});
          gettingStateRef.current = false;
        });
      }
    }, 1000);

    checkNewAppVersion();

    return () => {
      clearInterval(checkingUpdaterStatusHandlerRef.current);
    };
  }, []);

  const renderNewVersion = () => {
    // console.log(updaterUpdaterState.status);
    switch (updaterUpdaterState.status) {
      case UpdaterStatus.UpToDate:
        switch (appUpdaterState.status) {
          case UpdaterStatus.UpToDate:
            return i18n.t('Up-to-date');
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
                    >{i18n.t('Click to auto-update')}
                    </Button>
                    {
                      newVersion.installers?.length > 0 ? (
                        <>
                          <Divider direction={'ver'} />
                          <Balloon triggerType={'click'} align={'t'} trigger={<Button text type={'primary'}>{i18n.t('Auto-Update Fails? Click to download complete installers')}</Button>}>
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
                return i18n.t('Up-to-date');
              }
            } else {
              return i18n.t('Failed to get latest version');
            }
          case UpdaterStatus.Running:
            return (
              <div style={{ display: 'flex', alignItems: 'center' }}>
                {newVersion.version}
                &nbsp;
                {i18n.t('Updating')}
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
              >{i18n.t('Restart to update')}
              </Button>
            );
          case UpdaterStatus.Failed:
            return (
              <>
                {i18n.t('Failed to update app')}: {i18n.t(appUpdaterState.error)}
                &nbsp;
                <Button
                  text
                  type={'primary'}
                  onClick={() => {
                    StartUpdatingApp().invoke();
                  }}
                >{i18n.t('Click here to retry')}
                </Button>
              </>
            );
          default:
            return (
              <Icon type={'loading'} />
            );
        }
      case UpdaterStatus.Running:
        return (
          <>
            <Icon type={'loading'} /> {i18n.t('Downloading updater')}
          </>
        );
      case UpdaterStatus.Idle:
      case UpdaterStatus.Failed:
        return (
          <>
            {i18n.t('Failed to get updater')}: {i18n.t(updaterUpdaterState.error)}
            &nbsp;
            <Button
              text
              type={'primary'}
              onClick={() => {
                Message.notice(i18n.t('Start updating'));
                StartUpdatingUpdater().invoke();
              }}
            >{i18n.t('Click here to retry')}
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
      label: 'Updater Path',
      tip: 'Updater files for auto-updating, DO NOT delete any of them.',
      value: <Button text type={'primary'} onClick={() => OpenFileOrDirectory({ path: appInfo.updaterPath }).invoke()}>{appInfo.updaterPath}</Button>,
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
  ].map((t) => ({ ...t, label: i18n.t(t.label), tip: i18n.t(t.tip) }));

  return (
    <div className="group">
      <Title title={i18n.t('System information')} />
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
                  {i18n.t(l)}
                  {r.tip && (
                    <>
                      &nbsp;
                      <Balloon.Tooltip
                        align={'r'}
                        trigger={<CustomIcon type={'question-circle'} />}
                      >
                        {i18n.t(r.tip)}
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
