import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button } from "@/components/bakaui";
import { humanFileSize } from "@/components/utils";
import ExternalLink from "@/components/ExternalLink";
import urls from "@/cons/Urls";
import BApi from "@/sdk/BApi";
import { downloadProblemSummary } from "./problemLabels";

type Capacity = NonNullable<
  Awaited<ReturnType<typeof BApi.pikpakCli.getPikPakCliCapacity>>["data"]
>;
type Account = NonNullable<Awaited<ReturnType<typeof BApi.pikpakCli.getPikPakCliAccount>>["data"]>;

export default function PikPakAccountCapacity({
  accountId,
  accountVersion,
  savedQuota,
}: {
  accountId: string;
  accountVersion: string;
  savedQuota?: Account["quota"];
}) {
  const { t, i18n } = useTranslation();
  const [lastRead, setLastRead] = useState<Capacity>();
  const [notice, setNotice] = useState<string>();
  const [loading, setLoading] = useState(false);
  const active = useRef<AbortController>();
  useEffect(() => () => active.current?.abort(), []);

  const refresh = async () => {
    if (active.current) return;
    const controller = new AbortController();
    active.current = controller;
    setLoading(true);
    setNotice(undefined);
    try {
      const result = await BApi.pikpakCli.getPikPakCliCapacity(
        { expectedAccountId: accountId, expectedAccountVersion: accountVersion },
        { signal: controller.signal, showErrorToast: false, sensitiveResponse: true },
      );
      if (controller.signal.aborted) return;
      const value = result.data;
      if (result.code || !value) throw new Error(result.message || "PIKPAK_CAPACITY_UNAVAILABLE");
      if (
        (value.accountId && value.accountId !== accountId) ||
        (value.accountVersion && value.accountVersion !== accountVersion)
      )
        throw new Error("PIKPAK_ACCOUNT_CHANGED");
      if (value.status === "fresh") {
        setLastRead(value);
      } else if (value.status === "busy") {
        setNotice(t("pikpakCli.capacity.busy"));
      } else {
        throw new Error(value.problem || "PIKPAK_CAPACITY_UNAVAILABLE");
      }
    } catch (error) {
      if (!controller.signal.aborted) {
        setNotice(
          downloadProblemSummary(error instanceof Error ? error.message : "", t) ||
            t("cloudDownload.problemCode.PIKPAK_CAPACITY_UNAVAILABLE"),
        );
      }
    } finally {
      if (!controller.signal.aborted) setLoading(false);
      active.current = undefined;
    }
  };
  const format = (bytes: Capacity["usedBytes"]) => {
    if (
      !bytes ||
      !Number.isFinite(bytes.minimum) ||
      !Number.isFinite(bytes.maximum) ||
      bytes.minimum < 0 ||
      bytes.maximum < bytes.minimum
    )
      return t("pikpakCli.capacity.unreported");
    if (bytes.minimum === bytes.maximum) return humanFileSize(bytes.minimum, true, 2);
    return t("pikpakCli.capacity.approximate", {
      size: humanFileSize(bytes.minimum + (bytes.maximum - bytes.minimum) / 2, true, 2),
    });
  };
  const checked = lastRead?.checkedAt ? new Date(lastRead.checkedAt) : undefined;
  const locale = i18n?.language === "cn" ? "zh-CN" : i18n?.language;
  return (
    <section aria-label={t("pikpakCli.capacity.title")} className="space-y-2">
      <div className="flex items-center gap-3">
        <h4 className="text-sm font-medium">{t("pikpakCli.capacity.title")}</h4>
        <Button size="sm" variant="flat" isLoading={loading} onPress={() => void refresh()}>
          {t("pikpakCli.capacity.refresh")}
        </Button>
        <ExternalLink href={urls.PikPakDrive} size="sm">
          {t("pikpakCli.capacity.openDrive")}
        </ExternalLink>
      </div>
      {notice && (
        <p className="text-xs text-warning-600" role="status">
          {notice}
        </p>
      )}
      {lastRead ? (
        <>
          <dl className="flex flex-wrap gap-x-5 gap-y-2 text-sm">
            {(["usedBytes", "totalBytes", "availableBytes", "trashBytes"] as const).map((field) => (
              <div key={field}>
                <dt className="text-xs text-default-500">{t(`pikpakCli.capacity.${field}`)}</dt>
                <dd className="tabular-nums">
                  {lastRead.unlimited && (field === "totalBytes" || field === "availableBytes")
                    ? t("pikpakCli.capacity.unlimited")
                    : format(lastRead[field])}
                </dd>
              </div>
            ))}
          </dl>
          {checked && Number.isFinite(checked.getTime()) && (
            <p className="text-xs text-default-500">
              {t("pikpakCli.capacity.lastRead")}{" "}
              <time dateTime={lastRead.checkedAt}>{checked.toLocaleString(locale)}</time>
            </p>
          )}
        </>
      ) : savedQuota?.usedBytes != null && savedQuota.limitBytes != null ? (
        <p className="text-xs text-default-500">
          {t("pikpakCli.account.storage", {
            used: humanFileSize(savedQuota.usedBytes),
            total: humanFileSize(savedQuota.limitBytes),
          })}
        </p>
      ) : null}
    </section>
  );
}
