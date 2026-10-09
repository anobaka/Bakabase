"use client";

import type { AutocompleteProps } from "@heroui/react";

import React, { useCallback, useRef, useState } from "react";
import { useDebounce, useUpdateEffect } from "react-use";
import { FolderOutlined, FileOutlined } from "@ant-design/icons";
import { useTranslation } from "react-i18next";

import { Autocomplete, AutocompleteItem } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { storageError, validateUserStoragePaths } from "@/stores/userStorage";

export type PathType = "file" | "folder" | "both";

export interface PathAutocompleteProps
  extends Omit<
    AutocompleteProps<{ path: string; name: string; isDirectory: boolean }>,
    "items" | "onInputChange" | "onSelectionChange" | "onChange" | "children"
  > {
  value?: string;
  defaultValue?: string;
  onChange?: (value: string, type?: "file" | "folder") => void;
  onSelectionChange?: (value: string, type: "file" | "folder") => void;
  pathType?: PathType;
  maxResults?: number;
  debounceDelay?: number;
}

interface PathItem {
  path: string;
  name: string;
  isDirectory: boolean;
}

// 获取文件类型图标的函数
const getFileIcon = (item: PathItem) => {
  if (item.isDirectory) {
    return <FolderOutlined className="text-lg" />;
  } else {
    return <FileOutlined className="text-lg" />;
  }
};

export default function PathAutocomplete({
  value: propsValue,
  defaultValue,
  onChange,
  onSelectionChange,
  pathType = "folder",
  maxResults = 10,
  debounceDelay = 300,
  ...autocompleteProps
}: PathAutocompleteProps) {
  const { t } = useTranslation();
  const [autocompleteItems, setAutocompleteItems] = useState<PathItem[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [value, setValue] = useState(propsValue ?? defaultValue);
  const valueRef = useRef(value);
  const isFirstRender = useRef(true);
  const searchGeneration = useRef(0);
  const [error, setError] = useState<string>();

  const searchPaths = useCallback(
    (prefix?: string) => {
      const generation = ++searchGeneration.current;

      setIsLoading(true);
      setError(undefined);

      const reqPrefix = prefix && prefix.length >= 1 ? prefix : undefined;

      BApi.file
        .searchFileSystemEntries(
          {
            prefix: reqPrefix,
            maxResults: maxResults,
          },
          { showErrorToast: false },
        )
        .then((response) => {
          if (generation !== searchGeneration.current) return;
          if (response.code) throw storageError(response);
          if (response.data) {
            // Filter based on pathType
            let filteredItems = response.data;

            if (pathType === "folder") {
              filteredItems = response.data.filter((item) => item.isDirectory);
            }
            setAutocompleteItems(filteredItems);
          }
        })
        .catch((cause) => {
          if (generation !== searchGeneration.current) return;
          setAutocompleteItems([]);
          setError(storageError(cause, t).message || t("fileExplorer.storage.loadFailed"));
        })
        .finally(() => {
          if (generation === searchGeneration.current) setIsLoading(false);
        });
    },
    [pathType, maxResults, t],
  );

  // Use react-use's useDebounce (skip on first render)
  useDebounce(
    () => {
      if (isFirstRender.current) {
        isFirstRender.current = false;

        return;
      }
      searchPaths(value);
    },
    debounceDelay,
    [value],
  );

  useUpdateEffect(() => {
    valueRef.current = value;
  }, [value]);

  useUpdateEffect(() => {
    setValue(propsValue);
  }, [propsValue]);

  // Load drives when component mounts
  useUpdateEffect(() => {
    searchPaths(valueRef.current);
  }, [pathType, maxResults]);

  const handleInputChange = (inputValue: string) => {
    const item = autocompleteItems.find((it) => it.path === inputValue);

    // console.log(autocompleteItems, item, inputValue);

    setValue(inputValue);
    onChange?.(inputValue, item ? (item.isDirectory ? "folder" : "file") : undefined);
  };

  const handleSelectionChange = async (key: React.Key | null) => {
    if (key) {
      const selectedPath = key as string;

      const item = autocompleteItems.find((it) => it.path === selectedPath);

      if (!item) return;
      const type = item.isDirectory ? "folder" : "file";

      try {
        await validateUserStoragePaths([selectedPath]);
        setValue(selectedPath);
        onChange?.(selectedPath, type);
        onSelectionChange?.(selectedPath, type);
      } catch (cause) {
        setError(storageError(cause, t).message || t("fileExplorer.storage.pathRejected"));
      }
    }
  };

  return (
    <Autocomplete
      {...autocompleteProps}
      allowsCustomValue={true}
      errorMessage={error || autocompleteProps.errorMessage}
      inputValue={value}
      isInvalid={!!error || autocompleteProps.isInvalid}
      isLoading={isLoading}
      items={autocompleteItems}
      onInputChange={handleInputChange}
      onOpenChange={(isOpen) => {
        if (isOpen && autocompleteItems.length == 0) {
          searchPaths(value);
        }
      }}
      onSelectionChange={handleSelectionChange}
    >
      {(item) => (
        <AutocompleteItem key={item.path} startContent={getFileIcon(item)} title={item.path} />
      )}
    </Autocomplete>
  );
}
