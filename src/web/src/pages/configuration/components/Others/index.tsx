"use client";

import type { Key } from "@react-types/shared";
import type {
  ChipProps,
  NumberInputProps,
  InputProps,
} from "@/components/bakaui";
import type { BakabaseInsideWorldBusinessComponentsConfigurationsModelsInputNetworkOptionsPatchInputModel } from "@/sdk/Api";

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
import { useNetworkOptionsStore, useAppOptionsStore } from "@/stores/options";
import { useAppContextStore } from "@/stores/appContext.ts";
import { RuntimeMode } from "@/sdk/constants.ts";

enum ProxyMode {
  DoNotUse = 0,
  UseSystem = 1,
  UseCustom = 2,
}
const Others = ({
  applyPatches = () => {},
}: {
  applyPatches: (API: any, patches: any, success: (rsp: any) => void) => void;
}) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const appOptions = useAppOptionsStore((state) => state.data);
  const networkOptions = useNetworkOptionsStore((state) => state.data);
  const appContext = useAppContextStore();

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
                  const patches: BakabaseInsideWorldBusinessComponentsConfigurationsModelsInputNetworkOptionsPatchInputModel =
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
                      toast.error(t<string>("Invalid Data"));
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
  ];

  switch (appContext.runtimeMode) {
    case RuntimeMode.Dev:
    case RuntimeMode.WinForms:
      otherSettings.push(
        ...[
          {
            label: "Listening port count",
            tip: "You can configure the number of listening ports. The more listening ports you set, the more it helps with parallel processing of interface‑related operations. It’s recommended not to exceed the number of CPU cores. In most cases, you can just ignore this setting. If you leave it as empty and listening ports are not specified, we'll use a default value.",
            renderValue: () => {
              const min = 0;
              const max = 3;

              return (
                <EditableValue<
                  number,
                  NumberInputProps,
                  ChipProps & { value: number }
                >
                  Editor={(props) => (
                    <NumberInput
                      isClearable
                      className={"max-w-[320px]"}
                      description={
                        <div>
                          <div>
                            {t<string>(
                              "Current listening port count is {{port}}",
                              {
                                port:
                                  appOptions.autoListeningPortCount == 0
                                    ? t<string>("Auto")
                                    : appOptions.autoListeningPortCount,
                              },
                            )}
                          </div>
                          <div>
                            {t<string>(
                              "The configurable port range is {{min}}-{{max}}. And you can set it to 0 for auto count.",
                              {
                                min,
                                max,
                              },
                            )}
                          </div>
                          <div>
                            {t<string>(
                              "Changes will take effect after restarting the application",
                            )}
                          </div>
                          <div>
                            {t<string>(
                              "Please be careful when setting this value, if you set it to a value that is already in use by another application, our application will not be able to start.",
                            )}
                          </div>
                        </div>
                      }
                      formatOptions={{ useGrouping: false }}
                      max={max}
                      min={min}
                      placeholder={t<string>("Count")}
                      {...props}
                    />
                  )}
                  Viewer={({ value, ...props }) =>
                    value ? (
                      <Chip
                        radius={"sm"}
                        startContent={
                          <AiOutlineNumber className={"text-base"} />
                        }
                        variant={"flat"}
                        {...props}
                      >
                        {value}
                      </Chip>
                    ) : null
                  }
                  value={appOptions.autoListeningPortCount}
                  onSubmit={async (v) => {
                    await BApi.options.patchAppOptions({
                      autoListeningPortCount: v,
                    });
                    toast.success(t<string>("Saved"));
                  }}
                />
              );
            },
          },
          {
            label: "Listening ports",
            tip: "You can set fixed listening ports.",
            renderValue: () => {
              const toText = (ports?: number[]) =>
                ports?.length ? ports.join(", ") : "";
              const parsePorts = (text?: string) => {
                const raw = (text ?? "")
                  .split(/[，,\s]+/)
                  .map((s) => s.trim())
                  .filter((s) => s.length > 0);
                const nums = raw
                  .map((s) => Number.parseInt(s, 10))
                  .filter((n) => Number.isFinite(n) && n > 0 && n <= 65535);
                // de-duplicate, keep order
                const seen = new Set<number>();
                const unique: number[] = [];

                for (const n of nums) {
                  if (!seen.has(n)) {
                    seen.add(n);
                    unique.push(n);
                  }
                }

                return unique;
              };

              const max = 6;

              return (
                <EditableValue<string, InputProps>
                  Editor={(props) => (
                    <Input
                      isClearable
                      className={"max-w-[420px]"}
                      description={
                        <div>
                          <div>
                            {t<string>(
                              "Enter ports separated by commas, e.g. 34567, 34568",
                            )}
                          </div>
                          <div>
                            {t<string>(
                              "Changes will take effect after restarting the application",
                            )}
                          </div>
                          <div>
                            {t<string>(
                              "Please be careful when setting this value, if you set it to a value that is already in use by another application, our application will not be able to start.",
                            )}
                          </div>
                        </div>
                      }
                      placeholder={t<string>("e.g. 34567, 34568")}
                      {...props}
                    />
                  )}
                  Viewer={({ value }) => {
                    const ports = (parsePorts(value) as number[]);
                    if (ports && ports.length > 0) {
                      return (
                        <div className={"flex flex-wrap gap-2"}>
                        {(parsePorts(value) as number[]).map((p) => (
                          <Chip
                            key={p}
                            radius={"sm"}
                            startContent={
                              <AiOutlineNumber className={"text-base"} />
                            }
                            variant={"flat"}
                          >
                            {p}
                          </Chip>
                        ))}
                      </div>
                      )
                    }
                    return null;
                  }}
                  value={toText(appOptions.listeningPorts)}
                  onSubmit={async (text) => {
                    const ports = parsePorts(text);

                    if (ports.length > max) {
                      toast.error(
                        t<string>(
                          "Too many ports. Up to {{max}} ports are supported.",
                          {
                            max,
                          },
                        ),
                      );
                      throw new Error("invalid");
                    }
                    await BApi.options.patchAppOptions({
                      listeningPorts: ports,
                    });
                    toast.success(t<string>("Saved"));
                  }}
                />
              );
            },
          },
        ],
      );
      break;
    case RuntimeMode.Docker:
      break;
  }

  if (appContext.apiEndpoints)
    return (
      <div className="group">
        {/* <Title title={i18n.t<string>('Other settings')} /> */}
        <div className="settings">
          <Table removeWrapper>
            <TableHeader>
              <TableColumn width={200}>
                {t<string>("Other settings")}
              </TableColumn>
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
                            className="max-w-[300px]"
                            color={"secondary"}
                            content={t<string>(c.tip)}
                            placement={"top"}
                          >
                            <div className={"flex items-center gap-1"}>
                              {t<string>(c.label)}
                              <AiOutlineQuestionCircle
                                className={"text-base"}
                              />
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
        </div>
      </div>
    );
};

Others.displayName = "Others";

export default Others;
