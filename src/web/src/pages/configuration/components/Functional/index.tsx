"use client";

import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  Button,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  Tooltip,
  Input,
  Radio,
  RadioGroup,
} from "@heroui/react";
import { AiOutlineQuestionCircle } from "react-icons/ai";

import { toast } from "@/components/bakaui";
import {
  CloseBehavior,
  CookieValidatorTarget,
  startupPages,
} from "@/sdk/constants";
import {
  useAppOptionsStore,
  useExHentaiOptionsStore,
  useUiOptionsStore,
} from "@/models/options";
import BApi from "@/sdk/BApi";

export default ({
  applyPatches = () => {},
}: {
  applyPatches: (API: any, patches: any) => void;
}) => {
  const { t } = useTranslation();

  const appOptions = useAppOptionsStore((state) => state.data);
  const exhentaiOptions = useExHentaiOptionsStore((state) => state.data);
  const [validatingExHentaiCookie, setValidatingExHentaiCookie] =
    useState(false);
  const uiOptions = useUiOptionsStore((state) => state.data);

  const [tmpExHentaiOptions, setTmpExHentaiOptions] = useState(
    exhentaiOptions || {},
  );

  useEffect(() => {
    setTmpExHentaiOptions(JSON.parse(JSON.stringify(exhentaiOptions || {})));
  }, [exhentaiOptions]);

  const functionSettings = [
    {
      label: "ExHentai",
      tip:
        "Cookie is required for this feature. The format of excluded tags is something like 'language:chinese', " +
        "and you can use * to replace namespace or tag, 'language:*' for example.",
      renderCell: () => {
        return (
          <div className={"exhentai-options"}>
            <div>
              <Input
                label={"Cookie"}
                size={"sm"}
                value={tmpExHentaiOptions.cookie}
                onValueChange={(v) => {
                  setTmpExHentaiOptions({
                    ...tmpExHentaiOptions,
                    cookie: v,
                  });
                }}
              />
            </div>
            {/*<div>*/}
            {/*  <Select*/}
            {/*    dataSource={tmpExHentaiOptions?.enhancer?.excludedTags?.map(*/}
            {/*      (e) => ({ label: e, value: e }),*/}
            {/*    )}*/}
            {/*    label={*/}
            {/*      <Tooltip*/}
            {/*        content={t<string>(*/}
            {/*          "You can filter some namespaces and tags such as 'language:*' for ignoring all tags in language namespace",*/}
            {/*        )}*/}
            {/*      >*/}
            {/*        {t<string>("Excluded tags")}*/}
            {/*      </Tooltip>*/}
            {/*    }*/}
            {/*    selectionMode={"multiple"}*/}
            {/*    size={"sm"}*/}
            {/*    style={{ width: "100%" }}*/}
            {/*    value={tmpExHentaiOptions?.enhancer?.excludedTags}*/}
            {/*    onSelectionChange={(v) => {*/}
            {/*      setTmpExHentaiOptions({*/}
            {/*        ...tmpExHentaiOptions,*/}
            {/*        enhancer: {*/}
            {/*          ...(tmpExHentaiOptions?.enhancer || {}),*/}
            {/*          excludedTags: v,*/}
            {/*        },*/}
            {/*      });*/}
            {/*    }}*/}
            {/*  />*/}
            {/*</div>*/}
            <div className={"operations"}>
              <Button
                color={"primary"}
                size={"sm"}
                onPress={() => {
                  applyPatches(
                    BApi.options.patchExHentaiOptions,
                    tmpExHentaiOptions,
                  );
                }}
              >
                {t<string>("Save")}
              </Button>
              <Button
                disabled={
                  !(tmpExHentaiOptions?.cookie?.length > 0) ||
                  validatingExHentaiCookie
                }
                isLoading={validatingExHentaiCookie}
                size={"sm"}
                onPress={() => {
                  setValidatingExHentaiCookie(true);
                  BApi.tool
                    .validateCookie({
                      cookie: tmpExHentaiOptions.cookie,
                      target: CookieValidatorTarget.ExHentai,
                    })
                    .then((r) => {
                      if (r.code) {
                        toast.danger(
                          `${t<string>("Invalid cookie")}:${r.message}`,
                        );
                      } else {
                        toast.success(t<string>("Cookie is good"));
                      }
                    })
                    .finally(() => {
                      setValidatingExHentaiCookie(false);
                    });
                }}
              >
                {t<string>("Validate cookie")}
              </Button>
            </div>
          </div>
        );
      },
    },
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
