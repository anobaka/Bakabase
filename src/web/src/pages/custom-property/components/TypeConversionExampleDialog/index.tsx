"use client";

import type { StandardValueType } from "@/sdk/constants";

import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";

import {
  Modal,
  Tab,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  Tabs,
} from "@/components/bakaui";
import { PropertyType } from "@/sdk/constants";
import { propertyTypes } from "@/sdk/constants";
import StandardValueRenderer from "@/components/StandardValue/ValueRenderer";
import { deserializeStandardValue } from "@/components/StandardValue/helpers";
import BApi from "@/sdk/BApi";

type Result = {
  type: PropertyType;
  serializedBizValue?: string;
  bizValueType: StandardValueType;
  outputs?: {
    type: PropertyType;
    serializedBizValue?: string;
    bizValueType: StandardValueType;
  }[];
};

export default () => {
  const { t } = useTranslation();

  const [results, setResults] = useState<Result[]>([]);

  const columns = [
    // <TableColumn>{t<string>('Type to be converted')}</TableColumn>,
    <TableColumn>{t<string>("Value to be converted")}</TableColumn>,
    ...propertyTypes.map((cpt) => {
      return <TableColumn>{t<string>(PropertyType[cpt.value])}</TableColumn>;
    }),
  ];

  useEffect(() => {
    BApi.customProperty.testCustomPropertyTypeConversion().then((r) => {
      // @ts-ignore
      setResults(r.data?.results ?? []);
    });
  }, []);

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["cancel"],
        cancelProps: {
          children: t<string>("Close"),
        },
      }}
      size={"full"}
      title={t<string>("Type conversion examples")}
    >
      <div>
        <Tabs isVertical disabledKeys={["title"]}>
          <Tab key={"title"} title={t<string>("Source type")} />
          {propertyTypes.map((cpt) => {
            const filteredResults =
              results?.filter((x) => x.type == cpt.value) || [];

            return (
              <Tab
                key={cpt.value}
                className={"w-full"}
                title={t<string>(PropertyType[cpt.value])}
              >
                <Table isCompact isHeaderSticky isStriped removeWrapper>
                  <TableHeader>{columns}</TableHeader>
                  <TableBody>
                    {filteredResults.map((td, i) => {
                      const cells = [
                        // <TableCell>{t<string>(PropertyType[td.type])}</TableCell>,
                        <TableCell>
                          <StandardValueRenderer
                            propertyType={td.type}
                            type={td.bizValueType}
                            value={deserializeStandardValue(
                              td.serializedBizValue ?? null,
                              td.bizValueType,
                            )}
                            variant={"default"}
                          />
                        </TableCell>,
                      ];

                      propertyTypes.forEach((type) => {
                        const o = filteredResults?.[i]?.outputs?.find(
                          (o) => o.type == type.value,
                        );

                        if (o) {
                          const deserializedValue = deserializeStandardValue(
                            o.serializedBizValue ?? null,
                            o.bizValueType,
                          );

                          console.log(o, deserializedValue);
                          cells.push(
                            <TableCell>
                              <StandardValueRenderer
                                propertyType={type.value}
                                type={o.bizValueType}
                                value={deserializedValue}
                                variant={"default"}
                              />
                            </TableCell>,
                          );
                        } else {
                          cells.push(<TableCell>/</TableCell>);
                        }
                      });

                      // console.log(cells);

                      return <TableRow key={i}>{cells}</TableRow>;
                    })}
                  </TableBody>
                </Table>
              </Tab>
            );
          })}
        </Tabs>
      </div>
    </Modal>
  );
};
