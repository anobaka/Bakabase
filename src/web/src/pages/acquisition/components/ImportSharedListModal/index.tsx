"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { components } from "@/sdk/BApi2";

import React, { useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineDownload, AiOutlineUpload } from "react-icons/ai";

import { getSharedListTemplate } from "./template";

import BApi from "@/sdk/BApi";
import {
  Button,
  Input,
  Modal,
  Spinner,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  toast,
} from "@/components/bakaui";

type PreviewRow =
  components["schemas"]["Bakabase.Service.Components.Acquisition.SharedListPreviewRow"];

type Row = {
  title?: string;
  url?: string;
  password?: string;
  lineNumber: number;
  include: boolean;
};

type Props = DestroyableProps & {
  onImported?: () => void;
};

/**
 * Import a reviewed snapshot into draft tasks without touching the resource library.
 *
 * The preview is the point: a spreadsheet is read wrongly far more often than it fails to be read,
 * and a list of twenty rows silently turning into twenty wrong tasks is worse than not
 * importing it at all. So nothing is created until the user has seen what was understood — and can
 * correct any of it in place.
 */
const ImportSharedListModal = ({ onImported, onDestroyed }: Props) => {
  const { t, i18n } = useTranslation();
  const template = getSharedListTemplate(i18n.resolvedLanguage ?? i18n.language);
  const fileRef = useRef<HTMLInputElement>(null);

  const [rows, setRows] = useState<Row[]>();
  const [reading, setReading] = useState(false);
  const [readError, setReadError] = useState<string>();
  const [importing, setImporting] = useState(false);

  const read = async (file: File) => {
    setReading(true);
    setRows(undefined);
    setReadError(undefined);
    try {
      const rsp = await BApi.acquisition.previewSharedList({ file });

      if (rsp.code !== 0) {
        setReadError(rsp.message || t<string>("acquisition.sharedList.readFailed"));

        return;
      }

      setRows(
        ((rsp.data ?? []) as PreviewRow[]).map((r) => ({
          title: r.title ?? undefined,
          url: r.url ?? undefined,
          password: r.password ?? undefined,
          lineNumber: r.lineNumber,
          // The old preview flag describes library resources, not acquisition tasks.
          include: true,
        })),
      );
    } catch (error) {
      setReadError(
        error instanceof Error ? error.message : t<string>("acquisition.sharedList.readFailed"),
      );
    } finally {
      setReading(false);
    }
  };

  const edit = (lineNumber: number, patch: Partial<Row>) =>
    setRows((prev) => prev?.map((r) => (r.lineNumber === lineNumber ? { ...r, ...patch } : r)));

  const submit = async () => {
    const chosen = (rows ?? []).filter((r) => r.include);

    if (chosen.length === 0) return;

    setImporting(true);
    try {
      const response = await BApi.acquisition.importAcquisitionDraftList({
        rows: chosen.map((r) => ({
          title: r.title,
          url: r.url,
          password: r.password,
          lineNumber: r.lineNumber,
        })),
      });
      if (response.code || !response.data)
        throw new Error(response.message || t("acquisition.sharedList.readFailed"));
      const result = response.data;

      toast.success(
        t<string>("acquisition.sharedList.imported", {
          created: result.created,
          matched: result.matched,
        }),
      );

      for (const problem of result.problems ?? []) toast.danger(problem);

      onImported?.();
    } finally {
      setImporting(false);
    }
  };

  const chosen = (rows ?? []).filter((r) => r.include).length;

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["cancel", "ok"],
        okProps: {
          children: t<string>("acquisition.sharedList.import", { count: chosen }),
          isDisabled: chosen === 0,
          isLoading: importing,
        },
      }}
      size="5xl"
      title={t<string>("acquisition.sharedList.title")}
      onDestroyed={onDestroyed}
      onOk={submit}
    >
      <div className="flex flex-col gap-3">
        <div className="text-sm text-default-500">
          {t<string>("acquisition.sharedList.description")}
        </div>
        <p className="text-xs text-default-400">{t<string>("acquisition.sharedList.example")}</p>

        <div className="flex flex-wrap items-center gap-2">
          <Button
            as="a"
            download={template.fileName}
            href={template.url}
            size="sm"
            startContent={<AiOutlineDownload className="text-base" />}
            variant="flat"
          >
            {t<string>("acquisition.sharedList.downloadTemplate")}
          </Button>
          <input
            ref={fileRef}
            hidden
            accept=".txt,.csv,.tsv,.xlsx,.xls"
            type="file"
            onChange={(e) => {
              const file = e.target.files?.[0];

              if (file) void read(file);
              e.target.value = "";
            }}
          />
          <Button
            size="sm"
            startContent={<AiOutlineUpload className="text-base" />}
            variant="flat"
            onPress={() => fileRef.current?.click()}
          >
            {t<string>("acquisition.sharedList.choose")}
          </Button>
          {reading && <Spinner size="sm" />}
        </div>

        <p className="text-xs text-default-500">
          {t<string>("acquisition.sharedList.templateHelp")}
        </p>

        {readError && (
          <p className="text-sm text-danger" role="alert">
            {readError}
          </p>
        )}

        {rows && rows.length === 0 && (
          <div className="text-sm text-default-400">
            {t<string>("acquisition.sharedList.nothingRead")}
          </div>
        )}

        {rows && rows.length > 0 && (
          <>
            <Table isCompact aria-label={t<string>("acquisition.sharedList.title")}>
              <TableHeader>
                <TableColumn>{t<string>("acquisition.sharedList.column.include")}</TableColumn>
                <TableColumn>{t<string>("acquisition.sharedList.column.title")}</TableColumn>
                <TableColumn>{t<string>("acquisition.sharedList.column.url")}</TableColumn>
                <TableColumn>{t<string>("acquisition.sharedList.column.password")}</TableColumn>
              </TableHeader>
              <TableBody>
                {rows.map((row) => (
                  <TableRow key={row.lineNumber}>
                    <TableCell>
                      <div className="flex items-center gap-1">
                        <Switch
                          isSelected={row.include}
                          size="sm"
                          onValueChange={(include) => edit(row.lineNumber, { include })}
                        />
                      </div>
                    </TableCell>
                    <TableCell>
                      <Input
                        size="sm"
                        value={row.title ?? ""}
                        onValueChange={(title) => edit(row.lineNumber, { title })}
                      />
                    </TableCell>
                    <TableCell>
                      <Input
                        size="sm"
                        value={row.url ?? ""}
                        onValueChange={(url) => edit(row.lineNumber, { url })}
                      />
                    </TableCell>
                    <TableCell>
                      <Input
                        size="sm"
                        value={row.password ?? ""}
                        onValueChange={(password) => edit(row.lineNumber, { password })}
                      />
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </>
        )}
      </div>
    </Modal>
  );
};

ImportSharedListModal.displayName = "ImportSharedListModal";

export default ImportSharedListModal;
