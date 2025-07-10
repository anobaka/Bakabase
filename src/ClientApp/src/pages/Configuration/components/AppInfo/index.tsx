import { Balloon, Box, Divider, Icon, Message, Progress } from '@alifd/next';
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
import {
  Button,
  Table,
  TableRow,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  Snippet,
  Tooltip, Chip, Modal,
} from '@/components/bakaui';
import BApi from '@/sdk/BApi';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import type { components } from '@/sdk/BApi2';


type Version = components['schemas']['Bakabase.Infrastructures.Components.App.Upgrade.Abstractions.AppVersionInfo'];

export default ({ appInfo }) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [newVersion, setNewVersion] = useState<Version>();
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
    // if (!updaterContext || updaterContext.status == DependentComponentStatus.NotInstalled) {
    //   return t('Updater is required to auto-update app');
    // }

    // if (updaterContext.status == DependentComponentStatus.Installing) {
    //   return t('We\'re installing updater, please wait');
    // }

    switch (appUpdaterState.status) {
      case UpdaterStatus.UpToDate:
        return t('Up-to-date');
      case UpdaterStatus.Idle:
        if (newVersion) {
          if (newVersion.version) {
            return (
              <Box direction={'row'} style={{ alignItems: 'center' }}>
                <Chip radius={'sm'} variant={'light'}>{newVersion.version}</Chip>
                {newVersion.changelog && (
                  <>
                    <Divider direction={'ver'} />
                    <Button
                      onPress={() => {
                        createPortal(Modal, {
                          size: 'xl',
                          title: newVersion.version,
                          defaultVisible: true,
                          children: (
                            <pre>
                              {newVersion.changelog}
                            </pre>
                          ),
                          footer: { actions: ['cancel'] },
                        });
                      }}
                      variant={'light'}
                      color={'secondary'}
                      size={'small'}
                    >{t('Change log')}
                    </Button>
                  </>
                )}
                <Divider direction={'ver'} />
                <Button
                  onClick={() => {
                    StartUpdatingApp().invoke();
                  }}
                  variant={'light'}
                  color={'success'}
                  size={'small'}
                >{t('Click to auto-update')}
                </Button>
                {
                  newVersion.installers?.length > 0 ? (
                    <>
                      <Divider direction={'ver'} />
                      <Balloon
                        triggerType={'click'}
                        align={'t'}
                        trigger={<Button
                          variant={'light'}
                          size={'sm'}
                          color={'primary'}
                        >{t('Auto-Update Fails? Click to download complete installers')}</Button>}
                      >
                        {newVersion.installers.map((i) => (
                          <div key={i.url}>
                            <ExternalLink href={i.url} >{i.name}({bytesToSize(i.size)})</ExternalLink>
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
      value: <Button color={'primary'} variant={'light'} onClick={() => OpenFileOrDirectory({ path: appInfo.appDataPath }).invoke()}>{appInfo.appDataPath}</Button>,
      // value: <Snippet hideSymbol>{appInfo.appDataPath}</Snippet>,
    },
    {
      label: 'Temporary files path',
      tip: 'It\'s a directory where temporary files stored, such as cover files, etc.',
      value: <Button color={'primary'} variant={'light'} onClick={() => OpenFileOrDirectory({ path: appInfo.tempFilesPath }).invoke()}>{appInfo.tempFilesPath}</Button>,
    },
    {
      label: 'Log Path',
      tip: 'Detailed information which describing the running states of app.' +
        ' You can send log files to developer if the app is not running normally, and you can delete them also if everything is ok.',
      value: <Button color={'primary'} variant={'light'} onClick={() => OpenFileOrDirectory({ path: appInfo.logPath }).invoke()}>{appInfo.logPath}</Button>,
    },
    {
      label: 'Backup Path',
      tip: 'A data backup will be created when using the new version of app first time, you can delete them if everything is ok.',
      value: <Button color={'primary'} variant={'light'} onClick={() => OpenFileOrDirectory({ path: appInfo.backupPath }).invoke()}>{appInfo.backupPath}</Button>,
    },
    {
      label: 'Core Version',
      value: (<Chip radius={'sm'} variant={'light'}>{appInfo.coreVersion}</Chip>),
    },
    {
      label: 'Latest version',
      value: renderNewVersion(),
    },
  ].map((x) => ({ ...x, label: t(x.label), tip: t(x.tip) }));

  return (
    <div className="group">
      {/* <Title title={t('System information')} /> */}
      <div className="settings">
        <Table
          removeWrapper
          isCompact
        >
          <TableHeader>
            <TableColumn width={200}>{t('System information')}</TableColumn>
            <TableColumn>&nbsp;</TableColumn>
          </TableHeader>
          <TableBody>
            {buildAppInfoDataSource().map((c, i) => {
              return (
                <TableRow key={i} className={'hover:bg-[var(--bakaui-overlap-background)]'}>
                  <TableCell>
                    <div style={{ display: 'flex', alignItems: 'center' }}>
                      {t(c.label)}
                      {c.tip && (
                        <>
                          &nbsp;
                          <Tooltip
                            placement={'right'}
                            content={t(c.tip)}
                          >
                            <CustomIcon type={'question-circle'} className={'text-base'} />
                          </Tooltip>
                        </>
                      )}
                    </div>
                  </TableCell>
                  <TableCell>
                    {c.value}
                  </TableCell>
                </TableRow>
              );
            })}

          </TableBody>
        </Table>
      </div>
    </div>
  );
};
