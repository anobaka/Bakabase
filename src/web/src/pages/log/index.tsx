"use client";

import type { DateValue, RangeValue } from "@heroui/react";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  Table,
  TableHeader,
  TableBody,
  TableColumn,
  TableRow,
  Pagination,
  Input,
  Button,
  Select,
  SelectItem,
  DateRangePicker,
  TableCell,
} from "@heroui/react";

import BApi from "@/sdk/BApi";
import { LogLevel } from "@/sdk/constants";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { Modal } from "@/components/bakaui";

export default function LogPage() {
  const { t } = useTranslation();
  const [logs, setLogs] = useState<any[]>([]);
  const [form, setForm] = useState({
    pageIndex: 1,
    pageSize: 100,
    startDt: undefined as string | undefined,
    endDt: undefined as string | undefined,
    level: undefined as LogLevel | undefined,
    logger: undefined as string | undefined,
    event: undefined as string | undefined,
    message: undefined as string | undefined,
  });
  const [pageable, setPageable] = useState({
    pageIndex: 1,
    totalCount: 0,
  });
  const [dateRange, setDateRange] = useState<RangeValue<DateValue> | null>(
    null,
  );
  const [expandedMsg, setExpandedMsg] = useState<string | null>(null);
  const { createPortal } = useBakabaseContext();

  useEffect(() => {
    search();
    // eslint-disable-next-line
  }, [form.pageIndex, form.pageSize, form.startDt, form.endDt, form.level, form.logger, form.event, form.message]);

  const search = async () => {
    const d = await BApi.log.searchLogs(form);

    setLogs(d.data || []);
    setPageable({
      pageIndex: d.pageIndex,
      totalCount: d.totalCount,
    });
  };

  const patchForm = (patches: Partial<typeof form>) => {
    setPageable((prev) => ({ ...prev, pageIndex: 1 }));
    setForm((prev) => ({ ...prev, ...patches, pageIndex: 1 }));
  };

  const handleDateRangeChange = (range: RangeValue<DateValue> | null) => {
    setDateRange(range);
    patchForm({
      startDt: range?.start
        ? range.start.toDate().toISOString().slice(0, 19).replace("T", " ")
        : undefined,
      endDt: range?.end
        ? range.end.toDate().toISOString().slice(0, 19).replace("T", " ")
        : undefined,
    });
  };

  return (
    <div className="p-4">
      <div className="flex flex-wrap gap-4 items-center mb-4">
        <div className="flex items-center gap-1 min-w-[260px]">
          <span className="font-medium mr-2 min-w-[60px]">
            {t<string>("Time")}
          </span>
          <DateRangePicker
            className="flex-1"
            value={dateRange}
            onChange={handleDateRangeChange}
          />
        </div>
        <div className="flex items-center gap-1 min-w-[100px]">
          <span className="font-medium mr-2 min-w-[60px]">
            {t<string>("Level")}
          </span>
          <Select
            className="min-w-[100px]"
            placeholder={t<string>("All")}
            selectedKeys={
              form.level !== undefined
                ? new Set([String(form.level)])
                : new Set()
            }
            onSelectionChange={(keys) => {
              const key = Array.from(keys)[0];

              patchForm({ level: key ? Number(key) : undefined });
            }}
          >
            {Object.keys(LogLevel)
              .filter((k) => !isNaN(Number(k)))
              .map((k) => (
                <SelectItem key={k}>{LogLevel[Number(k)]}</SelectItem>
              ))}
          </Select>
        </div>
        <div className="flex items-center gap-1 min-w-[120px]">
          <span className="font-medium mr-2 min-w-[60px]">
            {t<string>("Logger")}
          </span>
          <Input
            className="min-w-[120px]"
            placeholder={t<string>("Logger")}
            size="sm"
            value={form.logger}
            onChange={(e) => patchForm({ logger: e.target.value })}
          />
        </div>
        <div className="flex items-center gap-1 min-w-[120px]">
          <span className="font-medium mr-2 min-w-[60px]">
            {t<string>("Event")}
          </span>
          <Input
            className="min-w-[120px]"
            placeholder={t<string>("Event")}
            size="sm"
            value={form.event}
            onChange={(e) => patchForm({ event: e.target.value })}
          />
        </div>
        <div className="flex items-center gap-1 min-w-[120px]">
          <span className="font-medium mr-2 min-w-[60px]">
            {t<string>("Message")}
          </span>
          <Input
            className="min-w-[120px]"
            placeholder={t<string>("Message")}
            size="sm"
            value={form.message}
            onChange={(e) => patchForm({ message: e.target.value })}
          />
        </div>
        <div className="flex items-center gap-1 ml-auto">
          <Button
            size="sm"
            type="button"
            onClick={() => BApi.log.clearAllLog().then(() => setLogs([]))}
          >
            {t<string>("Clear all")}
          </Button>
        </div>
      </div>
      <Table
        removeWrapper
        aria-label="logs"
        className="mb-4"
        selectionMode="none"
        size="sm"
      >
        <TableHeader>
          <TableColumn>{t<string>("Time")}</TableColumn>
          <TableColumn>{t<string>("Level")}</TableColumn>
          <TableColumn>{t<string>("Logger")}</TableColumn>
          <TableColumn>{t<string>("Event")}</TableColumn>
          <TableColumn>{t<string>("Message")}</TableColumn>
        </TableHeader>
        <TableBody items={logs}>
          {(item) => (
            <TableRow key={item.id || item.dateTime + item.logger + item.event}>
              <TableCell>
                {item.dateTime ? item.dateTime.slice(5, 16) : "-"}
              </TableCell>
              <TableCell>
                <span
                  className={
                    item.level === LogLevel.Error ||
                    item.level === LogLevel.Critical ||
                    item.level === LogLevel.Warning
                      ? "text-red-600"
                      : "text-blue-600"
                  }
                >
                  {item.level === LogLevel.Information
                    ? "Info"
                    : LogLevel[item.level]}
                </span>
              </TableCell>
              <TableCell>{item.logger}</TableCell>
              <TableCell>{item.event}</TableCell>
              <TableCell className="max-w-[300px] truncate">
                {item.message && item.message.length > 60 ? (
                  <pre className="inline">
                    {item.message.slice(0, 60)}...
                    <Button
                      color="primary"
                      size="sm"
                      variant="light"
                      onPress={() =>
                        createPortal(Modal, {
                          size: "xl",
                          title: t("Log"),
                          defaultVisible: true,
                          children: (
                            <pre className="whitespace-pre-wrap break-all">
                              {item.message}
                            </pre>
                          ),
                        })
                      }
                    >
                      {t<string>("Expand")}
                    </Button>
                  </pre>
                ) : (
                  <pre className="inline">{item.message}</pre>
                )}
              </TableCell>
            </TableRow>
          )}
        </TableBody>
      </Table>
      <div className="flex justify-center">
        <Pagination
          page={pageable.pageIndex}
          total={pageable.totalCount}
          onChange={(page) => setForm((prev) => ({ ...prev, pageIndex: page }))}
        />
      </div>
      {expandedMsg && (
        <Modal
          title={t<string>("Full Message")}
          onClose={() => setExpandedMsg(null)}
        >
          <pre className="whitespace-pre-wrap break-all">{expandedMsg}</pre>
        </Modal>
      )}
    </div>
  );
}
