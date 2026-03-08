"use client";

import { useCallback, useEffect, useState } from "react";
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
  Tooltip,
} from "@heroui/react";
import { AiOutlineEye } from "react-icons/ai";

import BApi from "@/sdk/BApi";
import type { BakabaseModulesAIModelsDbLlmUsageLogDbModel } from "@/sdk/Api";
import { LlmCallStatus } from "@/sdk/constants";
import LlmProviderSelector, { useLlmProviders } from "@/components/LlmProviderSelector";

type UsageLog = BakabaseModulesAIModelsDbLlmUsageLogDbModel;

const StatusChip = ({ status }: { status: number }) => {
  const { t } = useTranslation();
  const map: Record<number, { color: "success" | "danger" | "warning" | "default"; key: string }> = {
    [LlmCallStatus.Success]: { color: "success", key: "success" },
    [LlmCallStatus.Error]: { color: "danger", key: "error" },
    [LlmCallStatus.Timeout]: { color: "warning", key: "timeout" },
    [LlmCallStatus.Cancelled]: { color: "default", key: "cancelled" },
  };
  const info = map[status] ?? { color: "default" as const, key: "unknown" };
  return (
    <Chip size="sm" variant="flat" color={info.color}>
      {t(`configuration.ai.auditLog.status.${info.key}`)}
    </Chip>
  );
};

const PAGE_SIZE = 50;

