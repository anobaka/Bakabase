import type { DestroyableProps } from "@/components/bakaui/types";
import type { BakabaseServiceModelsViewAppStorageUsageViewModel } from "@/sdk/Api";

import { useCallback, useEffect, useRef, useState } from "react";
import { DatabaseOutlined, ReloadOutlined } from "@ant-design/icons";
import { useTranslation } from "react-i18next";

import BApi from "@/sdk/BApi";
import { Button, Chip, Modal, Spinner } from "@/components/bakaui";
import FilePathValue from "@/components/FilePathValue";
import { humanFileSize } from "@/components/utils";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

const key = (suffix: string) => `configuration.storageUsage.${suffix}`;

export function StorageUsageModal(props: DestroyableProps) {
  const { t, i18n } = useTranslation();
  const [usage, setUsage] = useState<BakabaseServiceModelsViewAppStorageUsageViewModel>();
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string>();
  const mounted = useRef(false);

  const load = useCallback(async (forceRefresh: boolean) => {
    setLoading(true);
    setError(undefined);
    try {
      const response = await BApi.app.getAppStorageUsage(
        { forceRefresh },
        { showErrorToast: false },
      );

      if (response.code || !response.data) throw new Error(response.message || "");
      if (mounted.current) setUsage(response.data);
    } catch (e) {
      if (mounted.current) setError(e instanceof Error ? e.message : "");
    } finally {
      if (mounted.current) setLoading(false);
    }
  }, []);

  useEffect(() => {
    mounted.current = true;
    void load(false);

    return () => {
      mounted.current = false;
    };
  }, [load]);

  const categories = [...(usage?.categories || [])].sort((a, b) => (b.bytes || 0) - (a.bytes || 0));

  return (
    <Modal
      defaultVisible
      footer={{ actions: ["cancel"] }}
      scrollBehavior="inside"
      size="3xl"
      title={t(key("title"))}
      {...props}
    >
      <div aria-busy={loading} className="flex flex-col gap-4">
        <p className="text-sm text-default-500">{t(key("description"))}</p>
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div aria-live="polite">
            {usage && (
              <>
                <div className="text-3xl font-semibold tabular-nums">
                  {humanFileSize(usage.totalBytes || 0)}
                </div>
                <div className="text-xs text-default-500">
                  {t(key("files"), { count: usage.fileCount || 0 })}
                </div>
              </>
            )}
          </div>
          <Button
            isDisabled={loading}
            isLoading={loading}
            size="sm"
            startContent={<ReloadOutlined />}
            variant="flat"
            onPress={() => void load(true)}
          >
            {t(key("refresh"))}
          </Button>
        </div>
        {loading && (
          <div className="flex items-center gap-2 text-sm text-default-500" role="status">
            <Spinner size="sm" />
            {t(key("scanning"))}
          </div>
        )}
        {error !== undefined && (
          <div className="rounded-lg bg-danger-50 p-3 text-sm text-danger" role="alert">
            {t(key("failed"))}
            {error && <div className="mt-1 break-words text-xs">{error}</div>}
          </div>
        )}
        {usage && (
          <>
            <FilePathValue path={usage.rootPath} />
            <div className="flex flex-wrap items-center gap-2 text-xs text-default-500">
              <Chip size="sm" variant="flat">
                {t(key(usage.fromCache ? "cached" : "fresh"))}
              </Chip>
              {usage.scannedAt && (
                <span>
                  {t(key("scannedAt"), {
                    time: new Date(usage.scannedAt).toLocaleString(i18n.language),
                  })}
                </span>
              )}
            </div>
            {usage.partial && (
              <p className="rounded-lg bg-warning-50 p-3 text-sm text-warning-700" role="alert">
                {t(key("partial"))}
              </p>
            )}
            {!!usage.skippedSymbolicLinks && (
              <p className="text-xs text-default-500">
                {t(key("skippedLinks"), { count: usage.skippedSymbolicLinks })}
              </p>
            )}
            <div className="divide-y divide-default-200 rounded-lg border border-default-200">
              {categories.map((category) => (
                <div key={category.id} className="flex flex-col gap-2 p-3">
                  <div className="flex items-start justify-between gap-3">
                    <div className="min-w-0">
                      <div className="text-sm font-medium">
                        {t(key(`category.${category.id}.label`))}
                      </div>
                      <p className="text-xs text-default-500">
                        {t(key(`category.${category.id}.description`))}
                      </p>
                    </div>
                    <div className="shrink-0 text-right">
                      <div className="text-sm font-medium tabular-nums">
                        {humanFileSize(category.bytes || 0)}
                      </div>
                      <div className="text-xs text-default-500">
                        {t(key("files"), { count: category.fileCount || 0 })}
                      </div>
                    </div>
                  </div>
                  {category.path && <FilePathValue path={category.path} />}
                  {category.partial && (
                    <span className="text-xs text-warning-600">{t(key("categoryPartial"))}</span>
                  )}
                </div>
              ))}
            </div>
            {!!usage.errors?.length && (
              <details className="text-xs text-default-500">
                <summary className="w-fit cursor-pointer">
                  {t(key("errors"), { count: usage.errorCount || usage.errors.length })}
                </summary>
                <ul className="mt-2 flex list-disc flex-col gap-2 pl-4">
                  {usage.errors.map((entry, index) => (
                    <li key={index}>
                      <code className="break-all">{entry.path}</code>
                      <div className="break-words">{entry.message}</div>
                    </li>
                  ))}
                </ul>
                {(usage.errorCount || 0) > usage.errors.length && (
                  <p className="mt-2">{t(key("errorsSample"), { count: usage.errors.length })}</p>
                )}
              </details>
            )}
          </>
        )}
      </div>
    </Modal>
  );
}

export default function StorageUsageButton() {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  return (
    <Button
      size="sm"
      startContent={<DatabaseOutlined />}
      variant="flat"
      onPress={() => createPortal(StorageUsageModal, {})}
    >
      {t(key("open"))}
    </Button>
  );
}
