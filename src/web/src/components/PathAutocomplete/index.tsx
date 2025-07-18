"use client";

import React, { useCallback, useEffect, useState } from "react";
import { useDebounce } from "react-use";
import { Autocomplete, AutocompleteItem } from "@/components/bakaui";
import type { AutocompleteProps } from "@heroui/react";
import BApi from "@/sdk/BApi";
import {
  FolderOutlined,
  FileOutlined,
  DesktopOutlined,
  HddOutlined,
  UsbOutlined
} from "@ant-design/icons";

export type PathType = "file" | "folder" | "both";

export interface PathAutocompleteProps extends Omit<AutocompleteProps<{ path: string, name: string, isDirectory: boolean }>, 'items' | 'onInputChange' | 'onSelectionChange' | 'onChange' | 'children'> {
  value: string;
  onChange: (value: string) => void;
  onSelectionChange?: (value: string) => void;
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

  const searchPaths = useCallback((prefix: string) => {
    setIsLoading(true);

    if (prefix && prefix.length >= 1) {
      // Search for paths with the prefix
      BApi.file.searchFileSystemEntries({
        prefix: prefix,
        maxResults: maxResults,
      }).then((response) => {
        if (response.data) {
          // Filter based on pathType
          let filteredItems = response.data;
          if (pathType === "folder") {
            filteredItems = response.data.filter(item => item.isDirectory);
          }
          setAutocompleteItems(filteredItems);
        }
      }).finally(() => {
        setIsLoading(false);
      });
    } else if (prefix.length === 0) {
      // Show drives when input is empty
      BApi.file.searchFileSystemEntries({
        maxResults: maxResults,
      }).then((response) => {
        if (response.data) {
          // Filter based on pathType
          let filteredItems = response.data;
          if (pathType === "folder") {
            filteredItems = response.data.filter(item => item.isDirectory);
          }
          setAutocompleteItems(filteredItems);
        }
      }).finally(() => {
        setIsLoading(false);
      });
    } else {
      setAutocompleteItems([]);
      setIsLoading(false);
    }
  }, [pathType, maxResults]);

  // Use react-use's useDebounce
  const [, cancel] = useDebounce(
    () => {
      searchPaths(value);
    },
    debounceDelay,
    [value]
  );

  // Load drives when component mounts
  useEffect(() => {
    BApi.file.searchFileSystemEntries({
      maxResults: maxResults,
    }).then((response) => {
      if (response.data) {
        // Filter based on pathType
        let filteredItems = response.data;
        if (pathType === "file") {
          filteredItems = response.data.filter(item => !item.isDirectory);
        } else if (pathType === "folder") {
          filteredItems = response.data.filter(item => item.isDirectory);
        }
        setAutocompleteItems(filteredItems);
      }
    });
  }, [pathType, maxResults]);

  const handleInputChange = (inputValue: string) => {
    onChange(inputValue);
  };

  const handleSelectionChange = (key: React.Key | null) => {
    if (key) {
      const selectedPath = key as string;
      onChange(selectedPath);
      onSelectionChange?.(selectedPath);
    }
  };

  return (
    <Autocomplete
      {...autocompleteProps}
      inputValue={value}
      isLoading={isLoading}
      onInputChange={handleInputChange}
      onSelectionChange={handleSelectionChange}
      items={autocompleteItems}
      allowsCustomValue={true}
    >
      {(item) => (
        <AutocompleteItem key={item.path} title={item.path} startContent={getFileIcon(item)}>
        </AutocompleteItem>
      )}
    </Autocomplete>
  );
} 