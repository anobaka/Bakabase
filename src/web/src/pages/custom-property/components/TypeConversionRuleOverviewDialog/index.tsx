"use client";

import type { StandardValueConversionRule } from "@/sdk/constants";

import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";
import { QuestionCircleOutlined } from "@ant-design/icons";

import TypeConversionExampleDialog from "../TypeConversionExampleDialog";

import {
  Button,
  Chip,
  Modal,
  Tab,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  Tabs,
  Tooltip,
} from "@/components/bakaui";
import { PropertyType, propertyTypes } from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
const TypeConversionRuleOverviewDialog = () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [rules, setRules] = useState<
    Record<
      number,
      Record<
        number,
        {
          rule: StandardValueConversionRule;
          name: string;
          description: string | null;
        }[]
      >
    >
  >();

  const columns = [
    // <TableColumn>{t<string>('Source type')}</TableColumn>,
    <TableColumn>{t<string>("Target type")}</TableColumn>,
    <TableColumn>{t<string>("Rules")}</TableColumn>,
  ];

  useEffect(() => {
    BApi.customProperty.getCustomPropertyConversionRules().then((r) => {
      // @ts-ignore
      setRules(r.data);
    });
  }, []);

  const renderRows = (fromType: PropertyType): any[] => {
    if (!rules) {
      return [];
    }

    const targetMap = rules[fromType];

    if (!targetMap) {
      return [];
    }

    const rows: any[] = [];

    Object.keys(targetMap).forEach((toTypeStr) => {
      const toType = parseInt(toTypeStr, 10) as PropertyType;
      const rules = targetMap[toType];

      rows.push(
        <TableRow key={fromType}>
          {/* <TableCell>{t<string>(PropertyType[fromType])}</TableCell> */}
          <TableCell>{t<string>(PropertyType[toType])}</TableCell>
          <TableCell>
            <div className={"flex flex-wrap gap-1"}>
              {rules.map((r) => {
                if (r.description == null) {
                  return <Chip size={"sm"}>{r.name}</Chip>;
                }

                return (
                  <Tooltip content={<pre>{r.description}</pre>}>
                    <Chip size={"sm"}>
                      <div className={"flex items-center gap-1"}>
                        {r.name}
                        <QuestionCircleOutlined className={"text-sm"} />
                      </div>
                    </Chip>
                  </Tooltip>
                );
              })}
            </div>
          </TableCell>
        </TableRow>,
      );
    });

    return rows;
  };

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["cancel"],
        cancelProps: {
          children: t<string>("Close"),
        },
      }}
      size={"xl"}
      title={
        <div className={"flex items-center gap-2"}>
          {t<string>("Type conversion rules")}
          <Button
            color={"secondary"}
            size={"sm"}
            variant={"light"}
            onClick={() => {
              createPortal(TypeConversionExampleDialog, {});
            }}
          >
            {t<string>("Check examples")}
          </Button>
        </div>
      }
    >
      <div>
        <Tabs isVertical disabledKeys={["title"]}>
          <Tab key={"title"} title={t<string>("Source type")} />
          {propertyTypes.map((cpt) => {
            return (
              <Tab
                key={cpt.value}
                className={"w-full"}
                title={t<string>(PropertyType[cpt.value])}
              >
                <Table isCompact isHeaderSticky isStriped removeWrapper>
                  <TableHeader>{columns}</TableHeader>
                  <TableBody>{renderRows(cpt.value)}</TableBody>
                </Table>
              </Tab>
            );
          })}
        </Tabs>
      </div>
    </Modal>
  );
};

TypeConversionRuleOverviewDialog.displayName =
  "TypeConversionRuleOverviewDialog";

export default TypeConversionRuleOverviewDialog;
