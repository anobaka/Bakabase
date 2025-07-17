"use client";

import React from "react";
import { useTranslation } from "react-i18next";
import { QuestionCircleOutlined } from "@ant-design/icons";

import {
  Snippet,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  Tooltip,
} from "@/components/bakaui";
import { useAppContextStore } from "@/models/appContext";
import ExternalLink from "@/components/ExternalLink";

export default () => {
  const { t } = useTranslation();
  const appContext = useAppContextStore((state) => state);

  const apiDocumentUrl = appContext.apiEndpoint
    ? `${appContext.apiEndpoint}/swagger`
    : undefined;

  const items = [
    {
      label: "API endpoints",
      tip: (
        <div>
          {t<string>("Listening addresses:")}
          {appContext.listeningAddresses.map((addr) => {
            return <div>{addr}</div>;
          })}
        </div>
      ),
      value: appContext.apiEndpoints && (
        <div className={"flex flex-wrap gap-1 items-center"}>
          {appContext.apiEndpoints?.map((x) => (
            <Snippet size={"sm"} symbol={<>&nbsp;</>} variant={"flat"}>
              {x}
            </Snippet>
          ))}
        </div>
      ),
    },
    {
      label: "API document",
      value: apiDocumentUrl && (
        <ExternalLink href={apiDocumentUrl}>{apiDocumentUrl}</ExternalLink>
      ),
    },
  ];

  return (
    <Table isCompact removeWrapper>
      <TableHeader>
        <TableColumn width={200}>{t<string>("Development")}</TableColumn>
        <TableColumn>&nbsp;</TableColumn>
      </TableHeader>
      <TableBody>
        {items.map((c, i) => {
          return (
            <TableRow
              key={i}
              className={"hover:bg-[var(--bakaui-overlap-background)]"}
            >
              <TableCell>
                <div className={"flex items-center gap-1"}>
                  {t<string>(c.label)}
                  {c.tip && (
                    <Tooltip content={c.tip}>
                      <QuestionCircleOutlined className={"text-base"} />
                    </Tooltip>
                  )}
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
