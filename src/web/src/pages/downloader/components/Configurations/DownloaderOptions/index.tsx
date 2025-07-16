"use client";

import type { CookieValidatorTarget } from "@/sdk/constants";
import type { components } from "@/sdk/BApi2";

import React from "react";
import { useTranslation } from "react-i18next";
import { Textarea } from "@heroui/react";

import CookieValidator from "@/components/CookieValidator";
import { ThirdPartyId } from "@/sdk/constants";
import { FileSystemSelectorButton } from "@/components/FileSystemSelector";
import { Chip, NumberInput, Tooltip } from "@/components/bakaui";

type ConfigurableKey =
  | "cookie"
  | "threads"
  | "interval"
  | "defaultDownloadPath"
  | "namingConvention";

type NamingDefinition =
  components["schemas"]["Bakabase.InsideWorld.Models.Models.Aos.DownloaderNamingDefinitions"];

type Options = {
  cookie?: string;
  downloader?: {
    defaultPath?: string;
    threads?: number;
    interval?: number;
    namingConvention?: string;
  };
};

type Props = {
  thirdPartyId: ThirdPartyId;
  options: Options;
  namingDefinition?: NamingDefinition;
  configurableKeys: ConfigurableKey[];
  onChange: (patches: Partial<Options>) => void;
};

export default ({
  thirdPartyId,
  options,
  configurableKeys = [],
  namingDefinition,
  onChange,
}: Props) => {
  const { t } = useTranslation();

  const renderOptions = () => {
    const items: any[] = [];

    for (const k of configurableKeys) {
      switch (k) {
        case "cookie": {
          items.push(
            <>
              <div>{t<string>("Cookie")}</div>
              <div>
                <CookieValidator
                  cookie={options?.cookie}
                  target={thirdPartyId as unknown as CookieValidatorTarget}
                  onChange={(cookie) => {
                    onChange({ cookie });
                  }}
                />
              </div>
            </>,
          );
          break;
        }
        case "defaultDownloadPath": {
          items.push(
            <>
              <div>{t<string>("Default download path")}</div>
              <div>
                <FileSystemSelectorButton
                  // size={'small'}
                  fileSystemSelectorProps={{
                    targetType: "folder",
                    onSelected: (e) =>
                      onChange({
                        downloader: {
                          ...(options?.downloader || {}),
                          defaultPath: e.path,
                        },
                      }),
                    defaultSelectedPath: options?.downloader?.defaultPath,
                  }}
                />
              </div>
            </>,
          );
          break;
        }
        case "threads": {
          items.push(
            <>
              <div>{t<string>("Threads")}</div>
              <div>
                <NumberInput
                  description={t<string>(
                    "If you are browsing {{thirdPartyName}}, you should decrease the threads of downloading.",
                    {
                      lowerCasedThirdPartyName:
                        ThirdPartyId[thirdPartyId].toLowerCase(),
                    },
                  )}
                  fullWidth={false}
                  max={5}
                  min={0}
                  size={"sm"}
                  step={1}
                  value={options?.downloader?.threads}
                  onChange={(threads) =>
                    onChange({
                      downloader: {
                        ...(options?.downloader || {}),
                        threads,
                      },
                    })
                  }
                />
              </div>
            </>,
          );
          break;
        }
        case "interval": {
          items.push(
            <>
              <div>{t<string>("Request interval")}</div>
              <div className={"w-[200px]"}>
                <NumberInput
                  endContent={t<string>("ms")}
                  fullWidth={false}
                  max={9999999}
                  min={0}
                  size={"sm"}
                  value={options?.downloader?.interval}
                  onChange={(interval) =>
                    onChange({
                      downloader: {
                        ...(options?.downloader || {}),
                        interval,
                      },
                    })
                  }
                />
              </div>
            </>,
          );
          break;
        }
        case "namingConvention": {
          const { fields: namingFields = [], defaultConvention } =
            namingDefinition || {};
          const currentConvention =
            options?.downloader?.namingConvention ?? defaultConvention;
          let namingPathSegments: string[] = [];

          if (currentConvention) {
            namingPathSegments = namingFields
              .reduce((s, t) => {
                if (t.example) {
                  return s.replace(
                    new RegExp(`\\{${t.key}\\}`, "g"),
                    t.example,
                  );
                }

                return s;
              }, currentConvention)
              .replace(/\\/g, "/")
              .split("/");
          }
          items.push(
            <>
              <div>{t<string>("Naming convention")}</div>
              <div className={"flex flex-col gap-2"}>
                <Textarea
                  description={
                    <div>
                      {t<string>(
                        "You can select fields to build a naming convention template, and '/' to create directory.",
                      )}
                    </div>
                  }
                  placeholder={defaultConvention}
                  style={{ width: "100%" }}
                  value={options?.downloader?.namingConvention}
                  onChange={(v) => {
                    onChange({
                      downloader: {
                        ...(options?.downloader || {}),
                        namingConvention: v,
                      },
                    });
                  }}
                />
                {currentConvention && (
                  <div className={"flex items-center gap-1"}>
                    <Chip
                      color={"secondary"}
                      radius={"sm"}
                      size={"sm"}
                      variant={"flat"}
                    >
                      {t<string>("Example")}
                    </Chip>
                    {namingPathSegments.map((t, i) => {
                      if (i == namingPathSegments.length - 1) {
                        return <span className={"text-primary"}>{t}</span>;
                      } else {
                        return (
                          <>
                            <span className={"text-primary"}>{t}</span>
                            <span className={""}>/</span>
                          </>
                        );
                      }
                    })}
                  </div>
                )}
                <div className={"flex items-center gap-1 flex-wrap"}>
                  {namingFields.map((f) => {
                    const tag = (
                      <Chip
                        className={"flex flex-col h-auto"}
                        radius={"sm"}
                        size={"sm"}
                        variant={"faded"}
                        onClick={() => {
                          const value = `{${f.key}}`;
                          let nc = value;

                          if (
                            options?.downloader?.namingConvention &&
                            options.downloader.namingConvention.length > 0
                          ) {
                            nc = `${options?.downloader?.namingConvention}${value}`;
                          }
                          onChange({
                            downloader: {
                              ...(options?.downloader || {}),
                              namingConvention: nc,
                            },
                          });
                        }}
                      >
                        <div className="text-xs text-primary">{f.key}</div>
                        {f.example?.length > 0 && (
                          <div className={""}>{f.example}</div>
                        )}
                      </Chip>
                    );

                    if (f.description) {
                      return (
                        <Tooltip content={t<string>(f.description)}>
                          {tag}
                        </Tooltip>
                      );
                    } else {
                      return tag;
                    }
                  })}
                </div>
              </div>
            </>,
          );
          break;
        }
      }
    }

    return items;
  };

  // console.log(namingDefinitions, options);

  return (
    <div
      className={"grid gap-x-2 gap-y-1 items-center"}
      style={{ gridTemplateColumns: "auto 1fr" }}
    >
      {renderOptions()}
    </div>
  );
};
