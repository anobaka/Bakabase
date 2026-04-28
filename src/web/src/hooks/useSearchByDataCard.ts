"use client";

import { useCallback, useMemo } from "react";
import { useTranslation } from "react-i18next";
import { useLocation, useNavigate } from "react-router-dom";

import type { IProperty } from "@/components/Property/models";

import { Modal } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { usePendingSearchStore } from "@/stores/pendingSearch";
import {
  PropertyPool,
  SearchCombinator,
  SearchOperation,
} from "@/sdk/constants";

// DataCardMatchMode is not exported from constants — declared inline to keep
// the mapping explicit. Backend enum: 1 = Any, 2 = All.
const MATCH_MODE_ANY = 1;
const MATCH_MODE_ALL = 2;

export interface DataCardLikeValue {
  propertyId: number;
  value?: string;
}

export interface DataCardLike {
  propertyValues?: DataCardLikeValue[];
}

export interface DataCardTypeLike {
  matchRules?: {
    matchProperties?: number[];
    matchMode?: number;
  };
}

interface TriggerOptions {
  /** Show a confirmation dialog before navigating. */
  confirm?: boolean;
  /** Called after the user confirms (or immediately when confirm is false), right before navigation. */
  beforeNavigate?: () => void;
}

const PAGE_SIZE = 50;

export function useSearchByDataCard() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const location = useLocation();
  const { createPortal } = useBakabaseContext();
  const setPendingSearch = usePendingSearchStore((s) => s.setPendingSearch);

  const buildSearchForm = useCallback(
    (card: DataCardLike, cardType: DataCardTypeLike, allProperties: IProperty[]) => {
      const matchPropIds = cardType.matchRules?.matchProperties ?? [];
      if (matchPropIds.length === 0) return null;

      const matchMode = cardType.matchRules?.matchMode ?? MATCH_MODE_ALL;

      const nonNull = (card.propertyValues ?? []).filter(
        (pv) =>
          matchPropIds.includes(pv.propertyId) &&
          pv.value != null &&
          pv.value !== "",
      );

      if (nonNull.length === 0) return null;

      // All-mode requires every match property to have a non-null card value;
      // otherwise the auto-association would yield zero matches and the search
      // should not happen.
      if (matchMode === MATCH_MODE_ALL && nonNull.length < matchPropIds.length) {
        return null;
      }

      const propertyById = new Map(allProperties.map((p) => [p.id, p]));

      const filters = nonNull.map((pv) => ({
        propertyId: pv.propertyId,
        propertyPool: propertyById.get(pv.propertyId)?.pool ?? PropertyPool.Custom,
        operation: SearchOperation.Equals,
        dbValue: pv.value,
        disabled: false,
      }));

      return {
        group: {
          combinator:
            matchMode === MATCH_MODE_ANY
              ? SearchCombinator.Or
              : SearchCombinator.And,
          filters,
          disabled: false,
        },
        page: 1,
        pageSize: PAGE_SIZE,
      };
    },
    [],
  );

  const canSearch = useCallback(
    (card: DataCardLike, cardType: DataCardTypeLike, allProperties: IProperty[]) =>
      buildSearchForm(card, cardType, allProperties) !== null,
    [buildSearchForm],
  );

  const triggerSearch = useCallback(
    (
      card: DataCardLike,
      cardType: DataCardTypeLike,
      allProperties: IProperty[],
      opts?: TriggerOptions,
    ) => {
      const form = buildSearchForm(card, cardType, allProperties);
      if (!form) return;

      const doSearch = () => {
        opts?.beforeNavigate?.();
        // @ts-ignore — store is typed against the DB shape, but pendingSearch
        // accepts the in-memory filter shape (dbValue/disabled), matching how
        // ChildrenModal already populates it.
        setPendingSearch(form);
        if (location.pathname !== "/resource") {
          navigate("/resource");
        }
      };

      if (opts?.confirm) {
        createPortal(Modal, {
          defaultVisible: true,
          title: t("dataCard.search.confirm.title"),
          children: t("dataCard.search.confirm.content"),
          onOk: doSearch,
        });
      } else {
        doSearch();
      }
    },
    [buildSearchForm, createPortal, location.pathname, navigate, setPendingSearch, t],
  );

  return useMemo(() => ({ canSearch, triggerSearch }), [canSearch, triggerSearch]);
}