const AiAuditLogPage = () => {
  const { t } = useTranslation();
  const [logs, setLogs] = useState<UsageLog[]>([]);
  const [page, setPage] = useState(1);
  const [hasMore, setHasMore] = useState(false);
  const [loading, setLoading] = useState(false);
  const [keyword, setKeyword] = useState("");
  const [searchKeyword, setSearchKeyword] = useState("");
  const [selectedProviderId, setSelectedProviderId] = useState<number | undefined>(undefined);
  const [detailLog, setDetailLog] = useState<UsageLog | null>(null);
  const { getProviderLabel } = useLlmProviders();

  const load = useCallback(async (pageNum: number, kw: string, providerId?: number) => {
    setLoading(true);
    try {
      const r = await BApi.ai.searchLlmUsage({
        pageIndex: pageNum - 1,
        pageSize: PAGE_SIZE,
        modelId: kw || undefined,
        providerConfigId: providerId,
      });
      if (!r.code && r.data) {
        setLogs(r.data);
        setHasMore(r.data.length >= PAGE_SIZE);
      }
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    load(page, searchKeyword, selectedProviderId);
  }, [page, searchKeyword, selectedProviderId]);

  const handleSearch = () => {
    setPage(1);
    setSearchKeyword(keyword);
  };

  const formatDuration = (ms: number) => {
    if (ms < 1000) return `${ms}ms`;
    return `${(ms / 1000).toFixed(1)}s`;
  };

  const formatTime = (iso: string) => {
    return new Date(iso).toLocaleString();
  };

  const totalPages = hasMore ? page + 1 : page;

  return (
    <div className="flex flex-col gap-4 p-4">
      <div className="flex items-center gap-2 min-w-0">
        <Input
          size="sm"
          className="min-w-[200px]"
          placeholder={t<string>("configuration.ai.auditLog.searchPlaceholder")}
          value={keyword}
          onValueChange={setKeyword}
          onKeyDown={(e) => e.key === "Enter" && handleSearch()}
        />
        <LlmProviderSelector
          value={selectedProviderId}
          onChange={(id) => { setSelectedProviderId(id); setPage(1); }}
          placeholder={t<string>("configuration.ai.auditLog.allProviders")}
        />
      </div>

      <Table removeWrapper size="sm" aria-label="audit log">
        <TableHeader>
          <TableColumn>{t("configuration.ai.auditLog.provider")}</TableColumn>
          <TableColumn>{t("configuration.ai.auditLog.model")}</TableColumn>
          <TableColumn>{t("configuration.ai.auditLog.feature")}</TableColumn>
          <TableColumn>{t("configuration.ai.auditLog.tokens")}</TableColumn>
          <TableColumn>{t("configuration.ai.auditLog.duration")}</TableColumn>
          <TableColumn>{t("configuration.ai.auditLog.status")}</TableColumn>
          <TableColumn>{t("configuration.ai.auditLog.time")}</TableColumn>
          <TableColumn width={60}>{" "}</TableColumn>
        </TableHeader>
        <TableBody emptyContent={t<string>("configuration.ai.auditLog.noLogs")}>
          {logs.map((log) => (
            <TableRow key={log.id}>
              <TableCell>
                <span className="text-sm">{getProviderLabel(log.providerConfigId)}</span>
              </TableCell>
              <TableCell>
                <span className="font-mono">{log.modelId}</span>
              </TableCell>
              <TableCell>
                {log.feature && <Chip size="sm" variant="flat">{t(`configuration.ai.feature.${log.feature}`, log.feature)}</Chip>}
              </TableCell>
              <TableCell>
                <Tooltip
                  content={
                    <div className="text-xs">
                      <div>Input: {log.inputTokens.toLocaleString()}</div>
                      <div>Output: {log.outputTokens.toLocaleString()}</div>
                      <div>Total: {log.totalTokens.toLocaleString()}</div>
                    </div>
                  }
                >
                  <span className="cursor-default">
                    {t("configuration.ai.auditLog.inputOutput", {
                      input: log.inputTokens.toLocaleString(),
                      output: log.outputTokens.toLocaleString(),
                    })}
                  </span>
                </Tooltip>
              </TableCell>
              <TableCell>{formatDuration(log.durationMs)}</TableCell>
              <TableCell>
                <div className="flex items-center gap-1">
                  <StatusChip status={log.status} />
                  {log.cacheHit && (
                    <Chip size="sm" variant="flat" color="primary">
                      {t("configuration.ai.auditLog.cacheHit")}
                    </Chip>
                  )}
                </div>
                {log.errorMessage && (
                  <Tooltip content={log.errorMessage}>
                    <span className="text-xs text-danger cursor-default truncate max-w-[200px] block">
                      {log.errorMessage}
                    </span>
                  </Tooltip>
                )}
              </TableCell>
              <TableCell>
                <span className="text-default-400">{formatTime(log.createdAt)}</span>
              </TableCell>
              <TableCell>
                <Button
                  isIconOnly
                  size="sm"
                  variant="light"
                  onPress={() => setDetailLog(log)}
                >
                  <AiOutlineEye className="text-lg" />
                </Button>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>

      {(totalPages > 1 || logs.length > 0) && (
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
        isOpen={!!detailLog}
        onClose={() => setDetailLog(null)}
        scrollBehavior="inside"
      >
        <ModalContent>
          {detailLog && (
            <>
              <ModalHeader>{t("configuration.ai.auditLog.detail")}</ModalHeader>
              <ModalBody className="pb-6">
                <div className="flex flex-col gap-4">
                  {/* Basic info */}
                  <div className="grid grid-cols-2 gap-2 text-sm">
                    <div>
                      <span className="text-default-400">{t("configuration.ai.auditLog.provider")}: </span>
                      {getProviderLabel(detailLog.providerConfigId)}
                    </div>
                    <div>
                      <span className="text-default-400">{t("configuration.ai.auditLog.model")}: </span>
                      <span className="font-mono">{detailLog.modelId}</span>
                    </div>
                    <div>
                      <span className="text-default-400">{t("configuration.ai.auditLog.feature")}: </span>
                      {detailLog.feature ? t(`configuration.ai.feature.${detailLog.feature}`, detailLog.feature) : "-"}
                    </div>
                    <div>
                      <span className="text-default-400">{t("configuration.ai.auditLog.duration")}: </span>
                      {formatDuration(detailLog.durationMs)}
                    </div>
                    <div>
                      <span className="text-default-400">{t("configuration.ai.auditLog.tokens")}: </span>
                      {`${detailLog.inputTokens} / ${detailLog.outputTokens} (${detailLog.totalTokens})`}
                    </div>
                    <div>
                      <span className="text-default-400">{t("configuration.ai.auditLog.time")}: </span>
                      {formatTime(detailLog.createdAt)}
                    </div>
                  </div>

                  {/* Request */}
                  <div>
                    <div className="text-sm font-medium mb-1">{t("configuration.ai.auditLog.request")}</div>
                    <pre className="whitespace-pre-wrap break-all text-sm bg-default-100 rounded-lg p-3 max-h-[300px] overflow-auto">
                      {detailLog.requestSummary || t<string>("configuration.ai.auditLog.noContent")}
                    </pre>
                  </div>

                  {/* Response */}
                  <div>
                    <div className="text-sm font-medium mb-1">{t("configuration.ai.auditLog.response")}</div>
                    <pre className="whitespace-pre-wrap break-all text-sm bg-default-100 rounded-lg p-3 max-h-[300px] overflow-auto">
                      {detailLog.responseSummary || t<string>("configuration.ai.auditLog.noContent")}
                    </pre>
                  </div>

                  {/* Error */}
                  {detailLog.errorMessage && (
                    <div>
                      <div className="text-sm font-medium mb-1 text-danger">{t("configuration.ai.auditLog.error")}</div>
                      <pre className="whitespace-pre-wrap break-all text-sm bg-danger-50 rounded-lg p-3 max-h-[200px] overflow-auto">
                        {detailLog.errorMessage}
                      </pre>
                    </div>
                  )}
                </div>
              </ModalBody>
            </>
          )}
        </ModalContent>
      </NextUiModal>
    </div>
  );
};

AiAuditLogPage.displayName = "AiAuditLogPage";

export default AiAuditLogPage;
