"use client";

import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Input, Button, Textarea } from "@heroui/react";
import { AiOutlineCheck, AiOutlineClose } from "react-icons/ai";

import BApi from "@/sdk/BApi";
import { toast } from "@/components/bakaui";
import { notifyCookieCaptureDismissal } from "./notifyCookieCaptureDismissal";
import { CookieValidatorTarget, RuntimeMode } from "@/sdk/constants";
import { useAppContextStore } from "@/stores/appContext";

interface DownloaderOptionsConfigProps {
  options: any;
  patchApi: (patches: any) => Promise<any>;
  cookieValidatorTarget?: CookieValidatorTarget;
  cookieCaptureTarget?: CookieValidatorTarget;
  hideCookie?: boolean;
}

export default function DownloaderOptionsConfig({
  options: externalOptions,
  patchApi,
  cookieValidatorTarget,
  cookieCaptureTarget,
  hideCookie,
}: DownloaderOptionsConfigProps) {
  const { t } = useTranslation();
  const runtimeMode = useAppContextStore((s) => s.runtimeMode);
  const isDesktopApp = runtimeMode !== RuntimeMode.Docker;
  const [options, setOptions] = useState<any>(externalOptions || {});
  const [validatingCookie, setValidatingCookie] = useState(false);
  const [validationResult, setValidationResult] = useState<"succeed" | "failed" | undefined>();
  const [capturing, setCapturing] = useState(false);

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
          setValidationResult("failed");
          toast.danger(
            `${t<string>("thirdPartyConfig.error.invalidCookie")}:${r.message}`,
          );
        } else {
          setValidationResult("succeed");
        }
      })
      .catch(() => setValidationResult("failed"))
      .finally(() => {
        setValidatingCookie(false);
      });
  };

  const handleCaptureCookie = async () => {
    if (!cookieCaptureTarget) return;
    setCapturing(true);
    try {
      const rsp = await BApi.tool.captureCookie({ target: cookieCaptureTarget });
      if (!rsp.code && rsp.data) {
        setOptions((prev: any) => ({ ...prev, cookie: rsp.data }));
      } else {
        notifyCookieCaptureDismissal(rsp);
      }
    } finally {
      setCapturing(false);
    }
  };

  return (
    <div className="space-y-6">
      {!hideCookie && (
        <div>
          <Textarea
            label={t<string>("thirdPartyConfig.label.cookie")}
            size="sm"
            value={options.cookie || ""}
            onValueChange={(v) => setOptions({ ...options, cookie: v })}
          />
        </div>
      )}
      <div className="space-y-3">
        <h3 className="text-small font-semibold text-default-700">
          {t<string>("thirdPartyConfig.group.dataFetch")}
        </h3>
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
      </div>
      <div className="space-y-3">
        <h3 className="text-small font-semibold text-default-700">
          {t<string>("thirdPartyConfig.group.download")}
        </h3>
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
      </div>
      <div className="operations flex gap-2">
        <Button color="primary" size="sm" onPress={handleSave}>
          {t<string>("thirdPartyConfig.action.save")}
        </Button>
        {!hideCookie && cookieValidatorTarget && options.cookie && (
          <Button
            color={validationResult === "succeed" ? "success" : validationResult === "failed" ? "danger" : "default"}
            disabled={!options.cookie?.length || validatingCookie}
            isLoading={validatingCookie}
            size="sm"
            variant="flat"
            startContent={validationResult === "succeed" ? <AiOutlineCheck /> : validationResult === "failed" ? <AiOutlineClose /> : undefined}
            onPress={handleValidateCookie}
          >
            {t<string>("thirdPartyConfig.action.validateCookie")}
          </Button>
        )}
        {!hideCookie && isDesktopApp && cookieCaptureTarget != null && (
          <Button
            color="secondary"
            isLoading={capturing}
            size="sm"
            variant="flat"
            onPress={handleCaptureCookie}
          >
            {t("resourceSource.accounts.loginToImport")}
          </Button>
        )}
      </div>
    </div>
  );
}
