"use client";

import React, { useEffect, useMemo, useRef, useState } from "react";
import { FileZipOutlined } from "@ant-design/icons";
import { useTranslation } from "react-i18next";
import { Button, Modal, Spinner, Tooltip, toast } from "@/components/bakaui";
import ArchivePasswordInput from "@/components/PasswordSelector/ArchivePasswordInput";
import { useResourceOwnerScope } from "@/features/management/resourceContext";
import { PasswordSearchOrder } from "@/sdk/constants";
import BApi from "@/sdk/BApi";

interface Props {
  trigger: React.ReactElement;
  entry: { path: string };
  passwords?: string[];
}
type Source = "filename" | "recent" | "frequent";
type Candidate = { value: string; sources: Source[] };
type Selection = { path: string; ownerKey?: string; candidates: string[] };

function mergeCandidates(groups: [Source, string[]][]): Candidate[] {
  const values = new Map<string, Candidate>();
  for (const [source, passwords] of groups) for (const value of passwords) {
    // Passwords are case-sensitive and may contain whitespace. Never normalize their contents.
    if (typeof value !== "string" || value.length === 0) continue;
    const candidate = values.get(value) ?? { value, sources: [] };
    if (!candidate.sources.includes(source)) candidate.sources.push(source);
    values.set(value, candidate);
  }
  return [...values.values()];
}

