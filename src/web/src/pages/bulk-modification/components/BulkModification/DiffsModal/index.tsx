"use client";

"use strict";

import type { IProperty } from "@/components/Property/models";
import type { DestroyableProps } from "@/components/bakaui/types";

import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";
import { ArrowRightOutlined, SearchOutlined } from "@ant-design/icons";

import {
  Chip,
  Input,
  Kbd,
  Modal,
  Pagination,
  Spinner,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
} from "@/components/bakaui";
import PropertyValueRenderer from "@/components/Property/components/PropertyValueRenderer";
import BApi from "@/sdk/BApi";

type BulkModificationDiff = {
  resourcePath: string;
  diffs: Diff[];
};

type Diff = {
  property: IProperty;
  value1?: string;
  value2?: string;
};

const PageSize = 20;

type Props = {
  bmId: number;
} & DestroyableProps;
const DiffsModal = ({ bmId, onDestroyed }: Props) => {
  const { t } = useTranslation();

  const [keyword, setKeyword] = useState<string>();
  const [pageable, setPageable] = useState({
    page: 0,
    total: 0,
  });
  const [data, setData] = useState<BulkModificationDiff[]>();

  useEffect(() => {
    search(undefined, 1);
  }, []);

  const TPagination = () => {
    if (pageable.total > 1) {
      return (
        <div className={"flex justify-end"}>
          <Pagination
            page={pageable.page}
            size={"sm"}
            total={pageable.total}
            onChange={(page) => {
              search(keyword, page);
            }}
          />
        </div>
      );
    }

    return null;
  };

  const search = (keyword: string | undefined, page: number) => {
    setPageable({
      ...pageable,
      page,
    });
    BApi.bulkModification
      .searchBulkModificationDiffs(bmId, {
        path: keyword,
        pageIndex: page,
        pageSize: PageSize,
      })
      .then((r) => {
        setData(r.data || []);
        setPageable({
          ...pageable,
          total: Math.ceil(r.totalCount / PageSize),
        });
      });
  };

  return (
    <Modal defaultVisible size={"xl"} onDestroyed={onDestroyed}>
      <div>
        <div className={"flex items-center gap-8"}>
          <Input
            endContent={<Kbd keys={["enter"]} />}
            placeholder={t<string>("common.action.search")}
            size={"sm"}
            startContent={<SearchOutlined className={"text-base"} />}
            value={keyword}
            onKeyDown={(e) => {
              if (e.key == "Enter") {
                search(keyword, 1);
              }
            }}
            onValueChange={(e) => setKeyword(e)}
          />
          <TPagination />
        </div>
      </div>
      {data ? (
        data.length == 0 ? (
          <div className={"flex justify-center mt-4"}>
            <div>
              <div>{t<string>("bulkModification.empty.noData")}</div>
              <div>
                1.{" "}
                {t<string>("bulkModification.info.ensureCalculation")}
              </div>
              <div>
                2.{" "}
                {t<string>("bulkModification.info.noResourcesToChange")}
              </div>
              <div>3. {t<string>("bulkModification.error.checkCriteria")}</div>
            </div>
          </div>
        ) : (
          <Table removeWrapper>
            <TableHeader>
              <TableColumn>{t<string>("bulkModification.label.resource")}</TableColumn>
              <TableColumn>{t<string>("bulkModification.label.diffs")}</TableColumn>
            </TableHeader>
            <TableBody>
              {data.map((d) => {
                return (
                  <TableRow>
                    <TableCell>
                      <div className={"max-w-[600px] break-all"}>
                        {d.resourcePath}
                      </div>
                    </TableCell>
                    <TableCell>
                      {d.diffs.map((diff) => {
                        return (
                          <div className={"flex items-center gap-2"}>
                            <div className={"flex items-center gap-1"}>
                              <Chip
                                color={"secondary"}
                                size={"sm"}
                                variant={"flat"}
                              >
                                {diff.property.poolName}
                              </Chip>
                              <Chip color={"primary"} size={"sm"}>
                                {diff.property.name}
                              </Chip>
                            </div>
                            <div className={"flex items-center gap-1"}>
                              <PropertyValueRenderer
                                bizValue={diff.value1}
                                property={diff.property}
                              />
                              <ArrowRightOutlined className={"text-base"} />
                              <PropertyValueRenderer
                                bizValue={diff.value2}
                                property={diff.property}
                              />
                            </div>
                          </div>
                        );
                      })}
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        )
      ) : (
        <div className={"flex justify-center mt-12"}>
          <Spinner />
        </div>
      )}
      <TPagination />
    </Modal>
  );
};

DiffsModal.displayName = "DiffsModal";

export default DiffsModal;
