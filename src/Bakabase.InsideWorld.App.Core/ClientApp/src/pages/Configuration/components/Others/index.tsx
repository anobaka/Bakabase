import i18n from 'i18next';
import { Balloon, Dialog, Input, Message, Radio, Switch, Table } from '@alifd/next';
import React, { useEffect, useState } from 'react';
import Cookies from 'universal-cookie';
import { useTranslation } from 'react-i18next';
import Title from '@/components/Title';
import CustomIcon from '@/components/CustomIcon';
import { MoveCoreData, PatchAppOptions, PatchThirdPartyOptions } from '@/sdk/apis';
import FileSelector from '@/components/FileSelector';
import store from '@/store';
import BApi from '@/sdk/BApi';
import { UiTheme, uiThemes } from '@/sdk/constants';

const cookies = new Cookies();

export default ({
                  applyPatches = () => {
                  },
                }: { applyPatches: (API: any, patches: any, success: (rsp: any) => void) => void }) => {
  const { t } = useTranslation();
  const [appOptions, appOptionsDispatcher] = store.useModel('appOptions');
  const thirdPartyOptions = store.useModelState('thirdPartyOptions');
  const networkOptions = store.useModelState('networkOptions');

  const [proxy, setProxy] = useState(networkOptions.proxy);
  useEffect(() => {
    setProxy(networkOptions.proxy);
  }, [networkOptions]);

  const otherSettings = [
    {
      label: 'Theme',
      value: appOptions.uiTheme,
      renderValue: () => (
        <Radio.Group
          // shape="button"
          size={'small'}
          dataSource={[{ label: t('Follow system'), value: UiTheme.FollowSystem }, { label: t('Light mode'), value: UiTheme.Light }, { label: `${t('Dark mode')}(beta)`, value: UiTheme.Dark }]}
          value={appOptions.uiTheme}
          onChange={(uiTheme) => {
            PatchAppOptions({
              model: {
                uiTheme,
              },
            }).invoke((a) => {
              location.reload();
            });
          }}
        />
      ),
    },
    {
      label: 'Language',
      value: cookies.get('lng'),
      renderValue: () => (
        <Radio.Group
          shape="button"
          size={'small'}
          dataSource={[{ label: '中文', value: 'cn' }, { label: 'English', value: 'en' }]}
          value={appOptions.language}
          onChange={(language) => {
            PatchAppOptions({
              model: {
                language,
              },
            }).invoke((a) => {
              location.reload();
            });
          }}
        />
      ),
    },
    {
      label: 'Proxy',
      tip: 'You can set a proxy for network requests, such as socks5://127.0.0.1:18888',
      renderValue: () => {
        return (
          <Input
            size={'small'}
            value={proxy?.address}
            onChange={v => {
              setProxy({
                address: v,
              });
            }}
            onBlur={() => {
              BApi.options.patchNetworkOptions({
                proxy,
              }).then(t => {
                if (!t.code) {
                  Message.success(i18n.t('Saved'));
                }
              });
            }}
          />
        );
      },
    },
    // {
    //   label: 'FFmpeg bin directory',
    //   tip: 'You can download binary files from https://ffmpeg.org/download.html, and make sure you have ffprobe.exe and ffmpeg.exe in your directory.',
    //   renderValue: () => {
    //     return (
    //       <FileSelector
    //         size={'small'}
    //         value={thirdPartyOptions.fFmpeg?.binDirectory}
    //         type={'folder'}
    //         // multiple
    //         onChange={(path) => {
    //           if (path) {
    //             applyPatches(PatchThirdPartyOptions, {
    //               fFmpeg: {
    //                 ...(thirdPartyOptions.fFmpeg || {}),
    //                 binDirectory: path,
    //               },
    //             });
    //           }
    //         }}
    //       />
    //     );
    //   },
    // },
    {
      label: 'Enable pre-release channel',
      tip: 'Prefer pre-release version which has new features but less stability',
      renderValue: () => {
        return (
          <Switch
            size={'small'}
            checked={appOptions.enablePreReleaseChannel}
            onChange={(checked) => {
              applyPatches(PatchAppOptions, {
                enablePreReleaseChannel: checked,
              }, () => {
              });
            }}
          />
        );
      },
    },
    {
      label: 'Enable anonymous data tracking',
      tip: 'The anonymous data will help us to improve our product experience, and no personal data will be collected',
      renderValue: () => {
        return (
          <Switch
            size={'small'}
            checked={appOptions.enableAnonymousDataTracking}
            onChange={(checked) => {
              applyPatches(PatchAppOptions, {
                enableAnonymousDataTracking: checked,
              }, () => {
              });
            }}
          />
        );
      },
    },
    {
      label: 'Move core data',
      tip: 'Move core data to another directory',
      renderValue: () => {
        return (
          <FileSelector
            size={'small'}
            value={appOptions.dataPath}
            type={'folder'}
            // multiple
            onChange={(path) => {
              if (path) {
                const dialog = Dialog.show({
                  title: i18n.t('Moving files'),
                  closeable: false,
                  closeMode: [],
                  footer: false,
                });
                MoveCoreData({
                  model: {
                    dataPath: path,
                  },
                }).invoke((a) => {
                  if (!a.code) {
                    Dialog.show({
                      title: i18n.t('Moving files successfully, please restart app'),
                      closeable: false,
                      closeMode: [],
                      footer: false,
                    });
                  }
                }).finally(() => {
                  dialog.hide();
                });
              }
            }}
          />
        );
      },
    },
  ];

  return (
    <div className="group">
      <Title title={i18n.t('Other settings')} />
      <div className="settings">
        <Table
          dataSource={otherSettings}
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
            title={i18n.t('Other setting')}
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
          <Table.Column
            dataIndex={'renderValue'}
            title={i18n.t('Value')}
            cell={(render, i, r) => (render ? render() : r.value)}
          />
        </Table>
      </div>
    </div>
  );
};
