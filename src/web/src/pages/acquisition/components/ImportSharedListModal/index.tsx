"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { CollectionModel } from "@/stores/collections";
import type { components } from "@/sdk/BApi2";

import React, { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineUpload } from "react-icons/ai";

import BApi from "@/sdk/BApi";
import {
  Button,
  Chip,
  Input,
  Modal,
  Select,
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
  alreadyKnown: boolean;
  include: boolean;
};

type Props = DestroyableProps & {
  onImported?: () => void;
};

/**
 * Importing somebody's list of things to get.
 *
 * The preview is the point: a spreadsheet is read wrongly far more often than it fails to be read,
 * and a list of twenty rows silently turning into twenty wrong resources is worse than not
 * importing it at all. So nothing is created until the user has seen what was understood — and can
 * correct any of it in place.
 */
const ImportSharedListModal = ({ onImported, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const fileRef = useRef<HTMLInputElement>(null);

  const [rows, setRows] = useState<Row[]>();
  const [reading, setReading] = useState(false);
  const [importing, setImporting] = useState(false);
  const [collections, setCollections] = useState<CollectionModel[]>([]);
  const [collectionId, setCollectionId] = useState<number>();
  const [startAcquiring, setStartAcquiring] = useState(false);

  useEffect(() => {
    BApi.collection
      .getAllCollections({ withProgress: false })
      .then((r) => setCollections((r.data ?? []) as CollectionModel[]));
  }, []);

  const read = async (file: File) => {
    setReading(true);
    try {
      const rsp = await BApi.acquisition.previewSharedList({ file });

      setRows(
        ((rsp.data ?? []) as PreviewRow[]).map((r) => ({
          title: r.title ?? undefined,
          url: r.url ?? undefined,
          password: r.password ?? undefined,
          lineNumber: r.lineNumber,
          alreadyKnown: r.alreadyKnown,
          // Something already known is not an error, but importing it again is rarely what the
          // user wants; unchecking it by default says so without hiding it.
          include: !r.alreadyKnown,
        })),
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
      const result = (
        await BApi.acquisition.importSharedList({
          rows: chosen.map((r) => ({
            title: r.title,
            url: r.url,
            password: r.password,
            lineNumber: r.lineNumber,
          })),
          collectionId,
          startAcquiring,
        })
      ).data;

      if (!result) return;

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

        <div className="flex items-center gap-2">
          <input
            ref={fileRef}
            hidden
            accept=".txt,.csv,.tsv,.xlsx,.xls"
            type="file"
            onChange={(e) => {
              const file = e.target.files?.[0];

              if (file) void read(file);
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
                        {row.alreadyKnown && (
                          <Chip size="sm" variant="flat">
                            {t<string>("acquisition.sharedList.alreadyKnown")}
                          </Chip>
                        )}
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

            <div className="flex items-center gap-3">
              <Select
                className="max-w-xs"
                dataSource={collections.map((c) => ({
                  value: String(c.id),
                  label: c.name,
                  textValue: c.name,
                }))}
                label={t<string>("acquisition.sharedList.collection")}
                selectedKeys={collectionId ? [String(collectionId)] : []}
                size="sm"
                onSelectionChange={(keys) => {
                  const raw = Array.from(keys)[0];

                  setCollectionId(raw == null ? undefined : Number(raw));
                }}
              />
              <Switch isSelected={startAcquiring} size="sm" onValueChange={setStartAcquiring}>
                <span className="text-sm">
                  {t<string>("acquisition.sharedList.startAcquiring")}
                </span>
              </Switch>
            </div>
          </>
        )}
      </div>
    </Modal>
  );
};

ImportSharedListModal.displayName = "ImportSharedListModal";

export default ImportSharedListModal;
