"use client";

import type { CollectionMemoRange, CollectionMemoTarget } from "./helpers";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineDelete, AiOutlineEdit, AiOutlinePlus, AiOutlineReload } from "react-icons/ai";

import RangeEditor from "./components/RangeEditor";
import TargetEditor from "./components/TargetEditor";
import Timeline from "./components/Timeline";
import { getTimelineDomain, requireSuccess } from "./helpers";

import BApi from "@/sdk/BApi";
import { Button, Card, CardBody, Input, Modal, Spinner } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

const CollectionMemoPage = () => {
  const { t, i18n } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [targets, setTargets] = useState<CollectionMemoTarget[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState(false);
  const [keyword, setKeyword] = useState("");
  const [now, setNow] = useState(Date.now);
  const requestSequence = useRef(0);
  const locale =
    i18n.language === "cn" ? "zh-CN" : i18n.language === "en" ? "en-US" : i18n.language;
  const dateFormatter = useMemo(
    () =>
      new Intl.DateTimeFormat(locale, {
        year: "numeric",
        month: "2-digit",
        day: "2-digit",
        hour: "2-digit",
        minute: "2-digit",
        second: "2-digit",
        hour12: false,
      }),
    [locale],
  );
  const formatDate = (value: number | string) => dateFormatter.format(new Date(value));

  const load = useCallback(async () => {
    const request = ++requestSequence.current;

    setLoading(true);
    setLoadError(false);
    try {
      const response = requireSuccess(
        await BApi.collectionMemo.getCollectionMemoTargets({ showErrorToast: false }),
      );

      if (request !== requestSequence.current) return;

      setTargets((response.data ?? []) as CollectionMemoTarget[]);
      setNow(Date.now());
    } catch {
      if (request === requestSequence.current) setLoadError(true);
    } finally {
      if (request === requestSequence.current) setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();

    return () => {
      ++requestSequence.current;
    };
  }, [load]);
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 60_000);

    return () => window.clearInterval(timer);
  }, []);

  const editTarget = (target?: CollectionMemoTarget) => {
    createPortal(TargetEditor, {
      target,
      onSave: async (name: string) => {
        requireSuccess(
          target
            ? await BApi.collectionMemo.updateCollectionMemoTarget(
                target.id,
                { name },
                { showErrorToast: false },
              )
            : await BApi.collectionMemo.createCollectionMemoTarget(
                { name },
                { showErrorToast: false },
              ),
        );
        await load();
      },
    });
  };

  const editRange = (target: CollectionMemoTarget, range?: CollectionMemoRange) => {
    createPortal(RangeEditor, {
      range,
      targetName: target.name,
      onSave: async (value: { startAt: string; endAt: string }) => {
        requireSuccess(
          range
            ? await BApi.collectionMemo.updateCollectionMemoRange(target.id, range.id, value, {
                showErrorToast: false,
              })
            : await BApi.collectionMemo.createCollectionMemoRange(target.id, value, {
                showErrorToast: false,
              }),
        );
        await load();
      },
    });
  };

  const remove = (target: CollectionMemoTarget, range?: CollectionMemoRange) => {
    createPortal(Modal, {
      defaultVisible: true,
      title: t<string>(
        range ? "collectionMemo.action.deleteRange" : "collectionMemo.action.deleteTarget",
      ),
      children: t<string>(
        range ? "collectionMemo.confirm.deleteRange" : "collectionMemo.confirm.deleteTarget",
        { name: target.name },
      ),
      footer: {
        actions: ["ok", "cancel"],
        okProps: { children: t<string>("common.action.delete"), color: "danger" },
        cancelProps: { children: t<string>("common.action.cancel") },
      },
      onOk: async () => {
        requireSuccess(
          range
            ? await BApi.collectionMemo.deleteCollectionMemoRange(target.id, range.id, {
                showErrorToast: false,
              })
            : await BApi.collectionMemo.deleteCollectionMemoTarget(target.id, {
                showErrorToast: false,
              }),
        );
        await load();
      },
    });
  };

  const domain = getTimelineDomain(targets, now);
  const shown = targets
    .filter((target) =>
      target.name.toLocaleLowerCase(locale).includes(keyword.trim().toLocaleLowerCase(locale)),
    )
    .sort(
      (a, b) =>
        a.name.localeCompare(b.name, locale, { numeric: true, sensitivity: "base" }) || a.id - b.id,
    );

  return (
    <div className="flex flex-col gap-4 p-2">
      <div className="flex flex-col gap-1">
        <h1 className="text-xl font-semibold">{t<string>("collectionMemo.title")}</h1>
        <p className="text-sm text-default-500">{t<string>("collectionMemo.description")}</p>
      </div>
      <div className="flex flex-wrap items-center gap-2">
        <Button
          color="primary"
          size="sm"
          startContent={<AiOutlinePlus />}
          onPress={() => editTarget()}
        >
          {t<string>("collectionMemo.action.addTarget")}
        </Button>
        <Input
          aria-label={t<string>("collectionMemo.action.search")}
          className="max-w-xs"
          placeholder={t<string>("collectionMemo.action.search")}
          size="sm"
          value={keyword}
          onValueChange={setKeyword}
        />
        <Button
          isLoading={loading}
          size="sm"
          startContent={<AiOutlineReload />}
          variant="light"
          onPress={() => {
            void load();
          }}
        >
          {t<string>("common.action.refresh")}
        </Button>
        <div className="ml-auto flex items-center gap-3 text-xs text-default-500">
          <span className="flex items-center gap-1">
            <span aria-hidden className="h-2 w-2 rounded-full bg-success" />
            {t<string>("collectionMemo.timeline.collected")}
          </span>
          <span className="flex items-center gap-1">
            <span aria-hidden className="h-2 w-2 rounded-full bg-default-200" />
            {t<string>("collectionMemo.timeline.uncollected")}
          </span>
        </div>
      </div>
      {loadError && (
        <p className="text-sm text-danger" role="alert">
          {t<string>("collectionMemo.error.load")}
        </p>
      )}
      {loading && targets.length === 0 ? (
        <div className="flex justify-center py-12">
          <Spinner />
        </div>
      ) : targets.length === 0 && !loadError ? (
        <p className="py-12 text-center text-default-500">{t<string>("collectionMemo.empty")}</p>
      ) : shown.length === 0 && !loadError ? (
        <p className="py-8 text-center text-default-500">{t<string>("collectionMemo.noResults")}</p>
      ) : (
        shown.map((target) => (
          <Card key={target.id} shadow="sm">
            <CardBody className="flex flex-col gap-3 p-4">
              <div className="flex flex-wrap items-center gap-2">
                <h2 className="min-w-0 flex-1 break-words text-base font-medium">{target.name}</h2>
                <Button
                  size="sm"
                  startContent={<AiOutlinePlus />}
                  variant="flat"
                  onPress={() => editRange(target)}
                >
                  {t<string>("collectionMemo.action.addRange")}
                </Button>
                <Button
                  isIconOnly
                  aria-label={t<string>("collectionMemo.action.editTarget")}
                  size="sm"
                  variant="light"
                  onPress={() => editTarget(target)}
                >
                  <AiOutlineEdit />
                </Button>
                <Button
                  isIconOnly
                  aria-label={t<string>("collectionMemo.action.deleteTarget")}
                  color="danger"
                  size="sm"
                  variant="light"
                  onPress={() => remove(target)}
                >
                  <AiOutlineDelete />
                </Button>
              </div>
              <Timeline domain={domain} formatDate={formatDate} target={target} />
              {target.ranges.length === 0 ? (
                <p className="text-sm text-default-500">
                  {t<string>("collectionMemo.range.empty")}
                </p>
              ) : (
                <ul className="flex flex-col divide-y divide-default-100">
                  {[...target.ranges]
                    .sort((a, b) => Date.parse(a.startAt) - Date.parse(b.startAt) || a.id - b.id)
                    .map((range) => (
                      <li key={range.id} className="flex flex-wrap items-center gap-2 py-2 text-sm">
                        <div className="min-w-0 flex-1">
                          <time dateTime={range.startAt}>{formatDate(range.startAt)}</time>
                          {range.startAt === range.endAt ? (
                            <span className="ml-2 text-xs text-default-500">
                              {t<string>("collectionMemo.range.point")}
                            </span>
                          ) : (
                            <>
                              <span className="px-2">~</span>
                              <time dateTime={range.endAt}>{formatDate(range.endAt)}</time>
                            </>
                          )}
                        </div>
                        <Button
                          isIconOnly
                          aria-label={t<string>("collectionMemo.action.editRange")}
                          size="sm"
                          variant="light"
                          onPress={() => editRange(target, range)}
                        >
                          <AiOutlineEdit />
                        </Button>
                        <Button
                          isIconOnly
                          aria-label={t<string>("collectionMemo.action.deleteRange")}
                          color="danger"
                          size="sm"
                          variant="light"
                          onPress={() => remove(target, range)}
                        >
                          <AiOutlineDelete />
                        </Button>
                      </li>
                    ))}
                </ul>
              )}
            </CardBody>
          </Card>
        ))
      )}
      <p className="text-xs text-default-500">
        {t<string>("collectionMemo.browsingIntegration.pending")}
      </p>
    </div>
  );
};

export default CollectionMemoPage;
