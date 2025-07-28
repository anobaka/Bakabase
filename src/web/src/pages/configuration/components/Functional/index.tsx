"use client";

import { useTranslation } from "react-i18next";
import {
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  Tooltip,
  Radio,
  RadioGroup,
} from "@heroui/react";
import { AiOutlineQuestionCircle } from "react-icons/ai";

import { CloseBehavior, startupPages } from "@/sdk/constants";
import { useAppOptionsStore, useUiOptionsStore } from "@/stores/options";
import BApi from "@/sdk/BApi";
const Functional = ({
  applyPatches = () => {},
}: {
  applyPatches: (API: any, patches: any) => void;
}) => {
  const { t } = useTranslation();

  const appOptions = useAppOptionsStore((state) => state.data);
  const uiOptions = useUiOptionsStore((state) => state.data);

  const functionSettings = [
    {
      label: "Startup page",
      renderCell: () => {
        return (
          <RadioGroup
            orientation={"horizontal"}
            size={"sm"}
            value={String(uiOptions.startupPage)}
            onValueChange={(v) => {
              applyPatches(BApi.options.patchUiOptions, {
                startupPage: Number(v),
              });
            }}
          >
            {startupPages.map((s) => {
              return (
                <Radio key={s.value} value={String(s.value)}>
                  {t<string>(s.label)}
                </Radio>
              );
            })}
          </RadioGroup>
        );
      },
    },
    {
      label: "Exit behavior",
      renderCell: () => {
        return (
          <RadioGroup
            orientation={"horizontal"}
            size={"sm"}
            value={String(appOptions.closeBehavior)}
            onValueChange={(v) => {
              applyPatches(BApi.options.patchAppOptions, {
                closeBehavior: Number(v),
              });
            }}
          >
            {[
              CloseBehavior.Minimize,
              CloseBehavior.Exit,
              CloseBehavior.Prompt,
            ].map((c) => (
              <Radio key={c} value={String(c)}>
                {t<string>(CloseBehavior[c])}
              </Radio>
            ))}
          </RadioGroup>
        );
      },
    },
  ];

  return (
    <div className="group">
      {/* <Title title={i18n.t<string>('Functional configurations')} /> */}
      <div className="settings">
        <Table removeWrapper>
          <TableHeader>
            <TableColumn width={200}>
              {t<string>("Functional configurations")}
            </TableColumn>
            <TableColumn>&nbsp;</TableColumn>
          </TableHeader>
          <TableBody>
            {functionSettings.map((c, i) => {
              return (
                <TableRow
                  key={i}
                  className={"hover:bg-[var(--bakaui-overlap-background)]"}
                >
                  <TableCell>
                    <div style={{ display: "flex", alignItems: "center" }}>
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
                  <TableCell>{c.renderCell()}</TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      </div>
    </div>
  );
};

Functional.displayName = "Functional";

export default Functional;
