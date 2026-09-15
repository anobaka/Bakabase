"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { Resource } from "@/core/models/Resource";
import type {
  BakabaseServiceModelsViewResourceProfileViewModel as ResourceProfile,
  BakabaseServiceModelsInputResourceSearchInputModel as SearchInput,
} from "@/sdk/Api";

import { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineArrowRight, AiOutlineFile, AiOutlineReload } from "react-icons/ai";

import { checkProfileResponse, profilePreviewSearch } from "../profileUtils";

import { Modal, Chip, Button, Pagination, Spinner } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import ResourceDetailModal from "@/components/Resource/components/DetailModal";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { resolveScopedValue } from "@/core/models/Resource";
import {
  PropertyPool,
  PropertyValueScope,
  propertyValueScopes,
  ReservedProperty,
} from "@/sdk/constants";
import { useResourceOptionsStore } from "@/stores/options";

type PreviewResource = Pick<
  Resource,
  "id" | "path" | "displayName" | "properties" | "scopePreferences"
>;
type Props = { profile: ResourceProfile; isDraft?: boolean } & DestroyableProps;
type Result = {
  key: string;
  status: "ready" | "error";
  resources: PreviewResource[];
  totalCount: number;
};

export default function ResourceProfileTestModal({ profile, isDraft = false, onDestroyed }: Props) {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const configuredPriority = useResourceOptionsStore(
    (state) => state.data?.propertyValueScopePriority,
  );
  const valueScopePriority = useMemo(() => {
    const priority = configuredPriority?.length
      ? [...configuredPriority]
      : propertyValueScopes.map((scope) => scope.value);

    for (const { value } of propertyValueScopes) {
      if (priority.includes(value)) continue;
      if (value === PropertyValueScope.Manual) priority.unshift(value);
      else priority.push(value);
    }

    return priority;
  }, [configuredPriority]);
  const criteriaKey = JSON.stringify(profilePreviewSearch(profile, 1));
  const [pagination, setPagination] = useState({ criteriaKey, page: 1 });
  const page = pagination.criteriaKey === criteriaKey ? pagination.page : 1;
  const [retryKey, setRetryKey] = useState(0);
  const [result, setResult] = useState<Result>();
  const requestKey = `${criteriaKey}:${page}:${retryKey}`;
  const current = result?.key === requestKey ? result : undefined;
  const loading = current === undefined;
  const retry = () => setRetryKey((key) => key + 1);

  useEffect(() => {
    let active = true;
    const controller = new AbortController();
    const { additionalItems, ...search } = profilePreviewSearch(profile, page);

    BApi.resource
      .searchResources(
        search as SearchInput,
        { additionalItems, saveSearch: false },
        { signal: controller.signal },
      )
      .then((response) => {
        if (!active) return;
        checkProfileResponse(response, "Preview unavailable");
        const resources = response.data ?? [];
        const totalCount = Math.max(0, response.totalCount ?? resources.length);
        const lastPage = Math.max(1, Math.ceil(totalCount / search.pageSize));

        if (page > lastPage) {
          setPagination({ criteriaKey, page: lastPage });

          return;
        }
        setResult({ key: requestKey, status: "ready", resources, totalCount });
      })
      .catch(() => {
        if (active) setResult({ key: requestKey, status: "error", resources: [], totalCount: 0 });
      });

    return () => {
      active = false;
      controller.abort();
    };
  }, [requestKey]);

  return (
    <Modal
      defaultVisible
      footer={{ actions: ["cancel"], cancelProps: { children: t<string>("common.action.close") } }}
      size="3xl"
      title={
        <div className="flex flex-wrap items-center gap-2">
          <span>{t<string>("resourceProfile.preview.title")}</span>
          {isDraft && (
            <Chip color="warning" size="sm" variant="flat">
              {t<string>("resourceProfile.preview.draft")}
            </Chip>
          )}
        </div>
      }
      onDestroyed={onDestroyed}
    >
      <p className="break-words text-sm font-medium">{profile.name}</p>
      <p className="text-xs leading-5 text-default-500">
        {t<string>("resourceProfile.preview.description")}
      </p>
      {isDraft && (
        <p className="rounded-lg bg-warning/5 px-3 py-2 text-xs leading-5 text-warning-700">
          {t<string>("resourceProfile.preview.draftHint")}
        </p>
      )}
      <div className="sticky top-0 z-10 flex flex-wrap items-center justify-between gap-3 bg-content1 py-3">
        <div className="text-sm text-default-500">
          {t<string>("resourceProfile.label.totalMatchingResources")}{" "}
          <strong className="ml-1 font-semibold tabular-nums text-foreground">
            {current?.status === "ready" ? current.totalCount.toLocaleString() : "—"}
          </strong>
        </div>
        {current?.status === "ready" && current.totalCount > 25 && (
          <Pagination
            showControls
            aria-label={t<string>("resourceProfile.preview.pagination")}
            page={page}
            size="sm"
            total={Math.ceil(current.totalCount / 25)}
            onChange={(next) => setPagination({ criteriaKey, page: next })}
          />
        )}
      </div>
      {loading ? (
        <div className="flex min-h-48 items-center justify-center" role="status">
          <Spinner label={t<string>("resourceProfile.status.testingCriteria")} size="sm" />
        </div>
      ) : current.status === "error" ? (
        <div className="flex min-h-48 flex-col items-center justify-center gap-3" role="alert">
          <p className="text-sm text-default-500">
            {t<string>("resourceProfile.preview.loadFailed")}
          </p>
          <Button size="sm" startContent={<AiOutlineReload />} variant="flat" onPress={retry}>
            {t<string>("resourceProfile.preview.retry")}
          </Button>
        </div>
      ) : current.resources.length === 0 ? (
        <div className="flex min-h-48 flex-col items-center justify-center gap-3 text-default-400">
          <AiOutlineFile aria-hidden className="text-3xl" />
          <p className="text-sm">
            {t<string>("resourceProfile.empty.noResourcesMatchedByCriteria")}
          </p>
        </div>
      ) : (
        <div className="flex flex-col gap-1 pb-2">
          {current.resources.map((resource) => {
            const nameProperty =
              resource.properties?.[PropertyPool.Reserved]?.[ReservedProperty.Name];
            const nameValue = resolveScopedValue(
              nameProperty?.values,
              valueScopePriority,
              resource.scopePreferences?.find(
                (preference) =>
                  preference.propertyPool === PropertyPool.Reserved &&
                  preference.propertyId === ReservedProperty.Name,
              ),
              nameProperty?.profileScopePriority,
            );
            const scopedName = nameValue?.aliasAppliedBizValue ?? nameValue?.bizValue;
            const name =
              resource.displayName ||
              (typeof scopedName === "string" && scopedName.trim() ? scopedName : undefined) ||
              resource.path?.replace(/\\/g, "/").split("/").filter(Boolean).at(-1) ||
              t<string>("resourceProfile.preview.unnamed", { id: resource.id });

            return (
              <Button
                key={resource.id}
                aria-label={t<string>("resourceProfile.preview.openResource", { name })}
                className="h-auto min-h-14 w-full justify-start gap-3 px-3 py-2 text-left"
                variant="light"
                onPress={() =>
                  createPortal(ResourceDetailModal, { id: resource.id, onDestroyed: retry })
                }
              >
                <AiOutlineFile aria-hidden className="shrink-0 text-lg text-default-400" />
                <span className="flex min-w-0 flex-1 flex-col gap-1">
                  <span className="truncate text-sm font-medium" title={name}>
                    {name}
                  </span>
                  <span
                    className="truncate text-xs font-normal text-default-400"
                    title={resource.path}
                  >
                    {resource.path || t<string>("resourceProfile.preview.noLocalFile")}
                  </span>
                </span>
                <span className="shrink-0 text-xs font-normal tabular-nums text-default-400">
                  #{resource.id}
                </span>
                <AiOutlineArrowRight aria-hidden className="shrink-0 text-default-400" />
              </Button>
            );
          })}
        </div>
      )}
    </Modal>
  );
}
