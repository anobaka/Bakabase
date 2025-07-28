"use client";

import React from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineQuestionCircle } from "react-icons/ai";

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
import { useAppContextStore } from "@/stores/appContext";
import ExternalLink from "@/components/ExternalLink";
const Development = () => {
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
                  {c.tip ? (
                    <Tooltip
                      color={"secondary"}
                      content={t<string>(c.tip)}
                      placement={"top"}
                    >
                      <div className={"flex items-center gap-1"}>
                        {t<string>(c.label)}
                        <AiOutlineQuestionCircle className={"text-base"} />
                      </div>
                    </Tooltip>
                  ) : (
                    t<string>(c.label)
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

Development.displayName = "Development";

export default Development;