export default function DecompressBalloon({ trigger, entry, passwords = [] }: Props) {
  const { t } = useTranslation();
  const owner = useResourceOwnerScope();
  const canUseServer = !owner || owner.source.isLocal;
  const [selection, setSelection] = useState<Selection>();
  const [password, setPassword] = useState("");
  const [retry, setRetry] = useState(0);
  const [saved, setSaved] = useState<{ selection: Selection; groups: [Source, string[]][]; failed: boolean }>();
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string>();
  const pending = useRef(false);
  const mounted = useRef(true);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  const current = selection?.path === entry.path && selection?.ownerKey === owner?.key;

  useEffect(() => {
    if (!selection || !current || !canUseServer) return;
    const request = new AbortController();
    setSaved(undefined);
    void Promise.allSettled([
      BApi.password.searchPasswords({ order: PasswordSearchOrder.Latest, pageSize: 5 },
        { signal: request.signal, showErrorToast: false, sensitiveResponse: true }),
      BApi.password.searchPasswords({ order: PasswordSearchOrder.Frequency, pageSize: 5 },
        { signal: request.signal, showErrorToast: false, sensitiveResponse: true }),
    ]).then(results => {
      if (request.signal.aborted) return;
      const groups: [Source, string[]][] = [];
      let failed = false;
      results.forEach((result, index) => {
        if (result.status !== "fulfilled" || result.value.code !== 0) { failed = true; return; }
        groups.push([index === 0 ? "recent" : "frequent", (result.value.data ?? []).map(item => item.text)]);
      });
      setSaved({ selection, groups, failed });
    });
    return () => request.abort();
  }, [selection, current, canUseServer, retry]);

  const savedForSelection = saved?.selection === selection ? saved : undefined;
  const candidates = useMemo(() => mergeCandidates([
    ["filename", selection?.candidates ?? []], ...(savedForSelection?.groups ?? []),
  ]), [selection, savedForSelection]);
  const close = () => {
    if (pending.current) return;
    setSelection(undefined); setPassword(""); setError(undefined);
  };
  const start = async () => {
    if (!selection || !current || !canUseServer || pending.current) return;
    pending.current = true; setSubmitting(true); setError(undefined);
    try {
      const response = await BApi.file.decompressFiles(
        // An explicit empty string disables the server's filename-password fallback.
        { paths: [selection.path], password }, { showErrorToast: false, sensitiveResponse: true },
      );
      if (!mounted.current) return;
      if (response.code !== 0) { setError(response.message || t("fileExplorer.decompress.failed")); return; }
      setSelection(undefined); setPassword("");
      toast.success(t("fileExplorer.decompress.started"));
    } catch {
      if (mounted.current) setError(t("fileExplorer.decompress.failed"));
    } finally {
      pending.current = false;
      if (mounted.current) setSubmitting(false);
    }
  };

  return <>
    {React.cloneElement(trigger, {
      onClick: (event: React.MouseEvent) => event.stopPropagation(),
      onPress: () => {
        if (pending.current) return;
        setSelection({ path: entry.path, ownerKey: owner?.key, candidates: passwords.slice() });
        setPassword(""); setError(undefined);
      },
    })}
    {selection && <Modal visible size="md" scrollBehavior="inside" onClose={close}
      title={<span className="inline-flex items-center gap-2"><FileZipOutlined aria-hidden />{t("fileExplorer.decompress.title")}</span>}
      isDismissable={!submitting} isKeyboardDismissDisabled={submitting} hideCloseButton={submitting}
      footer={<div className="flex w-full flex-wrap justify-end gap-2">
        <Button variant="light" isDisabled={submitting} onPress={close}>{t("common.action.close")}</Button>
        <Button color="primary" isLoading={submitting} isDisabled={!current || !canUseServer || submitting} onPress={start}>
          {t("fileExplorer.decompress.start")}
        </Button>
      </div>}>
      <div className="min-w-0 space-y-1">
        <p className="text-sm font-medium [overflow-wrap:anywhere]">{selection.path.replace(/\\/g, "/").split("/").pop()}</p>
        <p className="max-h-20 overflow-auto whitespace-pre-wrap break-all text-xs text-default-500">{selection.path}</p>
        <p className="text-xs text-default-500">{t("fileExplorer.decompress.destination")}</p>
      </div>
      {!current || !canUseServer ? <p role="alert" className="text-sm text-warning">{t("fileExplorer.decompress.unavailable")}</p> : <>
        <ArchivePasswordInput label={t("fileExplorer.decompress.password")} value={password}
          size="sm" isDisabled={submitting} onValueChange={setPassword} />
        <div className="min-w-0 space-y-1.5">
          <div className="flex items-center gap-2 text-xs text-default-500">
            <span>{t("fileExplorer.decompress.quickChoices")}</span>
            {!savedForSelection && <Spinner size="sm" aria-label={t("common.state.loading")} />}
          </div>
          <div className="flex max-h-32 min-w-0 flex-wrap gap-1.5 overflow-auto">
            <Button size="sm" radius="full" variant={password === "" ? "flat" : "bordered"}
              color={password === "" ? "primary" : "default"} aria-pressed={password === ""}
              isDisabled={submitting} onPress={() => setPassword("")}>{t("fileExplorer.decompress.noPassword")}</Button>
            {candidates.map(candidate => <Tooltip key={candidate.value} delay={650} placement="bottom"
              content={candidate.sources.map(source => t(`fileExplorer.decompress.source.${source}`)).join(" · ")}>
              <Button type="button" size="sm" radius="full" isDisabled={submitting}
                className="h-auto min-h-7 min-w-0 max-w-full whitespace-normal px-2.5 py-1"
                color={password === candidate.value ? "primary" : "default"} variant="flat"
                aria-pressed={password === candidate.value} onPress={() => setPassword(candidate.value)}>
                <span className="min-w-0 whitespace-pre-wrap [overflow-wrap:anywhere]">{candidate.value}</span>
              </Button>
            </Tooltip>)}
          </div>
          <p className="text-xs text-default-500">{t("fileExplorer.decompress.choicesHint")}</p>
          {savedForSelection?.failed && <div role="status" className="flex flex-wrap items-center gap-1 text-xs text-warning">
            <span>{t("fileExplorer.decompress.passwordsFailed")}</span>
            <Button size="sm" variant="light" isDisabled={submitting} onPress={() => setRetry(value => value + 1)}>{t("common.action.retry")}</Button>
          </div>}
        </div>
      </>}
      {error && <p role="alert" className="whitespace-pre-wrap break-words text-sm text-danger">{error}</p>}
    </Modal>}
  </>;
}
