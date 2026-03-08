"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  Button,
  Chip,
  Input,
  Modal as NextUiModal,
  ModalBody,
  ModalContent,
  ModalHeader,
  Pagination,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
} from "@heroui/react";
import { AiOutlineDelete, AiOutlineEye } from "react-icons/ai";

import { Modal, toast } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import BApi from "@/sdk/BApi";
import type { BakabaseModulesAIModelsDbLlmCallCacheEntryDbModel } from "@/sdk/Api";
import LlmProviderSelector, { useLlmProviders } from "@/components/LlmProviderSelector";

type CacheEntry = BakabaseModulesAIModelsDbLlmCallCacheEntryDbModel;

const PAGE_SIZE = 20;

const AiCachePage = () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [allEntries, setAllEntries] = useState<CacheEntry[]>([]);
  const [loading, setLoading] = useState(false);
  const [keyword, setKeyword] = useState("");
  const [selectedProviderId, setSelectedProviderId] = useState<number | undefined>(undefined);
  const [page, setPage] = useState(1);
  const [detailEntry, setDetailEntry] = useState<CacheEntry | null>(null);
  const { getProviderLabel } = useLlmProviders();

  const loadEntries = useCallback(async () => {
    setLoading(true);
    try {
      const r = await BApi.ai.getAllLlmCacheEntries();
      if (!r.code && r.data) {
        setAllEntries(r.data);
      }
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadEntries();
  }, []);

  const filteredEntries = useMemo(() => {
    let result = allEntries;
    if (selectedProviderId !== undefined) {
      result = result.filter((e) => e.providerConfigId === selectedProviderId);
    }
    if (keyword) {
      const kw = keyword.toLowerCase();
      result = result.filter(
        (e) =>
          e.modelId.toLowerCase().includes(kw) ||
          e.cacheKey.toLowerCase().includes(kw),
      );
    }
    return result;
  }, [allEntries, keyword, selectedProviderId]);

  const totalPages = Math.max(1, Math.ceil(filteredEntries.length / PAGE_SIZE));
  const pagedEntries = filteredEntries.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE);

  // Reset page when filters change
  useEffect(() => {
    setPage(1);
  }, [keyword, selectedProviderId]);

  const handleDelete = async (id: number) => {
    const r = await BApi.ai.deleteLlmCacheEntry(id);
    if (!r.code) {
      setAllEntries((prev) => prev.filter((e) => e.id !== id));
    }
  };

  const handleClearAll = () => {
    createPortal(Modal, {
      defaultVisible: true,
      title: t<string>("configuration.ai.cache.clearAll"),
      children: t<string>("configuration.ai.cache.clearConfirm"),
      onOk: async () => {
        const r = await BApi.ai.clearAllLlmCache();
        if (!r.code) {
          setAllEntries([]);
          toast.success(t<string>("common.success.saved"));
        }
      },
    });
  };

  const isExpired = (entry: CacheEntry) => {
    if (!entry.expiresAt) return false;
    return new Date(entry.expiresAt) < new Date();
  };

  const formatTime = (iso?: string) => {
    if (!iso) return "-";
    return new Date(iso).toLocaleString();
  };

  const formatJson = (json: string) => {
    try {
      return JSON.stringify(JSON.parse(json), null, 2);
    } catch {
      return json;
    }
  };

  return (
    <div className="flex flex-col gap-4 p-4">
      {/* Search & actions */}
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-2 min-w-0">
          <Input
            size="sm"
            className="min-w-[200px]"
            placeholder={t<string>("configuration.ai.cache.searchPlaceholder")}
            value={keyword}
            onValueChange={setKeyword}
          />
          <LlmProviderSelector
            value={selectedProviderId}
            onChange={setSelectedProviderId}
            placeholder={t<string>("configuration.ai.cache.allProviders")}
          />
          <span className="text-sm text-default-400 whitespace-nowrap">
            {t("configuration.ai.cache.totalEntries", { count: filteredEntries.length })}
          </span>
        </div>
        {allEntries.length > 0 && (
          <Button size="sm" color="danger" variant="flat" onPress={handleClearAll}>
            {t("configuration.ai.cache.clearAll")}
          </Button>
        )}
      </div>

      {/* Cache entries table */}
      <Table removeWrapper size="sm" aria-label="cache entries">
        <TableHeader>
          <TableColumn>{t("configuration.ai.auditLog.provider")}</TableColumn>
          <TableColumn>{t("configuration.ai.auditLog.model")}</TableColumn>
          <TableColumn>{t("configuration.ai.cache.hitCount", { count: "" })}</TableColumn>
          <TableColumn>{t("common.label.createdAt")}</TableColumn>
          <TableColumn>{t("configuration.ai.cache.expiresAt")}</TableColumn>
          <TableColumn width={80}>{" "}</TableColumn>
        </TableHeader>
        <TableBody emptyContent={t<string>("configuration.ai.cache.noEntries")}>
          {pagedEntries.map((entry) => (
            <TableRow key={entry.id}>
              <TableCell>
                <span className="text-sm">{getProviderLabel(entry.providerConfigId)}</span>
              </TableCell>
              <TableCell>
                <span className="font-mono">{entry.modelId}</span>
              </TableCell>
              <TableCell>{entry.hitCount}</TableCell>
              <TableCell>
                <span className="text-default-400">{formatTime(entry.createdAt)}</span>
              </TableCell>
              <TableCell>
                <div className="flex items-center gap-1">
                  <span className="text-default-400">{formatTime(entry.expiresAt)}</span>
                  {isExpired(entry) && (
                    <Chip size="sm" variant="flat" color="warning">
                      {t("configuration.ai.cache.expired")}
                    </Chip>
                  )}
                </div>
              </TableCell>
              <TableCell>
                <div className="flex items-center gap-0.5">
                  <Button
                    isIconOnly
                    size="sm"
                    variant="light"
                    onPress={() => setDetailEntry(entry)}
                  >
                    <AiOutlineEye className="text-lg" />
                  </Button>
                  <Button
                    isIconOnly
                    size="sm"
                    variant="light"
                    color="danger"
                    onPress={() => handleDelete(entry.id)}
                  >
                    <AiOutlineDelete className="text-lg" />
                  </Button>
                </div>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>

      {totalPages > 1 && (
        <div className="flex justify-center">
          <Pagination
            page={page}
            total={totalPages}
            onChange={setPage}
          />
        </div>
      )}

      {/* Detail Modal */}
      <NextUiModal
        size="2xl"
        isOpen={!!detailEntry}
        onClose={() => setDetailEntry(null)}
        scrollBehavior="inside"
      >
        <ModalContent>
          {detailEntry && (
            <>
              <ModalHeader>{t("configuration.ai.cache.detail")}</ModalHeader>
              <ModalBody className="pb-6">
                <div className="flex flex-col gap-4">
                  {/* Basic info */}
                  <div className="grid grid-cols-2 gap-2 text-sm">
                    <div>
                      <span className="text-default-400">{t("configuration.ai.auditLog.provider")}: </span>
                      {getProviderLabel(detailEntry.providerConfigId)}
                    </div>
                    <div>
                      <span className="text-default-400">{t("configuration.ai.auditLog.model")}: </span>
                      <span className="font-mono">{detailEntry.modelId}</span>
                    </div>
                    <div>
                      <span className="text-default-400">{t("common.label.createdAt")}: </span>
                      {formatTime(detailEntry.createdAt)}
                    </div>
                    <div>
                      <span className="text-default-400">{t("configuration.ai.cache.expiresAt")}: </span>
                      {formatTime(detailEntry.expiresAt)}
                      {isExpired(detailEntry) && (
                        <Chip size="sm" variant="flat" color="warning" className="ml-1">
                          {t("configuration.ai.cache.expired")}
                        </Chip>
                      )}
                    </div>
                    <div>
                      <span className="text-default-400">{t("configuration.ai.cache.hitCount", { count: "" })}: </span>
                      {detailEntry.hitCount}
                    </div>
                  </div>

                  {/* Cache Key */}
                  <div>
                    <div className="text-sm font-medium mb-1">{t("configuration.ai.cache.cacheKey")}</div>
                    <pre className="whitespace-pre-wrap break-all text-sm bg-default-100 rounded-lg p-3 max-h-[150px] overflow-auto">
                      {detailEntry.cacheKey}
                    </pre>
                  </div>

                  {/* Response Content */}
                  <div>
                    <div className="text-sm font-medium mb-1">{t("configuration.ai.cache.responseContent")}</div>
                    <pre className="whitespace-pre-wrap break-all text-sm bg-default-100 rounded-lg p-3 max-h-[400px] overflow-auto">
                      {formatJson(detailEntry.responseJson)}
                    </pre>
                  </div>
                </div>
              </ModalBody>
            </>
          )}
        </ModalContent>
      </NextUiModal>
    </div>
  );
};

AiCachePage.displayName = "AiCachePage";

export default AiCachePage;
