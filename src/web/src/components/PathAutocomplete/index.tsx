"use client";

import type { AutocompleteProps } from "@heroui/react";

import React, { useCallback, useRef, useState } from "react";
import { useDebounce, useUpdateEffect } from "react-use";
import { FolderOutlined, FileOutlined } from "@ant-design/icons";

import { Autocomplete, AutocompleteItem } from "@/components/bakaui";
import BApi from "@/sdk/BApi";

export type PathType = "file" | "folder" | "both";

export interface PathAutocompleteProps
  extends Omit<
    AutocompleteProps<{ path: string; name: string; isDirectory: boolean }>,
    "items" | "onInputChange" | "onSelectionChange" | "onChange" | "children"
  > {
  value: string;
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
  value,
  onChange,
  onSelectionChange,
  pathType = "folder",
  maxResults = 10,
  debounceDelay = 300,
  ...autocompleteProps
}: PathAutocompleteProps) {
  const [autocompleteItems, setAutocompleteItems] = useState<PathItem[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const valueRef = useRef(value);

  const searchPaths = useCallback(
    (prefix: string) => {
      setIsLoading(true);

      if (prefix && prefix.length >= 1) {
        // Search for paths with the prefix
        BApi.file
          .searchFileSystemEntries({
            prefix: prefix,
            maxResults: maxResults,
          })
          .then((response) => {
            if (response.data) {
              // Filter based on pathType
              let filteredItems = response.data;

              if (pathType === "folder") {
                filteredItems = response.data.filter(
                  (item) => item.isDirectory,
                );
              }
              setAutocompleteItems(filteredItems);
            }
          })
          .finally(() => {
            setIsLoading(false);
          });
      } else if (prefix.length === 0) {
        // Show drives when input is empty
        BApi.file
          .searchFileSystemEntries({
            maxResults: maxResults,
          })
          .then((response) => {
            if (response.data) {
              // Filter based on pathType
              let filteredItems = response.data;

              if (pathType === "folder") {
                filteredItems = response.data.filter(
                  (item) => item.isDirectory,
                );
              }
              setAutocompleteItems(filteredItems);
            }
          })
          .finally(() => {
            setIsLoading(false);
          });
      } else {
        setAutocompleteItems([]);
        setIsLoading(false);
      }
    },
    [pathType, maxResults],
  );

  // Use react-use's useDebounce
  const [,] = useDebounce(
    () => {
      if (valueRef.current != value) {
        console.log("123");
        searchPaths(value);
      }
    },
    debounceDelay,
    [value],
  );

  useUpdateEffect(() => {
    valueRef.current = value;
  }, [value]);

  // Load drives when component mounts
  useUpdateEffect(() => {
    BApi.file
      .searchFileSystemEntries({
        maxResults: maxResults,
      })
      .then((response) => {
        if (response.data) {
          // Filter based on pathType
          let filteredItems = response.data;

          if (pathType === "file") {
            filteredItems = response.data.filter((item) => !item.isDirectory);
          } else if (pathType === "folder") {
            filteredItems = response.data.filter((item) => item.isDirectory);
          }
          setAutocompleteItems(filteredItems);
        }
      });
  }, [pathType, maxResults]);

  const handleInputChange = (inputValue: string) => {
    const item = autocompleteItems.find((it) => it.path === inputValue);

    // console.log(autocompleteItems, item, inputValue);

    onChange?.(
      inputValue,
      item ? (item.isDirectory ? "folder" : "file") : undefined,
    );
  };

  const handleSelectionChange = (key: React.Key | null) => {
    if (key) {
      const selectedPath = key as string;

      const item = autocompleteItems.find((it) => it.path === selectedPath)!;
      const type = item.isDirectory ? "folder" : "file";

      onChange?.(selectedPath, type);
      onSelectionChange?.(selectedPath, type);
    }
  };

  return (
    <Autocomplete
      {...autocompleteProps}
      allowsCustomValue={true}
      inputValue={value}
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
        <AutocompleteItem
          key={item.path}
          startContent={getFileIcon(item)}
          title={item.path}
        />
      )}
    </Autocomplete>
  );
}
