"use client";

import type { PropertyValueScope } from "@/sdk/constants";

import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Popover, PopoverContent, PopoverTrigger } from "@heroui/react";
import { AiOutlineDown, AiOutlinePlus, AiOutlineUndo } from "react-icons/ai";

import ScopePriorityList, { scopeLabel } from "./ScopePriorityList";

import { Button } from "@/components/bakaui";

type Props = {
  value: PropertyValueScope[] | null;
  availableScopes: PropertyValueScope[];
  onChange: (value: PropertyValueScope[] | null) => void;
  isDisabled?: boolean;
};

export default function ScopePriorityEditor({
  value,
  availableScopes,
  onChange,
  isDisabled,
}: Props) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const isCustom = value !== null;
  const scopes = value ?? availableScopes;
  const missing = availableScopes.filter((scope) => !scopes.includes(scope));
  const summary = scopes.map((scope) => scopeLabel(scope, t)).join(" → ");

  return (
    <div className="flex min-w-0 flex-wrap items-center gap-2">
      <div className="min-w-0 flex-1">
        <div className={`text-xs ${isCustom ? "text-secondary" : "text-default-500"}`}>
          {t<string>(
            isCustom
              ? "resourceProfile.scopePriority.customLabel"
              : "resourceProfile.scopePriority.useGlobal",
          )}
        </div>
        {isCustom && (
          <div className="mt-1 max-w-full truncate text-xs text-default-500" title={summary}>
            {summary || t<string>("resourceProfile.scopePriority.empty")}
          </div>
        )}
      </div>
      <Popover
        isOpen={open}
        placement="bottom-end"
        onOpenChange={(next) => {
          if (!isDisabled) setOpen(next);
        }}
      >
        <PopoverTrigger>
          <Button
            endContent={<AiOutlineDown className="text-xs" />}
            isDisabled={isDisabled}
            size="sm"
            variant="light"
          >
            {t<string>(
              isCustom
                ? "resourceProfile.scopePriority.edit"
                : "resourceProfile.scopePriority.customize",
            )}
          </Button>
        </PopoverTrigger>
        <PopoverContent className="w-80 max-w-[calc(100vw-2rem)] items-stretch p-4">
          <div className="mb-3 text-sm font-medium">
            {t<string>("resourceProfile.propertyPool.columnScopePriority")}
          </div>
          <p className="mb-3 text-xs leading-5 text-default-500">
            {t<string>("resourceProfile.scopePriority.orderHint")}
          </p>
          <div className="max-h-64 overflow-y-auto">
            <ScopePriorityList isDisabled={isDisabled} scopes={scopes} onChange={onChange} />
          </div>
          {!isCustom && (
            <Button
              className="mt-3"
              color="primary"
              isDisabled={isDisabled}
              size="sm"
              variant="flat"
              onPress={() => onChange([...availableScopes])}
            >
              {t<string>("resourceProfile.scopePriority.useCustomOrder")}
            </Button>
          )}
          {isCustom && missing.length > 0 && (
            <div className="mt-3 flex flex-wrap gap-1">
              {missing.map((scope) => (
                <Button
                  key={scope}
                  isDisabled={isDisabled}
                  size="sm"
                  startContent={<AiOutlinePlus />}
                  variant="light"
                  onPress={() => onChange([...scopes, scope])}
                >
                  {scopeLabel(scope, t)}
                </Button>
              ))}
            </div>
          )}
          {isCustom && (
            <Button
              className="mt-3 justify-start"
              isDisabled={isDisabled}
              size="sm"
              startContent={<AiOutlineUndo />}
              variant="light"
              onPress={() => {
                onChange(null);
                setOpen(false);
              }}
            >
              {t<string>("resourceProfile.scopePriority.resetTooltip")}
            </Button>
          )}
        </PopoverContent>
      </Popover>
    </div>
  );
}
