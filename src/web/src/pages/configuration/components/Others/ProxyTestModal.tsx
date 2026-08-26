"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { BakabaseServiceModelsViewProxyTestResultViewModel } from "@/sdk/Api";

import React, { useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineCheckCircle, AiOutlineCloseCircle, AiOutlineDelete } from "react-icons/ai";

import { Button, Chip, Input, Modal, Spinner } from "@/components/bakaui";
import { DefaultProxyTestSiteIds, ProxyTestSites } from "@/sdk/constants";
import BApi from "@/sdk/BApi";

type Result = BakabaseServiceModelsViewProxyTestResultViewModel;

type Props = {
  /** Saved proxy to test. Omit to test the system proxy or a direct connection. */
  customProxyId?: string;
  /** Test through the system proxy rather than bypassing it. */
  useSystemProxy?: boolean;
  /** Label shown in the title so it is obvious which proxy is being tested. */
  proxyLabel: string;
  initialPresetIds?: string[];
  initialCustomSites?: string[];
  /** Persist the site selection so the next test reuses it. */
  onSelectionPersist?: (presetIds: string[], customSites: string[]) => void;
} & DestroyableProps;

const ProxyTestModal = ({
  customProxyId,
  useSystemProxy,
  proxyLabel,
  initialPresetIds,
  initialCustomSites,
  onSelectionPersist,
  onDestroyed,
}: Props) => {
  const { t } = useTranslation();

  const [presetIds, setPresetIds] = useState<string[]>(
    initialPresetIds?.length ? initialPresetIds : [...DefaultProxyTestSiteIds],
  );
  const [customSites, setCustomSites] = useState<string[]>(initialCustomSites ?? []);
  const [newSite, setNewSite] = useState("");
  const [results, setResults] = useState<Result[]>([]);
  const [testing, setTesting] = useState(false);

  const togglePreset = (id: string) =>
    setPresetIds((prev) => (prev.includes(id) ? prev.filter((p) => p !== id) : [...prev, id]));

  const addCustomSite = () => {
    const url = newSite.trim();

    if (!url || customSites.includes(url)) {
      return;
    }
    setCustomSites((prev) => [...prev, url]);
    setNewSite("");
  };

  const runTest = async () => {
    setTesting(true);
    setResults([]);
    try {
      const rsp = await BApi.options.testProxy({
        customProxyId,
        useSystemProxy: useSystemProxy ?? false,
        presetSiteIds: presetIds,
        customSites,
      });

      setResults(rsp.data ?? []);
      onSelectionPersist?.(presetIds, customSites);
    } finally {
      setTesting(false);
    }
  };

  const total = presetIds.length + customSites.length;
  const succeeded = results.filter((r) => r.succeeded).length;

  return (
    <Modal
      defaultVisible
      footer={{ actions: ["cancel"] }}
      size="2xl"
      title={t<string>("configuration.others.proxy.test.title", { name: proxyLabel })}
      onDestroyed={onDestroyed}
    >
      <div className="flex flex-col gap-4">
        <div className="flex flex-col gap-2">
          <span className="text-sm font-medium">
            {t<string>("configuration.others.proxy.test.presets")}
          </span>
          <div className="flex flex-wrap gap-1">
            {ProxyTestSites.map((s) => (
              <Chip
                key={s.id}
                className="cursor-pointer"
                color={presetIds.includes(s.id) ? "primary" : "default"}
                variant={presetIds.includes(s.id) ? "solid" : "flat"}
                onClick={() => togglePreset(s.id)}
              >
                {s.name}
              </Chip>
            ))}
          </div>
        </div>

        <div className="flex flex-col gap-2">
          <span className="text-sm font-medium">
            {t<string>("configuration.others.proxy.test.customSites")}
          </span>
          <div className="flex items-center gap-2">
            <Input
              className="flex-1"
              placeholder={t<string>("configuration.others.proxy.test.customSitePlaceholder")}
              size="sm"
              value={newSite}
              onKeyDown={(e) => {
                if (e.key === "Enter") {
                  e.preventDefault();
                  addCustomSite();
                }
              }}
              onValueChange={setNewSite}
            />
            <Button size="sm" variant="flat" onPress={addCustomSite}>
              {t<string>("common.action.add")}
            </Button>
          </div>
          {customSites.length > 0 && (
            <div className="flex flex-wrap gap-1">
              {customSites.map((url) => (
                <Chip
                  key={url}
                  endContent={
                    <AiOutlineDelete
                      className="cursor-pointer text-base"
                      onClick={() => setCustomSites((prev) => prev.filter((u) => u !== url))}
                    />
                  }
                  variant="flat"
                >
                  {url}
                </Chip>
              ))}
            </div>
          )}
        </div>

        <div className="flex items-center gap-3">
          <Button color="primary" isDisabled={total === 0 || testing} size="sm" onPress={runTest}>
            {testing && <Spinner size="sm" />}
            {t<string>("configuration.others.proxy.test.run", { count: total })}
          </Button>
          {results.length > 0 && !testing && (
            <span className="text-sm text-foreground-500">
              {t<string>("configuration.others.proxy.test.summary", {
                succeeded,
                total: results.length,
              })}
            </span>
          )}
        </div>

        {results.length > 0 && (
          <div className="flex flex-col divide-y divide-default-200/60 rounded-medium border border-default-200 dark:border-default-100">
            {results.map((r) => (
              <div key={r.url} className="flex items-center gap-2 px-3 py-2">
                {r.succeeded ? (
                  <AiOutlineCheckCircle className="text-success text-lg shrink-0" />
                ) : (
                  <AiOutlineCloseCircle className="text-danger text-lg shrink-0" />
                )}
                <div className="min-w-0 flex-1">
                  <div className="text-sm">{r.name}</div>
                  {/* A failure is only actionable if it says why, so keep the flattened
                      backend message rather than a generic "failed". */}
                  {!r.succeeded && r.error && (
                    <div className="text-xs text-danger-400 break-words">{r.error}</div>
                  )}
                </div>
                {r.succeeded && (
                  <Chip size="sm" variant="flat">
                    {r.statusCode}
                  </Chip>
                )}
                <span className="text-xs text-foreground-400 tabular-nums">{r.elapsedMs}ms</span>
              </div>
            ))}
          </div>
        )}
      </div>
    </Modal>
  );
};

ProxyTestModal.displayName = "ProxyTestModal";

export default ProxyTestModal;
