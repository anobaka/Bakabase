import i18n from 'i18next';
import { Balloon, Dialog, Message, Switch } from '@alifd/next';
import React, { useEffect, useState } from 'react';
import Cookies from 'universal-cookie';
import { useTranslation } from 'react-i18next';
import type { Key } from '@react-types/shared';
import Title from '@/components/Title';
import CustomIcon from '@/components/CustomIcon';
import { MoveCoreData, PatchAppOptions } from '@/sdk/apis';
import FileSelector from '@/components/FileSelector';
import store from '@/store';
import BApi from '@/sdk/BApi';
import {
  Button,
  Select,
  Notification,
  Modal,
  Input,
  TableHeader,
  TableColumn,
  TableBody,
  TableRow, TableCell, Tooltip,
  Table,
} from '@/components/bakaui';
import type { BakabaseInsideWorldModelsRequestModelsOptionsNetworkOptionsPatchInputModel } from '@/sdk/Api';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import FeatureStatusTip from '@/components/FeatureStatusTip';

const cookies = new Cookies();

enum ProxyMode {
  DoNotUse = 0,
  UseSystem = 1,
  UseCustom = 2,
}

export default ({
                  applyPatches = () => {
                  },
                }: { applyPatches: (API: any, patches: any, success: (rsp: any) => void) => void }) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [appOptions, appOptionsDispatcher] = store.useModel('appOptions');
  const thirdPartyOptions = store.useModelState('thirdPartyOptions');
  const networkOptions = store.useModelState('networkOptions');

  const [proxy, setProxy] = useState(networkOptions.proxy);
  useEffect(() => {
    setProxy(networkOptions.proxy);
  }, [networkOptions]);

  const proxies = [
    {
      label: t('Do not use proxy'),
      value: ProxyMode.DoNotUse.toString(),
    },
    {
      label: t('Use system proxy'),
      value: ProxyMode.UseSystem.toString(),
    },
    ...(networkOptions.customProxies?.map(c => ({
      label: c.address!,
      value: c.id!,
    })) ?? []),
  ];

  let selectedProxy: Key | undefined;
  if (networkOptions?.proxy) {
    const p = networkOptions.proxy;
    if (p.mode == ProxyMode.UseCustom) {
      selectedProxy = p.customProxyId!;
    } else {
      selectedProxy = p.mode?.toString();
    }
  }

  selectedProxy ??= ProxyMode.DoNotUse.toString();

  // console.log('xxxxxx', selectedProxy, proxies);

  const otherSettings = [
    {
      label: 'Proxy',
      tip: 'You can set a proxy for network requests, such as socks5://127.0.0.1:18888',
      renderValue: () => {
        return (
          <div className={'flex items-center gap-2'}>
            <div style={{ width: 300 }}>
              <Select
                multiple={false}
                dataSource={proxies}
                selectedKeys={selectedProxy == undefined ? undefined : [selectedProxy]}
                size={'sm'}
                onSelectionChange={keys => {
                  const key = Array.from(keys)[0] as string;
                  const patches: BakabaseInsideWorldModelsRequestModelsOptionsNetworkOptionsPatchInputModel = {};
                  if (key == ProxyMode.DoNotUse.toString()) {
                    patches.proxy = {
                      mode: ProxyMode.DoNotUse,
                      customProxyId: undefined,
                    };
                  } else {
                    if (key == ProxyMode.UseSystem.toString()) {
                      patches.proxy = {
                        mode: ProxyMode.UseSystem,
                        customProxyId: undefined,
                      };
                    } else {
                      patches.proxy = {
                        mode: ProxyMode.UseCustom,
                        customProxyId: key,
                      };
                    }
                  }
                  console.log(key, keys, patches);
                  BApi.options.patchNetworkOptions(patches).then(x => {
                    if (!x.code) {
                      Notification.success(t('Saved'));
                    }
                  });
                }}
              />
            </div>

            <Button
              size={'sm'}
              color={'primary'}
              onClick={() => {
                let p: string;
                createPortal(Modal, {
                  defaultVisible: true,
                  size: 'lg',
                  title: t('Add a proxy'),
                  children: (
                    <Input
                      placeholder={t('You can set a proxy for network requests, such as socks5://127.0.0.1:18888')}
                      onValueChange={v => p = v}
                    />
                  ),
                  onOk: async () => {
                    if (p == undefined || p.length == 0) {
                      Notification.error(t('Invalid Data'));
                      throw new Error('Invalid data');
                    }
                    await BApi.options.patchNetworkOptions({
                      customProxies: [
                        ...(networkOptions.customProxies ?? []),
                        { address: p },
                      ],
                    });
                  },
                });
              }}
            >
              {t('Add')}
            </Button>
          </div>
          // <Input
          //   size={'small'}
          //   value={proxy?.address}
          //   onChange={v => {
          //     setProxy({
          //       address: v,
          //     });
          //   }}
          //   onBlur={() => {
          //     BApi.options.patchNetworkOptions({
          //       proxy,
          //     }).then(t => {
          //       if (!t.code) {
          //         Message.success(i18n.t('Saved'));
          //       }
          //     });
          //   }}
          // />
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
          <FeatureStatusTip status={'deprecated'} name={t('Move core data')} />
          // <FileSelector
          //   size={'small'}
          //   value={appOptions.dataPath}
          //   type={'folder'}
          //   // multiple
          //   onChange={(path) => {
          //     if (path) {
          //       const dialog = Dialog.show({
          //         title: i18n.t('Moving files'),
          //         closeable: false,
          //         closeMode: [],
          //         footer: false,
          //       });
          //       MoveCoreData({
          //         model: {
          //           dataPath: path,
          //         },
          //       }).invoke((a) => {
          //         if (!a.code) {
          //           Dialog.show({
          //             title: i18n.t('Moving files successfully, please restart app'),
          //             closeable: false,
          //             closeMode: [],
          //             footer: false,
          //           });
          //         }
          //       }).finally(() => {
          //         dialog.hide();
          //       });
          //     }
          //   }}
          // />
        );
      },
    },
  ];

  return (
    <div className="group">
      {/* <Title title={i18n.t('Other settings')} /> */}
      <div className="settings">
        <Table
          removeWrapper
        >
          <TableHeader>
            <TableColumn width={200}>{t('Other settings')}</TableColumn>
            <TableColumn>&nbsp;</TableColumn>
          </TableHeader>
          <TableBody>
            {otherSettings.map((c, i) => {
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
                    {c.renderValue()}
                  </TableCell>
                </TableRow>
              );
            })}

          </TableBody>
        </Table>
        {/* <Table */}
        {/*   dataSource={otherSettings} */}
        {/*   size={'small'} */}
        {/*   hasHeader={false} */}
        {/*   cellProps={(r, c) => { */}
        {/*     return { */}
        {/*       className: c == 0 ? 'key' : c == 1 ? 'value' : '', */}
        {/*     }; */}
        {/*   }} */}
        {/* > */}
        {/*   <Table.Column */}
        {/*     dataIndex={'label'} */}
        {/*     width={300} */}
        {/*     title={i18n.t('Other setting')} */}
        {/*     cell={(l, i, r) => { */}
        {/*       return ( */}
        {/*         <div style={{ */}
        {/*           display: 'flex', */}
        {/*           alignItems: 'center', */}
        {/*         }} */}
        {/*         > */}
        {/*           {i18n.t(l)} */}
        {/*           {r.tip && ( */}
        {/*             <> */}
        {/*               &nbsp; */}
        {/*               <Balloon.Tooltip */}
        {/*                 align={'r'} */}
        {/*                 trigger={<CustomIcon type={'question-circle'} />} */}
        {/*               > */}
        {/*                 {i18n.t(r.tip)} */}
        {/*               </Balloon.Tooltip> */}
        {/*             </> */}
        {/*           )} */}
        {/*         </div> */}
        {/*       ); */}
        {/*     }} */}
        {/*   /> */}
        {/*   <Table.Column */}
        {/*     dataIndex={'renderValue'} */}
        {/*     title={i18n.t('Value')} */}
        {/*     cell={(render, i, r) => (render ? render() : r.value)} */}
        {/*   /> */}
        {/* </Table> */}
      </div>
    </div>
  );
};
