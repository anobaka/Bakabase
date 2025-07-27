"use client";

import type { Key } from "@react-types/shared";
import type { ChipProps, NumberInputProps } from "@/components/bakaui";
import type { BakabaseInsideWorldModelsRequestModelsOptionsNetworkOptionsPatchInputModel } from "@/sdk/Api";

import React, { useEffect, useState } from "react";
import Cookies from "universal-cookie";
import { useTranslation } from "react-i18next";
import toast from "react-hot-toast";
import { AiOutlineNumber, AiOutlineQuestionCircle } from "react-icons/ai";

import BApi from "@/sdk/BApi";
import {
  Button,
  Chip,
  Input,
  Modal,
  NumberInput,
  Select,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  Tooltip,
  Switch,
} from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { EditableValue } from "@/components/EditableValue";
import {
  useThirdPartyOptionsStore,
  useNetworkOptionsStore,
  useAppOptionsStore,
} from "@/models/options";
import { useAppContextStore } from "@/models/appContext";

const cookies = new Cookies();

enum ProxyMode {
  DoNotUse = 0,
  UseSystem = 1,
  UseCustom = 2,
}

export default ({
  applyPatches = () => {},
}: {
  applyPatches: (API: any, patches: any, success: (rsp: any) => void) => void;
}) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const appOptions = useAppOptionsStore((state) => state.data);
  const thirdPartyOptions = useThirdPartyOptionsStore((state) => state.data);
  const networkOptions = useNetworkOptionsStore((state) => state.data);
  const appContext = useAppContextStore((state) => state);

  const [proxy, setProxy] = useState(networkOptions.proxy);

  useEffect(() => {
    setProxy(networkOptions.proxy);
  }, [networkOptions]);

  const proxies = [
    {
      label: t<string>("Do not use proxy"),
      value: ProxyMode.DoNotUse.toString(),
    },
    {
      label: t<string>("Use system proxy"),
      value: ProxyMode.UseSystem.toString(),
    },
    ...(networkOptions.customProxies?.map((c) => ({
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
      label: "Proxy",
      tip: "You can set a proxy for network requests, such as socks5://127.0.0.1:18888",
      renderValue: () => {
        return (
          <div className={"flex items-center gap-2"}>
            <div style={{ width: 300 }}>
              <Select
                dataSource={proxies}
                multiple={false}
                selectedKeys={
                  selectedProxy == undefined ? undefined : [selectedProxy]
                }
                size={"sm"}
                onSelectionChange={(keys) => {
                  const key = Array.from(keys)[0] as string;
                  const patches: BakabaseInsideWorldModelsRequestModelsOptionsNetworkOptionsPatchInputModel =
                    {};

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
                  BApi.options.patchNetworkOptions(patches).then((x) => {
                    if (!x.code) {
                      toast.success(t<string>("Saved"));
                    }
                  });
                }}
              />
            </div>

            <Button
              color={"primary"}
              size={"sm"}
              onClick={() => {
                let p: string;

                createPortal(Modal, {
                  defaultVisible: true,
                  size: "lg",
                  title: t<string>("Add a proxy"),
                  children: (
                    <Input
                      placeholder={t<string>(
                        "You can set a proxy for network requests, such as socks5://127.0.0.1:18888",
                      )}
                      onValueChange={(v) => (p = v)}
                    />
                  ),
                  onOk: async () => {
                    if (p == undefined || p.length == 0) {
                      Notification.error(t<string>("Invalid Data"));
                      throw new Error("Invalid data");
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
              {t<string>("Add")}
            </Button>
          </div>
        );
      },
    },
    {
      label: "Enable pre-release channel",
      tip: "Prefer pre-release version which has new features but less stability",
      renderValue: () => {
        return (
          <Switch
            isSelected={appOptions.enablePreReleaseChannel}
            size={"sm"}
            onValueChange={(checked) => {
              applyPatches(
                BApi.options.patchAppOptions,
                {
                  enablePreReleaseChannel: checked,
                },
                () => {},
              );
            }}
          />
        );
      },
    },
    {
      label: "Enable anonymous data tracking",
      tip: "We are using Microsoft Clarity to track anonymous data, which will help us to improve our product experience.",
      renderValue: () => {
        return (
          <Switch
            isSelected={appOptions.enableAnonymousDataTracking}
            size={"sm"}
            onValueChange={(checked) => {
              applyPatches(
                BApi.options.patchAppOptions,
                {
                  enableAnonymousDataTracking: checked,
                },
                () => {},
              );
            }}
          />
        );
      },
    },
    {
      label: "Listening port",
      tip: "You can set a fixed port for Bakabase to listen on.",
      renderValue: () => {
        const minPort = 5000;
        const maxPort = 65000;

        return (
          <EditableValue<
            number,
            NumberInputProps,
            ChipProps & { value: number }
          >
            Editor={(props) => (
              <NumberInput
                className={"max-w-[320px]"}
                description={
                  <div>
                    <div>
                      {t<string>("Current listening port is {{port}}", {
                        port: appContext?.listeningAddresses?.[0]
                          ?.split(":")
                          .slice(-1)[0],
                      })}
                    </div>
                    <div>
                      {t<string>(
                        "The configurable port range is {{min}}-{{max}}",
                        {
                          min: minPort,
                          max: maxPort,
                        },
                      )}
                    </div>
                    <div>
                      {t<string>(
                        "Changes will take effect after restarting the application",
                      )}
                    </div>
                  </div>
                }
                formatOptions={{ useGrouping: false }}
                max={maxPort}
                min={minPort}
                placeholder={t<string>("Port number")}
                {...props}
              />
            )}
            Viewer={({ value, ...props }) =>
              value ? (
                <Chip
                  radius={"sm"}
                  startContent={<AiOutlineNumber className={"text-base"} />}
                  variant={"flat"}
                  {...props}
                >
                  {value}
                </Chip>
              ) : null
            }
            value={appOptions.listeningPort}
            onSubmit={async (v) => {
              await BApi.options.putAppOptions({
                ...appOptions,
                listeningPort: v,
              });
            }}
          />
        );
      },
    },
  ];

  return (
    <div className="group">
      {/* <Title title={i18n.t<string>('Other settings')} /> */}
      <div className="settings">
        <Table removeWrapper>
          <TableHeader>
            <TableColumn width={200}>{t<string>("Other settings")}</TableColumn>
            <TableColumn>&nbsp;</TableColumn>
          </TableHeader>
          <TableBody>
            {otherSettings.map((c, i) => {
              return (
                <TableRow
                  key={i}
                  className={"hover:bg-[var(--bakaui-overlap-background)]"}
                >
                  <TableCell>
                    <div
                      style={{
                        display: "flex",
                        alignItems: "center",
                      }}
                    >
                      {c.tip ? (
                        <Tooltip
                          color={"secondary"}
                          content={t<string>(c.tip)}
                          placement={"top"}
                        >
                          <div className={"flex items-center gap-1"}>
                            {t<string>(c.label)}
                            <AiOutlineQuestionCircle className={"text-base"} />
                          </div>
                        </Tooltip>
                      ) : (
                        t<string>(c.label)
                      )}
                    </div>
                  </TableCell>
                  <TableCell>{c.renderValue()}</TableCell>
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
        {/*     title={i18n.t<string>('Other setting')} */}
        {/*     cell={(l, i, r) => { */}
        {/*       return ( */}
        {/*         <div style={{ */}
        {/*           display: 'flex', */}
        {/*           alignItems: 'center', */}
        {/*         }} */}
        {/*         > */}
        {/*           {i18n.t<string>(l)} */}
        {/*           {r.tip && ( */}
        {/*             <> */}
        {/*               &nbsp; */}
        {/*               <Balloon.Tooltip */}
        {/*                 align={'r'} */}
        {/*                 trigger={<CustomIcon type={'question-circle'} />} */}
        {/*               > */}
        {/*                 {i18n.t<string>(r.tip)} */}
        {/*               </Balloon.Tooltip> */}
        {/*             </> */}
        {/*           )} */}
        {/*         </div> */}
        {/*       ); */}
        {/*     }} */}
        {/*   /> */}
        {/*   <Table.Column */}
        {/*     dataIndex={'renderValue'} */}
        {/*     title={i18n.t<string>('Value')} */}
        {/*     cell={(render, i, r) => (render ? render() : r.value)} */}
        {/*   /> */}
        {/* </Table> */}
      </div>
    </div>
  );
};
