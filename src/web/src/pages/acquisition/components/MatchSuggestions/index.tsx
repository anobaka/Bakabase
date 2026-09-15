"use client";

import type { components } from "@/sdk/BApi2";

import React, { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineArrowRight } from "react-icons/ai";

import BApi from "@/sdk/BApi";
import { Button, Chip, Spinner, toast } from "@/components/bakaui";

type Suggestion =
  components["schemas"]["Bakabase.Abstractions.Models.Domain.ResourceMatchSuggestion"];

type Props = {
  onChanged?: () => void;
};

/**
 * The pairs of resources that might be one work.
 *
 * Nothing here has been decided: bringing in a list from outside creates a resource for everything
 * on it, and some of those are already tracked under a slightly different name. Merging on a score
 * would silently take a resource's files and history, so the answer is a person's.
 */
const MatchSuggestions = ({ onChanged }: Props) => {
  const { t } = useTranslation();

  const [suggestions, setSuggestions] = useState<Suggestion[]>([]);
  const [loading, setLoading] = useState(true);
  const [deciding, setDeciding] = useState<number>();

  const load = useCallback(async () => {
    try {
      const rsp = await BApi.resource.getPendingResourceMatchSuggestions();

      setSuggestions((rsp.data ?? []) as Suggestion[]);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const decide = async (id: number, same: boolean) => {
    setDeciding(id);
    try {
      const rsp = same
        ? await BApi.resource.confirmResourceMatchSuggestion(id)
        : await BApi.resource.dismissResourceMatchSuggestion(id);

      if (rsp.code) {
        toast.danger(rsp.message ?? "");

        return;
      }

      if (same) toast.success(t<string>("acquisition.matches.merged"));

      await load();
      onChanged?.();
    } finally {
      setDeciding(undefined);
    }
  };

  const name = (value: string | null | undefined, id: number) =>
    value && value.length > 0 ? value : t<string>("acquisition.unnamed", { id });

  if (loading) {
    return (
      <div className="flex justify-center py-10">
        <Spinner size="lg" />
      </div>
    );
  }

  if (suggestions.length === 0) {
    return (
      <div className="text-center text-default-500 py-10">
        {t<string>("acquisition.matches.empty")}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-2">
      <div className="text-sm text-default-500">{t<string>("acquisition.matches.description")}</div>

      {suggestions.map((suggestion) => (
        <div
          key={suggestion.id}
          className="flex items-center gap-3 rounded-medium border border-default-200 px-3 py-2"
        >
          <div className="flex min-w-0 flex-1 items-center gap-2">
            <span className="truncate" title={suggestion.resourceName ?? undefined}>
              {name(suggestion.resourceName, suggestion.resourceId)}
            </span>
            <AiOutlineArrowRight className="shrink-0 text-default-400" />
            <span className="truncate" title={suggestion.candidateResourceName ?? undefined}>
              {name(suggestion.candidateResourceName, suggestion.candidateResourceId)}
            </span>
          </div>

          <Chip size="sm" variant="flat">
            {Math.round(suggestion.score * 100)}%
          </Chip>

          <Button
            color="primary"
            isLoading={deciding === suggestion.id}
            size="sm"
            onPress={() => decide(suggestion.id, true)}
          >
            {t<string>("acquisition.matches.same")}
          </Button>
          <Button
            isLoading={deciding === suggestion.id}
            size="sm"
            variant="light"
            onPress={() => decide(suggestion.id, false)}
          >
            {t<string>("acquisition.matches.different")}
          </Button>
        </div>
      ))}
    </div>
  );
};

MatchSuggestions.displayName = "MatchSuggestions";

export default MatchSuggestions;
