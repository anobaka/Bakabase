"use client";

import React, { useEffect, useReducer, useState } from "react";
import { useTranslation } from "react-i18next";

import BApi from "@/sdk/BApi";
import {
  Pagination,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
} from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

type Form = {
  pageSize: 100;
  pageIndex: number;
  // resourceId?: number;
};

type PlayHistoryPage = {
  // resourceId: number;
  id: number;
  item?: string;
  playedAt?: string;
};
const PlayHistoryPage = () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [form, setForm] = useState<Form>({
    pageSize: 100,
    pageIndex: 0,
  });
  const [playHistories, setPlayHistories] = useState<PlayHistoryPage[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [, forceUpdate] = useReducer((x) => x + 1, 0);

  useEffect(() => {
    search();
  }, []);

  const search = (pForm?: Partial<Form>) => {
    const nf = {
      ...form,
      ...pForm,
    };

    setForm(nf);
    BApi.playHistory.searchPlayHistories(nf).then((a) => {
      setPlayHistories(a.data ?? []);
      setTotalCount(a.totalCount!);
    });
  };

  const renderPagination = () => {
    const pageCount = Math.ceil(totalCount / form.pageSize);

    if (pageCount > 1) {
      return (
        <div className={"flex justify-center"}>
          <Pagination
            page={form.pageIndex}
            size={"sm"}
            total={pageCount}
            onChange={(p) => search({ pageIndex: p })}
          />
        </div>
      );
    }

    return;
  };

  return (
    <div className="">
      {playHistories.length > 0 ? (
        <div className={"mt-1"}>
          <Table
            isCompact
            isHeaderSticky
            isStriped
            removeWrapper
            bottomContent={renderPagination()}
            topContent={renderPagination()}
          >
            <TableHeader>
              <TableColumn width="240">{t<string>("playHistory.label.playedAt")}</TableColumn>
              <TableColumn>{t<string>("playHistory.label.item")}</TableColumn>
            </TableHeader>
            <TableBody>
              {playHistories.map((a) => {
                return (
                  <TableRow key={a.id}>
                    <TableCell>{a.playedAt}</TableCell>
                    <TableCell>{a.item}</TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </div>
      ) : (
        <div>{t<string>("playHistory.empty.noHistory")}</div>
      )}
    </div>
  );
};

PlayHistoryPage.displayName = "PlayHistoryPage";

export default PlayHistoryPage;
