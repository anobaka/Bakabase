"use client";

import type { AutocompleteProps } from "@heroui/react";

import React, { useCallback, useRef, useState } from "react";
import { useDebounce } from "react-use";

import { Autocomplete, AutocompleteItem } from "@/components/bakaui";
import BApi from "@/sdk/BApi";

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
  ...autocompleteProps
}: ResourceKeywordAutocompleteProps) {
  const [isLoading, setIsLoading] = useState(false);
  const [candidates, setCandidates] = useState<string[]>([]);
  const lastSearchedRef = useRef<string | undefined>(undefined);

  const searchKeywords = useCallback(
    (prefix: string) => {
      setIsLoading(true);

      if (prefix && prefix.length > 0) {
        BApi.resource
          .getResourceSearchKeywordRecommendation({
            keyword: prefix,
            maxCount: maxResults,
          })
          .then((ret) => {
            setCandidates(ret.data ?? []);
          })
          .finally(() => setIsLoading(false));
      } else {
        setCandidates([]);
        setIsLoading(false);
      }
    },
    [maxResults],
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

  return (
    <Autocomplete
      aria-label={"Resource Keyword"}
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
