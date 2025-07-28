"use client";

import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  Button,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  Tooltip,
  Input,
  Accordion,
  AccordionItem,
  Textarea,
} from "@heroui/react";
import { AiOutlineQuestionCircle } from "react-icons/ai";

import { toast } from "@/components/bakaui";
import { CookieValidatorTarget } from "@/sdk/constants";
import {
  useBilibiliOptionsStore,
  useExHentaiOptionsStore,
  usePixivOptionsStore,
  useSoulPlusOptionsStore,
  useBangumiOptionsStore,
  useCienOptionsStore,
  useDLsiteOptionsStore,
  useFanboxOptionsStore,
  useFantiaOptionsStore,
  usePatreonOptionsStore,
  useTmdbOptionsStore,
} from "@/stores/options";
import BApi from "@/sdk/BApi";
import BetaChip from "@/components/Chips/BetaChip";
const ThirdParty = ({
  applyPatches = () => {},
}: {
  applyPatches: (API: any, patches: any) => void;
}) => {
  const { t } = useTranslation();

  const bilibiliOptions = useBilibiliOptionsStore((state) => state.data);
  const exhentaiOptions = useExHentaiOptionsStore((state) => state.data);
  const pixivOptions = usePixivOptionsStore((state) => state.data);
  const soulPlusOptions = useSoulPlusOptionsStore((state) => state.data);
  const bangumiOptions = useBangumiOptionsStore((state) => state.data);
  const cienOptions = useCienOptionsStore((state) => state.data);
  const dlsiteOptions = useDLsiteOptionsStore((state) => state.data);
  const fanboxOptions = useFanboxOptionsStore((state) => state.data);
  const fantiaOptions = useFantiaOptionsStore((state) => state.data);
  const patreonOptions = usePatreonOptionsStore((state) => state.data);
  const tmdbOptions = useTmdbOptionsStore((state) => state.data);

  const [tmpBilibiliOptions, setTmpBilibiliOptions] = useState(
    bilibiliOptions || {},
  );
  const [tmpExHentaiOptions, setTmpExHentaiOptions] = useState(
    exhentaiOptions || {},
  );
  const [tmpPixivOptions, setTmpPixivOptions] = useState(pixivOptions || {});
  const [tmpSoulPlusOptions, setTmpSoulPlusOptions] = useState(
    soulPlusOptions || {},
  );
  const [tmpBangumiOptions, setTmpBangumiOptions] = useState(
    bangumiOptions || {},
  );
  const [tmpCienOptions, setTmpCienOptions] = useState(cienOptions || {});
  const [tmpDLsiteOptions, setTmpDLsiteOptions] = useState(dlsiteOptions || {});
  const [tmpFanboxOptions, setTmpFanboxOptions] = useState(fanboxOptions || {});
  const [tmpFantiaOptions, setTmpFantiaOptions] = useState(fantiaOptions || {});
  const [tmpPatreonOptions, setTmpPatreonOptions] = useState(
    patreonOptions || {},
  );
  const [tmpTmdbOptions, setTmpTmdbOptions] = useState(tmdbOptions || {});

  const [validatingCookies, setValidatingCookies] = useState<{
    [key: string]: boolean;
  }>({});

  useEffect(() => {
    setTmpBilibiliOptions(JSON.parse(JSON.stringify(bilibiliOptions || {})));
  }, [bilibiliOptions]);

  useEffect(() => {
    setTmpExHentaiOptions(JSON.parse(JSON.stringify(exhentaiOptions || {})));
  }, [exhentaiOptions]);

  useEffect(() => {
    setTmpPixivOptions(JSON.parse(JSON.stringify(pixivOptions || {})));
  }, [pixivOptions]);

  useEffect(() => {
    setTmpSoulPlusOptions(JSON.parse(JSON.stringify(soulPlusOptions || {})));
  }, [soulPlusOptions]);

  useEffect(() => {
    setTmpBangumiOptions(JSON.parse(JSON.stringify(bangumiOptions || {})));
  }, [bangumiOptions]);

  useEffect(() => {
    setTmpCienOptions(JSON.parse(JSON.stringify(cienOptions || {})));
  }, [cienOptions]);

  useEffect(() => {
    setTmpDLsiteOptions(JSON.parse(JSON.stringify(dlsiteOptions || {})));
  }, [dlsiteOptions]);

  useEffect(() => {
    setTmpFanboxOptions(JSON.parse(JSON.stringify(fanboxOptions || {})));
  }, [fanboxOptions]);

  useEffect(() => {
    setTmpFantiaOptions(JSON.parse(JSON.stringify(fantiaOptions || {})));
  }, [fantiaOptions]);

  useEffect(() => {
    setTmpPatreonOptions(JSON.parse(JSON.stringify(patreonOptions || {})));
  }, [patreonOptions]);

  useEffect(() => {
    setTmpTmdbOptions(JSON.parse(JSON.stringify(tmdbOptions || {})));
  }, [tmdbOptions]);

  const validateCookie = (
    thirdParty: string,
    cookie: string,
    target: CookieValidatorTarget,
  ) => {
    setValidatingCookies((prev) => ({ ...prev, [thirdParty]: true }));
    BApi.tool
      .validateCookie({
        cookie,
        target,
      })
      .then((r) => {
        if (r.code) {
          toast.danger(`${t<string>("Invalid cookie")}:${r.message}`);
        } else {
          toast.success(t<string>("Cookie is good"));
        }
      })
      .finally(() => {
        setValidatingCookies((prev) => ({ ...prev, [thirdParty]: false }));
      });
  };

  const renderDownloaderOptions = (
    options: any,
    setOptions: (opts: any) => void,
    saveApi: any,
    thirdPartyName: string,
  ) => (
    <div className="space-y-4">
      <div>
        <Textarea
          label="Cookie"
          size="sm"
          value={options.cookie || ""}
          onValueChange={(v) => {
            setOptions({
              ...options,
              cookie: v,
            });
          }}
        />
      </div>
      <div className="grid grid-cols-2 gap-4">
        <Input
          label="Max Concurrency"
          size="sm"
          type="number"
          value={String(options.maxConcurrency || 1)}
          onValueChange={(v) => {
            setOptions({
              ...options,
              maxConcurrency: Number(v) || 1,
            });
          }}
        />
        <Input
          label="Request Interval (ms)"
          size="sm"
          type="number"
          value={String(options.requestInterval || 1000)}
          onValueChange={(v) => {
            setOptions({
              ...options,
              requestInterval: Number(v) || 1000,
            });
          }}
        />
      </div>
      <div>
        <Input
          label="Default Path"
          size="sm"
          value={options.defaultPath || ""}
          onValueChange={(v) => {
            setOptions({
              ...options,
              defaultPath: v,
            });
          }}
        />
      </div>
      <div>
        <Input
          label="Naming Convention"
          size="sm"
          value={options.namingConvention || ""}
          onValueChange={(v) => {
            setOptions({
              ...options,
              namingConvention: v,
            });
          }}
        />
      </div>
      <div className="grid grid-cols-2 gap-4">
        <Input
          label="Max Retries"
          size="sm"
          type="number"
          value={String(options.maxRetries || 0)}
          onValueChange={(v) => {
            setOptions({
              ...options,
              maxRetries: Number(v) || 0,
            });
          }}
        />
        <Input
          label="Request Timeout (ms)"
          size="sm"
          type="number"
          value={String(options.requestTimeout || 0)}
          onValueChange={(v) => {
            setOptions({
              ...options,
              requestTimeout: Number(v) || 0,
            });
          }}
        />
      </div>
      <div className="operations flex gap-2">
        <Button
          color="primary"
          size="sm"
          onPress={() => {
            applyPatches(saveApi, options);
          }}
        >
          {t<string>("Save")}
        </Button>
        {options.cookie && (
          <Button
            disabled={
              !options.cookie?.length || validatingCookies[thirdPartyName]
            }
            isLoading={validatingCookies[thirdPartyName]}
            size="sm"
            onPress={() => {
              const validatorMap: { [key: string]: CookieValidatorTarget } = {
                bilibili: CookieValidatorTarget.BiliBili,
                exhentai: CookieValidatorTarget.ExHentai,
                pixiv: CookieValidatorTarget.Pixiv,
              };

              const target = validatorMap[thirdPartyName.toLowerCase()];

              if (target) {
                validateCookie(thirdPartyName, options.cookie, target);
              }
            }}
          >
            {t<string>("Validate cookie")}
          </Button>
        )}
      </div>
    </div>
  );

  const thirdPartySettings = [
    {
      key: "bilibili",
      label: "Bilibili",
      tip: "Configure Bilibili downloader settings including cookie authentication and download parameters.",
      content: renderDownloaderOptions(
        tmpBilibiliOptions,
        setTmpBilibiliOptions,
        BApi.options.patchBilibiliOptions,
        "bilibili",
      ),
    },
    {
      key: "exhentai",
      label: "ExHentai",
      tip: "Configure ExHentai downloader settings. Cookie is required for authentication.",
      content: renderDownloaderOptions(
        tmpExHentaiOptions,
        setTmpExHentaiOptions,
        BApi.options.patchExHentaiOptions,
        "exhentai",
      ),
    },
    {
      key: "pixiv",
      label: "Pixiv",
      tip: "Configure Pixiv downloader settings including authentication and download parameters.",
      content: renderDownloaderOptions(
        tmpPixivOptions,
        setTmpPixivOptions,
        BApi.options.patchPixivOptions,
        "pixiv",
      ),
    },
    {
      key: "soulplus",
      label: "SoulPlus",
      tip: "Configure SoulPlus settings including cookie authentication and auto-buy threshold.",
      content: (
        <div className="space-y-4">
          <div>
            <Input
              label="Cookie"
              size="sm"
              value={tmpSoulPlusOptions.cookie || ""}
              onValueChange={(v) => {
                setTmpSoulPlusOptions({
                  ...tmpSoulPlusOptions,
                  cookie: v,
                });
              }}
            />
          </div>
          <div>
            <Input
              label="Auto Buy Threshold"
              size="sm"
              type="number"
              value={String(tmpSoulPlusOptions.autoBuyThreshold || 0)}
              onValueChange={(v) => {
                setTmpSoulPlusOptions({
                  ...tmpSoulPlusOptions,
                  autoBuyThreshold: Number(v) || 0,
                });
              }}
            />
          </div>
          <div className="operations">
            <Button
              color="primary"
              size="sm"
              onPress={() => {
                applyPatches(
                  BApi.options.patchSoulPlusOptions,
                  tmpSoulPlusOptions,
                );
              }}
            >
              {t<string>("Save")}
            </Button>
          </div>
        </div>
      ),
    },
    {
      key: "bangumi",
      label: "Bangumi",
      tip: "Configure Bangumi settings including request parameters and authentication.",
      content: (
        <div className="space-y-4">
          <div className="grid grid-cols-2 gap-4">
            <Input
              label="Max Concurrency"
              size="sm"
              type="number"
              value={String(tmpBangumiOptions.maxConcurrency || 1)}
              onValueChange={(v) => {
                setTmpBangumiOptions({
                  ...tmpBangumiOptions,
                  maxConcurrency: Number(v) || 1,
                });
              }}
            />
            <Input
              label="Request Interval (ms)"
              size="sm"
              type="number"
              value={String(tmpBangumiOptions.requestInterval || 1000)}
              onValueChange={(v) => {
                setTmpBangumiOptions({
                  ...tmpBangumiOptions,
                  requestInterval: Number(v) || 1000,
                });
              }}
            />
          </div>
          <div>
            <Input
              label="Cookie"
              size="sm"
              value={tmpBangumiOptions.cookie || ""}
              onValueChange={(v) => {
                setTmpBangumiOptions({
                  ...tmpBangumiOptions,
                  cookie: v,
                });
              }}
            />
          </div>
          <div className="grid grid-cols-2 gap-4">
            <Input
              label="User Agent"
              size="sm"
              value={tmpBangumiOptions.userAgent || ""}
              onValueChange={(v) => {
                setTmpBangumiOptions({
                  ...tmpBangumiOptions,
                  userAgent: v,
                });
              }}
            />
            <Input
              label="Referer"
              size="sm"
              value={tmpBangumiOptions.referer || ""}
              onValueChange={(v) => {
                setTmpBangumiOptions({
                  ...tmpBangumiOptions,
                  referer: v,
                });
              }}
            />
          </div>
          <div className="operations">
            <Button
              color="primary"
              size="sm"
              onPress={() => {
                applyPatches(
                  BApi.options.patchBangumiOptions,
                  tmpBangumiOptions,
                );
              }}
            >
              {t<string>("Save")}
            </Button>
          </div>
        </div>
      ),
    },
    {
      key: "cien",
      label: "Cien",
      tip: "Configure Cien downloader settings including authentication and download parameters.",
      content: renderDownloaderOptions(
        tmpCienOptions,
        setTmpCienOptions,
        BApi.options.patchCienOptions,
        "cien",
      ),
    },
    {
      key: "dlsite",
      label: "DLsite",
      tip: "Configure DLsite downloader settings including authentication and download parameters.",
      content: renderDownloaderOptions(
        tmpDLsiteOptions,
        setTmpDLsiteOptions,
        BApi.options.patchDLsiteOptions,
        "dlsite",
      ),
    },
    {
      key: "fanbox",
      label: "Fanbox",
      tip: "Configure Fanbox downloader settings including authentication and download parameters.",
      content: renderDownloaderOptions(
        tmpFanboxOptions,
        setTmpFanboxOptions,
        BApi.options.patchFanboxOptions,
        "fanbox",
      ),
    },
    {
      key: "fantia",
      label: "Fantia",
      tip: "Configure Fantia downloader settings including authentication and download parameters.",
      content: renderDownloaderOptions(
        tmpFantiaOptions,
        setTmpFantiaOptions,
        BApi.options.patchFantiaOptions,
        "fantia",
      ),
    },
    {
      key: "patreon",
      label: "Patreon",
      tip: "Configure Patreon downloader settings including authentication and download parameters.",
      content: renderDownloaderOptions(
        tmpPatreonOptions,
        setTmpPatreonOptions,
        BApi.options.patchPatreonOptions,
        "patreon",
      ),
    },
    {
      key: "tmdb",
      label: "TMDB",
      tip: "Configure TMDB (The Movie Database) settings including API key and request parameters.",
      content: (
        <div className="space-y-4">
          <div className="grid grid-cols-2 gap-4">
            <Input
              label="Max Concurrency"
              size="sm"
              type="number"
              value={String(tmpTmdbOptions.maxConcurrency || 1)}
              onValueChange={(v) => {
                setTmpTmdbOptions({
                  ...tmpTmdbOptions,
                  maxConcurrency: Number(v) || 1,
                });
              }}
            />
            <Input
              label="Request Interval (ms)"
              size="sm"
              type="number"
              value={String(tmpTmdbOptions.requestInterval || 1000)}
              onValueChange={(v) => {
                setTmpTmdbOptions({
                  ...tmpTmdbOptions,
                  requestInterval: Number(v) || 1000,
                });
              }}
            />
          </div>
          <div>
            <Input
              label="API Key"
              size="sm"
              value={tmpTmdbOptions.apiKey || ""}
              onValueChange={(v) => {
                setTmpTmdbOptions({
                  ...tmpTmdbOptions,
                  apiKey: v,
                });
              }}
            />
          </div>
          <div>
            <Textarea
              label="Cookie"
              size="sm"
              value={tmpTmdbOptions.cookie || ""}
              onValueChange={(v) => {
                setTmpTmdbOptions({
                  ...tmpTmdbOptions,
                  cookie: v,
                });
              }}
            />
          </div>
          <div className="grid grid-cols-2 gap-4">
            <Input
              label="User Agent"
              size="sm"
              value={tmpTmdbOptions.userAgent || ""}
              onValueChange={(v) => {
                setTmpTmdbOptions({
                  ...tmpTmdbOptions,
                  userAgent: v,
                });
              }}
            />
            <Input
              label="Referer"
              size="sm"
              value={tmpTmdbOptions.referer || ""}
              onValueChange={(v) => {
                setTmpTmdbOptions({
                  ...tmpTmdbOptions,
                  referer: v,
                });
              }}
            />
          </div>
          <div className="operations">
            <Button
              color="primary"
              size="sm"
              onPress={() => {
                applyPatches(BApi.options.patchTmdbOptions, tmpTmdbOptions);
              }}
            >
              {t<string>("Save")}
            </Button>
          </div>
        </div>
      ),
    },
  ];

  return (
    <div className="group">
      <div className="settings">
        <Table removeWrapper>
          <TableHeader>
            <TableColumn width={200}>
              {t<string>("Third Party Configurations")}
              &nbsp;
              <BetaChip />
            </TableColumn>
            <TableColumn>&nbsp;</TableColumn>
          </TableHeader>
          <TableBody>
            <TableRow className="hover:bg-[var(--bakaui-overlap-background)]">
              <TableCell colSpan={2}>
                <Accordion variant="splitted">
                  {thirdPartySettings.map((setting) => (
                    <AccordionItem
                      key={setting.key}
                      aria-label={setting.label}
                      title={
                        <div className="flex items-center gap-2">
                          {setting.label}
                          {setting.tip && (
                            <Tooltip
                              color="secondary"
                              content={t<string>(setting.tip)}
                              placement="top"
                            >
                              <AiOutlineQuestionCircle className="text-base" />
                            </Tooltip>
                          )}
                        </div>
                      }
                    >
                      {setting.content}
                    </AccordionItem>
                  ))}
                </Accordion>
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </div>
    </div>
  );
};

ThirdParty.displayName = "ThirdParty";

export default ThirdParty;
