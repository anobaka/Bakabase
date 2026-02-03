"use client";

import { useTranslation } from "react-i18next";
import {
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  Radio,
  RadioGroup,
} from "@heroui/react";

import { CloseBehavior, startupPages } from "@/sdk/constants";
import { useAppOptionsStore, useUiOptionsStore } from "@/stores/options";
import BApi from "@/sdk/BApi";

interface FunctionalProps {
  applyPatches: <T>(api: (patches: T) => Promise<{ code?: number }>, patches: T) => void;
}

const Functional: React.FC<FunctionalProps> = ({ applyPatches }) => {
  const { t } = useTranslation();

  const appOptions = useAppOptionsStore((state) => state.data);
  const uiOptions = useUiOptionsStore((state) => state.data);

  const functionSettings = [
    {
      label: "configuration.functional.startupPage",
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
      label: "configuration.functional.exitBehavior",
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
                {t<string>(`configuration.functional.exitBehavior.${CloseBehavior[c].toLowerCase()}`)}
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
              {t<string>("configuration.functional.title")}
            </TableColumn>
            <TableColumn>&nbsp;</TableColumn>
          </TableHeader>
          <TableBody>
            {functionSettings.map((c, i) => {
              return (
                <TableRow
                  key={i}
                  className="hover:bg-[var(--bakaui-overlap-background)]"
                >
                  <TableCell>
                    <div className="flex items-center">
                      {t(c.label)}
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
