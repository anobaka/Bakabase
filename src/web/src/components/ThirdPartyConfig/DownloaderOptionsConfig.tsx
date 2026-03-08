"use client";

import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Input, Button, Textarea } from "@heroui/react";

import BApi from "@/sdk/BApi";
import { toast } from "@/components/bakaui";
import { CookieValidatorTarget } from "@/sdk/constants";

interface DownloaderOptionsConfigProps {
  options: any;
  patchApi: (patches: any) => Promise<any>;
  cookieValidatorTarget?: CookieValidatorTarget;
}

export default function DownloaderOptionsConfig({
  options: externalOptions,
  patchApi,
  cookieValidatorTarget,
}: DownloaderOptionsConfigProps) {
  const { t } = useTranslation();
  const [options, setOptions] = useState<any>(externalOptions || {});
  const [validatingCookie, setValidatingCookie] = useState(false);

  useEffect(() => {
    setOptions(JSON.parse(JSON.stringify(externalOptions || {})));
  }, [externalOptions]);

  const handleSave = () => {
    patchApi(options).then((a: any) => {
      if (!a.code) {
        toast.success(t<string>("thirdPartyConfig.success.saved"));
      }
    });
  };

  const handleValidateCookie = () => {
    if (!cookieValidatorTarget || !options.cookie) return;
    setValidatingCookie(true);
    BApi.tool
      .validateCookie({ cookie: options.cookie, target: cookieValidatorTarget })
      .then((r: any) => {
        if (r.code) {
          toast.danger(
            `${t<string>("thirdPartyConfig.error.invalidCookie")}:${r.message}`,
          );
        } else {
          toast.success(t<string>("thirdPartyConfig.success.cookieValid"));
        }
      })
      .finally(() => {
        setValidatingCookie(false);
      });
  };

  return (
    <div className="space-y-4">
      <div>
        <Textarea
          label={t<string>("thirdPartyConfig.label.cookie")}
          size="sm"
          value={options.cookie || ""}
          onValueChange={(v) => setOptions({ ...options, cookie: v })}
        />
      </div>
      <div className="grid grid-cols-2 gap-4">
        <Input
          label={t<string>("thirdPartyConfig.label.maxConcurrency")}
          size="sm"
          type="number"
          value={String(options.maxConcurrency || 1)}
          onValueChange={(v) =>
            setOptions({ ...options, maxConcurrency: Number(v) || 1 })
          }
        />
        <Input
          label={t<string>("thirdPartyConfig.label.requestInterval")}
          size="sm"
          type="number"
          value={String(options.requestInterval || 1000)}
          onValueChange={(v) =>
            setOptions({ ...options, requestInterval: Number(v) || 1000 })
          }
        />
      </div>
      <div>
        <Input
          label={t<string>("thirdPartyConfig.label.defaultPath")}
          size="sm"
          value={options.defaultPath || ""}
          onValueChange={(v) => setOptions({ ...options, defaultPath: v })}
        />
      </div>
      <div>
        <Input
          label={t<string>("thirdPartyConfig.label.namingConvention")}
          size="sm"
          value={options.namingConvention || ""}
          onValueChange={(v) =>
            setOptions({ ...options, namingConvention: v })
          }
        />
      </div>
      <div className="grid grid-cols-2 gap-4">
        <Input
          label={t<string>("thirdPartyConfig.label.maxRetries")}
          size="sm"
          type="number"
          value={String(options.maxRetries || 0)}
          onValueChange={(v) =>
            setOptions({ ...options, maxRetries: Number(v) || 0 })
          }
        />
        <Input
          label={t<string>("thirdPartyConfig.label.requestTimeout")}
          size="sm"
          type="number"
          value={String(options.requestTimeout || 0)}
          onValueChange={(v) =>
            setOptions({ ...options, requestTimeout: Number(v) || 0 })
          }
        />
      </div>
      <div className="operations flex gap-2">
        <Button color="primary" size="sm" onPress={handleSave}>
          {t<string>("thirdPartyConfig.action.save")}
        </Button>
        {cookieValidatorTarget && options.cookie && (
          <Button
            disabled={!options.cookie?.length || validatingCookie}
            isLoading={validatingCookie}
            size="sm"
            onPress={handleValidateCookie}
          >
            {t<string>("thirdPartyConfig.action.validateCookie")}
          </Button>
        )}
      </div>
    </div>
  );
}
