"use client";

import type { AutocompleteProps } from "@heroui/react";

import React, { useCallback, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { useDebounce } from "react-use";

import { Autocomplete, AutocompleteItem } from "@/components/bakaui";
import { useResourceApi, useResourceOwner } from "@/features/management/resourceContext";
import { useResourceSources } from "@/features/management/resourceSourcesContext";
import { createResourceApi } from "@/features/management/resourceApi";

export interface ResourceKeywordAutocompleteProps
  extends Omit<
    AutocompleteProps<{ label: string; value: string }>,
    "items" | "onInputChange" | "onSelectionChange" | "onChange" | "children"
  > {
  value?: string;
  onValueChange?: (value: string) => void;
  onSelectionChange?: (value: string) => void;
  maxResults?: number;
  debounceDelay?: number;
}

export default function ResourceKeywordAutocomplete({
  value,
  onValueChange,
  onSelectionChange,
  maxResults = 10,
  debounceDelay = 300,
  placeholder,
  ...autocompleteProps
}: ResourceKeywordAutocompleteProps) {
  const { t } = useTranslation();
  const api = useResourceApi();
  const owner = useResourceOwner();
  const sources = useResourceSources()?.sources;
  const [isLoading, setIsLoading] = useState(false);
  const [candidates, setCandidates] = useState<string[]>([]);
  const lastSearchedRef = useRef<string | undefined>(undefined);
  const sequence = useRef(0);

  const searchKeywords = useCallback(
    async (prefix: string) => {
      const current = ++sequence.current;
      setIsLoading(true);

      if (prefix && prefix.length > 0) {
        try {
          const clients =
            !owner && sources?.some((source) => !source.isLocal)
              ? sources
                  .filter((source) => source.available)
                  .map((source) => createResourceApi(source))
              : [api];
          const suggestions = new Set<string>();

          for (let offset = 0; offset < clients.length; offset += 4) {
            const results = await Promise.allSettled(
              clients
                .slice(offset, offset + 4)
                .map((client) =>
                  client.resource.getResourceSearchKeywordRecommendation({
                    keyword: prefix,
                    maxCount: maxResults,
                  }),
                ),
            );

            if (current !== sequence.current) return;
            for (const result of results)
              if (result.status === "fulfilled")
                for (const value of result.value.data ?? []) suggestions.add(value);
          }
          if (current === sequence.current) setCandidates([...suggestions].slice(0, maxResults));
        } finally {
          if (current === sequence.current) setIsLoading(false);
        }
      } else {
        setCandidates([]);
        setIsLoading(false);
      }
    },
    [maxResults, api, owner, sources],
  );

  useDebounce(
    () => {
      if (lastSearchedRef.current !== value) {
        lastSearchedRef.current = value;
        searchKeywords(value ?? "");
      }
    },
    debounceDelay,
    [value],
  );

  const handleSelectionChange = (key: React.Key | null) => {
    if (key) {
      const selected = key as string;

      onValueChange?.(selected);
      onSelectionChange?.(selected);
    }
  };

  console.log(autocompleteProps);

  return (
    <Autocomplete
      aria-label={"Resource Keyword"}
      placeholder={placeholder ?? t<string>("resource.placeholder.searchAnyKeyword")}
      {...autocompleteProps}
      allowsCustomValue={true}
      inputValue={value ?? ""}
      isLoading={isLoading}
      items={candidates.map((k) => ({ label: k, value: k }))}
      onInputChange={(inputValue: string) => {
        onValueChange?.(inputValue);
      }}
      onOpenChange={(isOpen) => {
        if (isOpen && candidates.length === 0) {
          searchKeywords(value ?? "");
        }
      }}
      onSelectionChange={handleSelectionChange}
    >
      {(item) => (
        <AutocompleteItem key={item.value} title={item.label}>
          {item.label}
        </AutocompleteItem>
      )}
    </Autocomplete>
  );
}
