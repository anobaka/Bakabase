"use client";

import type { BakabaseModulesAliasAbstractionsModelsDomainAlias as AliasGroup } from "@/sdk/Api";

import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  CloseOutlined,
  DeleteOutlined,
  DownloadOutlined,
  MergeOutlined,
  PlusOutlined,
  ReloadOutlined,
  SearchOutlined,
  TagsOutlined,
  UploadOutlined,
} from "@ant-design/icons";

import AliasTextModal from "./components/AliasTextModal";
import CandidateMenu from "./components/CandidateMenu";

import BApi from "@/sdk/BApi";
import { Button, Checkbox, Chip, Input, Modal, Pagination, Spinner } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { FileSystemSelectorModal } from "@/components/FileSystemSelector";
import { toAbsoluteBackendUrl } from "@/config/env";
import { openExternalUrl } from "@/utils/openExternalUrl";

type SearchForm = { pageSize: number; pageIndex: number; fuzzyText?: string };
const initialForm: SearchForm = { pageSize: 20, pageIndex: 1 };

function requireSuccess(response: { code?: number; message?: string }) {
  if (response.code) throw new Error(response.message);
}

export default function AliasPage() {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [form, setForm] = useState(initialForm);
  const formRef = useRef(form);
  const [searchText, setSearchText] = useState("");
  const [aliases, setAliases] = useState<AliasGroup[]>([]);
  const [selected, setSelected] = useState<string[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();
  const [changingPreferred, setChangingPreferred] = useState(false);
  const preferredChangeRef = useRef(false);
  const requestId = useRef(0);

  const search = async (patches: Partial<SearchForm> = {}) => {
    const next = { ...formRef.current, ...patches };
    const request = ++requestId.current;

    formRef.current = next;
    setForm(next);
    setLoading(true);
    setError(undefined);
    try {
      const response = await BApi.alias.searchAliasGroups(next);

      if (request !== requestId.current) return;
      requireSuccess(response);
      const count = response.totalCount ?? 0;
      const lastPage = Math.max(1, Math.ceil(count / next.pageSize));

      if (next.pageIndex > lastPage) {
        await search({ pageIndex: lastPage });

        return;
      }
      setAliases(response.data ?? []);
      setTotalCount(count);
    } catch (cause) {
      if (request === requestId.current)
        setError((cause as Error)?.message || t("alias.error.loadFailed"));
    } finally {
      if (request === requestId.current) setLoading(false);
    }
  };

  useEffect(() => {
    void search();

    return () => {
      requestId.current++;
    };
  }, []);

  const applySearch = () =>
    void search({ fuzzyText: searchText.trim() || undefined, pageIndex: 1 });
  const clearSearch = () => {
    setSearchText("");
    void search({ fuzzyText: undefined, pageIndex: 1 });
  };
  const addAlias = (group?: AliasGroup) =>
    createPortal(AliasTextModal, {
      title: t(group ? "alias.action.addToGroup" : "alias.action.add"),
      description: group
        ? t("alias.input.groupDescription", { text: group.text })
        : t("alias.input.newGroupDescription"),
      onSubmit: async (text: string) => {
        requireSuccess(await BApi.alias.addAlias({ text, preferred: group?.text }));
        await search(group ? {} : { pageIndex: 1 });
      },
    });
  const renameAlias = (text: string) =>
    createPortal(AliasTextModal, {
      title: t("alias.action.rename"),
      initialValue: text,
      onSubmit: async (nextText: string) => {
        if (nextText !== text)
          requireSuccess(
            await BApi.alias.patchAlias({ text: nextText, isPreferred: false }, { text }),
          );
        await search();
      },
    });
  const setPreferred = async (group: AliasGroup, text: string) => {
    if (preferredChangeRef.current) return;
    preferredChangeRef.current = true;
    setChangingPreferred(true);
    setError(undefined);
    try {
      requireSuccess(await BApi.alias.patchAlias({ isPreferred: true }, { text }));
      setSelected((previous) => previous.map((value) => (value === group.text ? text : value)));
      await search();
    } catch (cause) {
      setError((cause as Error)?.message || t("alias.error.updateFailed"));
    } finally {
      preferredChangeRef.current = false;
      setChangingPreferred(false);
    }
  };
  const deleteAlias = (text: string) =>
    createPortal(Modal, {
      defaultVisible: true,
      size: "sm",
      title: t("alias.confirm.deleteSingle"),
      children: (
        <div className="flex flex-col gap-2">
          <p className="break-words font-medium">{text}</p>
          <p className="text-sm text-default-500">{t("alias.confirm.noWayBack")}</p>
        </div>
      ),
      okProps: { color: "danger", children: t("common.action.delete") },
      onOk: async () => {
        requireSuccess(await BApi.alias.deleteAlias({ text }));
        await search();
      },
    });
  const confirmBulk = (operation: "merge" | "delete") => {
    const texts = [...selected];

    createPortal(Modal, {
      defaultVisible: true,
      size: "md",
      title: t(
        operation === "merge"
          ? "alias.confirm.mergeGroupsTitle"
          : "alias.confirm.deleteGroupsTitle",
        { count: texts.length },
      ),
      children: (
        <div className="flex flex-col gap-3">
          <p className="text-sm leading-relaxed text-default-500">
            {t(
              operation === "merge"
                ? "alias.confirm.mergeDescription"
                : "alias.confirm.deleteDescription",
              { preferred: texts[0] },
            )}
          </p>
          <div className="flex max-h-40 flex-wrap gap-1 overflow-auto">
            {texts.map((text, index) => (
              <Chip
                key={text}
                className="h-auto max-w-full py-1"
                classNames={{ content: "whitespace-normal break-words" }}
                color={operation === "merge" && index === 0 ? "primary" : "default"}
                size="sm"
                variant="flat"
              >
                {text}
              </Chip>
            ))}
          </div>
        </div>
      ),
      okProps: {
        color: operation === "delete" ? "danger" : "primary",
        children: t(operation === "delete" ? "common.action.delete" : "alias.action.merge"),
      },
      onOk: async () => {
        requireSuccess(
          operation === "merge"
            ? await BApi.alias.mergeAliasGroups({ preferredTexts: texts })
            : await BApi.alias.deleteAliasGroups({ preferredTexts: texts }),
        );
        setSelected([]);
        await search();
      },
    });
  };
  const importAliases = () =>
    createPortal(FileSystemSelectorModal, {
      targetType: "file",
      filter: (entry: { isDirectoryOrDrive: boolean; path: string }) =>
        entry.isDirectoryOrDrive || entry.path.toLowerCase().endsWith(".csv"),
      onSelected: async (entry: { path: string }) => {
        const progress = createPortal(Modal, {
          defaultVisible: true,
          title: t("alias.label.importing"),
          footer: false,
          isDismissable: false,
          hideCloseButton: true,
        });

        try {
          requireSuccess(await BApi.alias.importAliases({ path: entry.path }));
          setSelected([]);
          await search({ pageIndex: 1 });
        } catch (cause) {
          setError((cause as Error)?.message || t("alias.error.importFailed"));
        } finally {
          progress.destroy();
        }
      },
    });
  const selectedOnPage = aliases.filter((group) => selected.includes(group.text)).length;
  const busy = loading || changingPreferred;

  return (
    <div className="mx-auto flex w-full max-w-6xl flex-col gap-5 pb-6">
      <header className="flex flex-wrap items-start justify-between gap-4">
        <div className="min-w-0">
          <div className="flex items-center gap-2.5">
            <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-primary/10 text-primary">
              <TagsOutlined aria-hidden className="text-xl" />
            </div>
            <h1 className="text-2xl font-semibold tracking-tight">{t("alias.title")}</h1>
            <Chip size="sm" variant="flat">
              {t("alias.label.groupCount", { count: totalCount })}
            </Chip>
          </div>
          <p className="mt-2 max-w-2xl text-sm leading-relaxed text-default-500">
            {t("alias.info.description")}
          </p>
        </div>
        <Button
          color="primary"
          size="sm"
          startContent={<PlusOutlined aria-hidden />}
          onPress={() => addAlias()}
        >
          {t("alias.action.add")}
        </Button>
      </header>
      <section className="flex flex-wrap items-center gap-2 rounded-xl border border-default-200 bg-content1 p-3">
        <form
          className="flex min-w-0 flex-1 basis-full items-center gap-2 sm:basis-72"
          onSubmit={(event) => {
            event.preventDefault();
            applySearch();
          }}
        >
          <Input
            isClearable
            aria-label={t("alias.input.searchLabel")}
            className="min-w-0 flex-1"
            placeholder={t("alias.input.searchPlaceholder")}
            size="sm"
            startContent={<SearchOutlined aria-hidden className="text-default-400" />}
            value={searchText}
            onClear={clearSearch}
            onValueChange={setSearchText}
          />
          <Button isLoading={loading} size="sm" type="submit" variant="flat">
            {t("alias.action.search")}
          </Button>
        </form>
        <div className="ml-auto flex shrink-0 items-center gap-1">
          <Button
            isIconOnly
            aria-label={t("alias.action.refresh")}
            isDisabled={busy}
            size="sm"
            title={t("alias.action.refresh")}
            variant="light"
            onPress={() => void search()}
          >
            <ReloadOutlined aria-hidden />
          </Button>
          <span aria-hidden className="mx-1 h-5 w-px bg-default-200" />
          <Button
            size="sm"
            startContent={<UploadOutlined aria-hidden />}
            variant="light"
            onPress={importAliases}
          >
            {t("alias.action.import")}
          </Button>
          <Button
            size="sm"
            startContent={<DownloadOutlined aria-hidden />}
            variant="light"
            onPress={() => openExternalUrl(toAbsoluteBackendUrl("/alias/xlsx"))}
          >
            {t("alias.action.export")}
          </Button>
        </div>
      </section>
      {error && (
        <div className="flex items-center justify-between gap-3 rounded-xl border border-danger/20 bg-danger/5 p-3">
          <p className="text-sm text-danger" role="alert">
            {error}
          </p>
          <Button size="sm" variant="light" onPress={() => void search()}>
            {t("alias.action.retry")}
          </Button>
        </div>
      )}
      {selected.length > 0 && (
        <section
          aria-label={t("alias.label.bulkOperations")}
          className="flex flex-col gap-3 rounded-xl border border-primary/20 bg-primary/5 p-3"
        >
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div className="text-sm font-medium">
              {t("alias.selection.count", { count: selected.length })}
            </div>
            <div className="flex flex-wrap items-center gap-1">
              <Button
                color="primary"
                isDisabled={selected.length < 2 || busy}
                size="sm"
                startContent={<MergeOutlined aria-hidden />}
                variant="flat"
                onPress={() => confirmBulk("merge")}
              >
                {t("alias.action.merge")}
              </Button>
              <Button
                color="danger"
                isDisabled={busy}
                size="sm"
                startContent={<DeleteOutlined aria-hidden />}
                variant="light"
                onPress={() => confirmBulk("delete")}
              >
                {t("common.action.delete")}
              </Button>
              <Button size="sm" variant="light" onPress={() => setSelected([])}>
                {t("alias.action.clearSelection")}
              </Button>
            </div>
          </div>
          <div className="flex max-h-28 flex-wrap gap-1.5 overflow-auto">
            {selected.map((text, index) => (
              <div
                key={text}
                className={`flex max-w-full items-center rounded-lg ${index === 0 ? "bg-primary/10 text-primary" : "bg-content1 text-default-600"}`}
              >
                <button
                  aria-label={t("alias.selection.choosePreferred", { text })}
                  className="min-h-8 min-w-0 rounded-l-lg px-2.5 py-1 text-left text-xs focus-visible:outline focus-visible:outline-2 focus-visible:outline-focus"
                  type="button"
                  onClick={() =>
                    setSelected((previous) => [text, ...previous.filter((value) => value !== text)])
                  }
                >
                  <span className="whitespace-normal break-words">{text}</span>
                </button>
                <Button
                  isIconOnly
                  aria-label={t("alias.selection.remove", { text })}
                  className="h-8 min-w-8 rounded-l-none"
                  size="sm"
                  variant="light"
                  onPress={() =>
                    setSelected((previous) => previous.filter((value) => value !== text))
                  }
                >
                  <CloseOutlined aria-hidden className="text-xs" />
                </Button>
              </div>
            ))}
          </div>
          <p className="text-xs leading-relaxed text-default-500">
            {t("alias.selection.mergeTarget", { text: selected[0] })}
          </p>
        </section>
      )}
      <section aria-busy={loading} className="flex min-w-0 flex-col gap-2">
        <div className="flex flex-wrap items-center justify-between gap-2 px-1">
          <Checkbox
            aria-label={t("alias.selection.selectPage")}
            isDisabled={busy || aliases.length === 0}
            isIndeterminate={selectedOnPage > 0 && selectedOnPage < aliases.length}
            isSelected={aliases.length > 0 && selectedOnPage === aliases.length}
            size="sm"
            onValueChange={(checked) =>
              setSelected((previous) =>
                checked
                  ? [...new Set([...previous, ...aliases.map((group) => group.text)])]
                  : previous.filter((text) => !aliases.some((group) => group.text === text)),
              )
            }
          >
            <span className="text-xs text-default-500">{t("alias.selection.selectPage")}</span>
          </Checkbox>
          <span className="text-xs text-default-400">
            {loading
              ? t("alias.label.loading")
              : t("alias.label.resultRange", {
                  start: totalCount === 0 ? 0 : (form.pageIndex - 1) * form.pageSize + 1,
                  end: Math.min(form.pageIndex * form.pageSize, totalCount),
                  total: totalCount,
                })}
          </span>
        </div>
        {loading && aliases.length === 0 ? (
          <div
            className="flex min-h-48 items-center justify-center gap-3 text-sm text-default-500"
            role="status"
          >
            <Spinner size="sm" />
            {t("alias.label.loading")}
          </div>
        ) : error && aliases.length === 0 ? null : aliases.length === 0 ? (
          <div className="flex min-h-60 flex-col items-center justify-center gap-3 rounded-xl border border-dashed border-default-200 bg-default-50 px-6 py-8 text-center">
            <TagsOutlined aria-hidden className="text-3xl text-default-300" />
            <h2 className="text-base font-medium">
              {t(form.fuzzyText ? "alias.empty.noResults" : "alias.empty.title")}
            </h2>
            <p className="max-w-md text-sm leading-relaxed text-default-500">
              {t(form.fuzzyText ? "alias.empty.searchDescription" : "alias.empty.description")}
            </p>
            <Button
              color="primary"
              size="sm"
              variant="flat"
              onPress={form.fuzzyText ? clearSearch : () => addAlias()}
            >
              {t(form.fuzzyText ? "alias.action.clearSearch" : "alias.action.add")}
            </Button>
          </div>
        ) : (
          <div className="flex flex-col gap-2">
            {aliases.map((group) => (
              <article
                key={group.text}
                className={`min-w-0 rounded-xl border bg-content1 p-4 transition-colors ${selected.includes(group.text) ? "border-primary/40 bg-primary/5" : "border-default-200"}`}
              >
                <div className="flex items-start gap-3">
                  <Checkbox
                    aria-label={t("alias.selection.selectGroup", { text: group.text })}
                    className="mt-0.5"
                    isDisabled={busy}
                    isSelected={selected.includes(group.text)}
                    size="sm"
                    onValueChange={(checked) =>
                      setSelected((previous) =>
                        checked
                          ? [...previous, group.text]
                          : previous.filter((text) => text !== group.text),
                      )
                    }
                  />
                  <div className="min-w-0 flex-1">
                    <div className="mb-1 flex items-center gap-2">
                      <span className="text-xs font-medium text-default-400">
                        {t("alias.label.preferred")}
                      </span>
                      <span className="text-xs tabular-nums text-default-400">
                        {t("alias.label.aliasCount", { count: group.candidates?.length ?? 0 })}
                      </span>
                    </div>
                    <h2 className="break-words text-base font-semibold leading-snug">
                      {group.text}
                    </h2>
                  </div>
                  <Button
                    isDisabled={busy}
                    size="sm"
                    startContent={<PlusOutlined aria-hidden />}
                    variant="light"
                    onPress={() => addAlias(group)}
                  >
                    {t("alias.action.addToGroup")}
                  </Button>
                </div>
                <div className="mt-3 flex min-w-0 flex-wrap items-center gap-1.5 pl-7">
                  {group.candidates?.length ? (
                    group.candidates.map((text) => (
                      <CandidateMenu
                        key={text}
                        isDisabled={busy}
                        text={text}
                        onDelete={() => deleteAlias(text)}
                        onPreferred={() => void setPreferred(group, text)}
                        onRename={() => renameAlias(text)}
                      />
                    ))
                  ) : (
                    <span className="py-1 text-sm text-default-400">{t("alias.empty.group")}</span>
                  )}
                </div>
              </article>
            ))}
          </div>
        )}
      </section>
      {totalCount > form.pageSize && (
        <div className="flex justify-center pt-1">
          <Pagination
            showControls
            isDisabled={loading}
            page={form.pageIndex}
            size="sm"
            total={Math.ceil(totalCount / form.pageSize)}
            onChange={(pageIndex) => void search({ pageIndex })}
          />
        </div>
      )}
    </div>
  );
}
