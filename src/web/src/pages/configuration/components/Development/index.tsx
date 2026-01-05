"use client";

import type { ChipProps, InputProps, NumberInputProps } from "@/components/bakaui";

import React from "react";
import { useTranslation } from "react-i18next";
import toast from "react-hot-toast";
import { AiOutlineNumber } from "react-icons/ai";

import {
  Chip,
  Input,
  NumberInput,
  Snippet,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
} from "@/components/bakaui";
import { useAppContextStore } from "@/stores/appContext";
import { useAppOptionsStore } from "@/stores/options";
import { RuntimeMode } from "@/sdk/constants";
import ExternalLink from "@/components/ExternalLink";
import { EditableValue } from "@/components/EditableValue";
import BApi from "@/sdk/BApi";

const Development: React.FC = () => {
  const { t } = useTranslation();
  const appContext = useAppContextStore((state) => state);
  const appOptions = useAppOptionsStore((state) => state.data);

  const apiDocumentUrl = appContext.apiEndpoint
    ? `${appContext.apiEndpoint}/swagger`
    : undefined;

  const parsePorts = (text?: string): number[] => {
    const raw = (text ?? "")
      .split(/[，,\s]+/)
      .map((s) => s.trim())
      .filter((s) => s.length > 0);
    const nums = raw
      .map((s) => Number.parseInt(s, 10))
      .filter((n) => Number.isFinite(n) && n > 0 && n <= 65535);
    const seen = new Set<number>();
    const unique: number[] = [];
    for (const n of nums) {
      if (!seen.has(n)) {
        seen.add(n);
        unique.push(n);
      }
    }
    return unique;
  };

  const renderListeningPortCount = () => {
    const min = 0;
    const max = 3;
    return (
      <EditableValue<number, NumberInputProps, ChipProps & { value: number }>
        Editor={(props) => (
          <NumberInput
            isClearable
            className="max-w-[320px]"
            description={
              <div>
                <div>
                  {t("configuration.development.tip.currentListeningPortCount", {
                    port: appOptions.autoListeningPortCount === 0
                      ? t("configuration.development.auto")
                      : appOptions.autoListeningPortCount,
                  })}
                </div>
                <div>
                  {t("configuration.development.tip.configurablePortRange", { min, max })}
                </div>
                <div>{t("configuration.development.tip.changesAfterRestart")}</div>
                <div>
                  {t("configuration.development.tip.portWarning")}
                </div>
              </div>
            }
            formatOptions={{ useGrouping: false }}
            max={max}
            min={min}
            placeholder={t("configuration.development.count")}
            {...props}
          />
        )}
        Viewer={({ value, ...props }) =>
          value !== undefined ? (
            <Chip
              radius="sm"
              startContent={<AiOutlineNumber className="text-base" />}
              variant="flat"
              {...props}
            >
              {value === 0 ? t("configuration.development.auto") : value}
            </Chip>
          ) : null
        }
        value={appOptions.autoListeningPortCount}
        onSubmit={async (v) => {
          await BApi.options.patchAppOptions({ autoListeningPortCount: v });
          toast.success(t("common.success.saved"));
        }}
      />
    );
  };

  const renderListeningPorts = () => {
    const toText = (ports?: number[]) => (ports?.length ? ports.join(", ") : "");
    const max = 6;
    return (
      <EditableValue<string, InputProps>
        Editor={(props) => (
          <Input
            isClearable
            className="max-w-[420px]"
            description={
              <div>
                <div>{t("configuration.development.tip.portsInput")}</div>
                <div>{t("configuration.development.tip.changesAfterRestart")}</div>
                <div>
                  {t("configuration.development.tip.portWarning")}
                </div>
              </div>
            }
            placeholder={t("configuration.development.input.portsPlaceholder")}
            {...props}
          />
        )}
        Viewer={({ value }) => {
          const ports = parsePorts(value);
          if (ports && ports.length > 0) {
            return (
              <div className="flex flex-wrap gap-2">
                {ports.map((p) => (
                  <Chip
                    key={p}
                    radius="sm"
                    startContent={<AiOutlineNumber className="text-base" />}
                    variant="flat"
                  >
                    {p}
                  </Chip>
                ))}
              </div>
            );
          }
          return null;
        }}
        value={toText(appOptions.listeningPorts)}
        onSubmit={async (text) => {
          const ports = parsePorts(text);
          if (ports.length > max) {
            toast.error(t("configuration.development.error.tooManyPorts", { max }));
            throw new Error("invalid");
          }
          await BApi.options.patchAppOptions({ listeningPorts: ports });
          toast.success(t("common.success.saved"));
        }}
      />
    );
  };

  const items: { label: string; value: React.ReactNode }[] = [
    {
      label: "configuration.development.apiEndpoints",
      value: appContext.apiEndpoints && (
        <div className="flex flex-wrap gap-1 items-center">
          {appContext.apiEndpoints?.map((x, idx) => (
            <Snippet key={idx} size="sm" symbol={<>&nbsp;</>} variant="flat">
              {x}
            </Snippet>
          ))}
        </div>
      ),
    },
    {
      label: "configuration.development.apiDocument",
      value: apiDocumentUrl && (
        <ExternalLink href={apiDocumentUrl}>{apiDocumentUrl}</ExternalLink>
      ),
    },
  ];

  // Add port configurations for Dev and WinForms modes
  if (appContext.runtimeMode === RuntimeMode.Dev || appContext.runtimeMode === RuntimeMode.WinForms) {
    items.push(
      {
        label: "configuration.development.listeningPortCount",
        value: renderListeningPortCount(),
      },
      {
        label: "configuration.development.listeningPorts",
        value: renderListeningPorts(),
      },
    );
  }

  return (
    <Table isCompact removeWrapper>
      <TableHeader>
        <TableColumn width={200}>{t("configuration.development.title")}</TableColumn>
        <TableColumn>&nbsp;</TableColumn>
      </TableHeader>
      <TableBody>
        {items.map((c, i) => {
          return (
            <TableRow
              key={i}
              className="hover:bg-[var(--bakaui-overlap-background)]"
            >
              <TableCell>
                <div className="flex items-center gap-1">
                  {t(c.label)}
                </div>
              </TableCell>
              <TableCell>{c.value}</TableCell>
            </TableRow>
          );
        })}
      </TableBody>
    </Table>
  );
};

Development.displayName = "Development";

export default Development;
