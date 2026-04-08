"use client";

import { useTranslation } from "react-i18next";
import {
  AiOutlineDelete,
  AiOutlineDownload,
  AiOutlinePlusCircle,
  AiOutlineQuestionCircle,
  AiOutlineSetting,
  AiOutlineWarning,
} from "react-icons/ai";
import _ from "lodash";
import { useEffect } from "react";
import ReactJson from "react-json-view";
import * as XLSX from "xlsx";

import {
  Alert,
  Button,
  Checkbox,
  CheckboxGroup,
  Chip,
  Modal,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  Textarea,
} from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import FeatureStatusTip from "@/components/FeatureStatusTip";
import BApi from "@/sdk/BApi";
import {
  PostParserSource,
  PostParseTarget,
  PostParseTargetLabel,
  postParserSources,
  postParseTargets,
  ThirdPartyId,
} from "@/sdk/constants";
import { useThirdPartyOptionsStore } from "@/stores/options";
import { usePostParserTasksStore } from "@/stores/postParserTasks";
import ConfigurationModal from "@/pages/post-parser/components/ConfigurationModal";
import ThirdPartyIcon from "@/components/ThirdPartyIcon";
import TampermonkeyInstallButton from "@/components/ThirdPartyConfig/base/TampermonkeyInstallButton";
import type { PostParseTargetResult } from "@/core/models/PostParserTask";

const ThirdPartyMap: Record<PostParserSource, ThirdPartyId> = {
  [PostParserSource.SoulPlus]: ThirdPartyId.SoulPlus,
};

const PostParserPage = () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const thirdPartyOptions = useThirdPartyOptionsStore((state) => state.data);

  const tasks = usePostParserTasksStore((state) => state.tasks);

  useEffect(() => {}, []);

  const renderResultCell = (results: Record<number, PostParseTargetResult> | undefined, targets: PostParseTarget[]) => {
    if (!results || Object.keys(results).length === 0) {
      return (
        <div className={"text-default-400 text-sm"}>
          {t<string>("postParser.label.pending")}
        </div>
      );
    }

    return (
      <div className={"flex flex-col gap-2"}>
        {targets.map((target) => {
          const result = results[target];
          if (!result) {
            return (
              <Chip key={target} size={"sm"} variant={"flat"}>
                {t<string>(`PostParseTarget.${PostParseTargetLabel[target]}`)} - {t<string>("postParser.label.pending")}
              </Chip>
            );
          }

          if (result.error) {
            return (
              <div key={target} className={"flex items-center gap-1"}>
                <Chip color={"danger"} size={"sm"} variant={"flat"}>
                  {t<string>(`PostParseTarget.${PostParseTargetLabel[target]}`)}
                </Chip>
                <Button
                  isIconOnly
                  color={"danger"}
                  size={"sm"}
                  variant={"light"}
                  onPress={() => {
                    createPortal(Modal, {
                      defaultVisible: true,
                      size: "xl",
                      children: <pre className={"whitespace-pre-wrap break-all"}>{result.error}</pre>,
                      footer: { actions: ["cancel"] },
                    });
                  }}
                >
                  <AiOutlineWarning className={"text-base"} />
                </Button>
              </div>
            );
          }

          if (result.data) {
            return (
              <div key={target} className={"flex items-center gap-1"}>
                <Chip color={"success"} size={"sm"} variant={"flat"}>
                  {t<string>(`PostParseTarget.${PostParseTargetLabel[target]}`)}
                </Chip>
                <Button
                  color={"primary"}
                  size={"sm"}
                  variant={"light"}
                  onPress={() => {
                    createPortal(Modal, {
                      defaultVisible: true,
                      size: "xl",
                      title: t<string>(`PostParseTarget.${PostParseTargetLabel[target]}`),
                      children: (
                        <ReactJson
                          src={typeof result.data === "object" ? result.data : { value: result.data }}
                          enableClipboard
                          displayDataTypes={false}
                          quotesOnKeys={false}
                          name={false}
                          theme={"monokai"}
                        />
                      ),
                      footer: { actions: ["cancel"] },
                    });
                  }}
                >
                  {t<string>("postParser.label.viewResult")}
                </Button>
                {result.parsedAt && (
                  <span className={"text-xs text-default-400"}>{result.parsedAt}</span>
                )}
              </div>
            );
          }

          return null;
        })}
      </div>
    );
  };

  const handleExport = () => {
    const rows: Record<string, any>[] = [];

    for (const task of tasks) {
      if (!task.results || Object.keys(task.results).length === 0) {
        rows.push({
          "ID": task.id,
          "Source": PostParserSource[task.source] ?? task.source,
          "Link": task.link,
          "Title": task.title ?? "",
          "Target": "",
          "Resource Link": "",
          "Access Code": "",
          "Password": "",
          "Error": "",
          "ParsedAt": "",
        });
        continue;
      }

      for (const [targetKey, result] of Object.entries(task.results)) {
        const targetName = t<string>(`PostParseTarget.${PostParseTargetLabel[Number(targetKey) as PostParseTarget] ?? targetKey}`);

        if (result.data && typeof result.data === "object") {
          const data = result.data as any;
          if (data.resources && Array.isArray(data.resources) && data.resources.length > 0) {
            for (const r of data.resources) {
              rows.push({
                "ID": task.id,
                "Source": PostParserSource[task.source] ?? task.source,
                "Link": task.link,
                "Title": data.title ?? task.title ?? "",
                "Target": targetName,
                "Resource Link": r.link ?? "",
                "Access Code": r.code ?? "",
                "Password": r.password ?? "",
                "Error": result.error ?? "",
                "ParsedAt": result.parsedAt ?? "",
              });
            }
          } else {
            // Generic JSON: flatten top-level fields
            const flatFields: Record<string, string> = {};
            for (const [k, v] of Object.entries(data)) {
              flatFields[k] = typeof v === "object" ? JSON.stringify(v) : String(v ?? "");
            }
            rows.push({
              "ID": task.id,
              "Source": PostParserSource[task.source] ?? task.source,
              "Link": task.link,
              "Title": task.title ?? "",
              "Target": targetName,
              ...flatFields,
              "Error": result.error ?? "",
              "ParsedAt": result.parsedAt ?? "",
            });
          }
        } else {
          rows.push({
            "ID": task.id,
            "Source": PostParserSource[task.source] ?? task.source,
            "Link": task.link,
            "Title": task.title ?? "",
            "Target": targetName,
            "Error": result.error ?? "",
            "ParsedAt": result.parsedAt ?? "",
          });
        }
      }
    }

    const ws = XLSX.utils.json_to_sheet(rows);
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, "PostParser");
    XLSX.writeFile(wb, `post-parser-export-${new Date().toISOString().slice(0, 10)}.xlsx`);
  };

  return (
    <div>
      <div className={"flex justify-between items-center"}>
        <div className={"flex items-center gap-2"}>
          <Button
            onPress={() => {
              let linksTextMap: Record<number, string> = {};
              let selectedTargets: PostParseTarget[] = postParseTargets.map(t => t.value);
              createPortal(Modal, {
                defaultVisible: true,
                size: "xl",
                title: t<string>("postParser.action.addTasks"),
                children: (
                  <div className={"flex flex-col gap-4"}>
                    <div>
                      <div className={"text-sm font-semibold mb-2"}>
                        {t<string>("postParser.label.selectTargets")}
                      </div>
                      <CheckboxGroup
                        defaultValue={selectedTargets.map(String)}
                        orientation={"horizontal"}
                        onValueChange={(v) => {
                          selectedTargets = v.map(Number) as PostParseTarget[];
                        }}
                      >
                        {postParseTargets.map((target) => (
                          <Checkbox key={target.value} value={String(target.value)}>
                            {t<string>(`PostParseTarget.${target.label}`)}
                          </Checkbox>
                        ))}
                      </CheckboxGroup>
                    </div>
                    {postParserSources.map((s) => {
                      return (
                        <Textarea
                          key={s.value}
                          onValueChange={(v) => {
                            linksTextMap[s.value] = v;
                          }}
                          label={t<string>("postParser.input.postLinks", {
                            source: s.label,
                          })}
                          minRows={10}
                          placeholder={`https://xxxxxxx
https://xxxxxxx
...`}
                        />
                      );
                    })}
                    <FeatureStatusTip
                      status={"developing"}
                      name={t<string>("postParser.tip.otherSitesSupport")}
                    />
                  </div>
                ),
                onOk: async () => {
                  const linksMap = _.mapValues(linksTextMap, (value) =>
                    value
                      .split("\n")
                      .map((x) => x.trim())
                      .filter((x) => x),
                  );
                  await BApi.postParser.addPostParserTasks({
                    sourceLinksMap: linksMap,
                    targets: selectedTargets,
                  } as any);
                },
              });
            }}
            size={"sm"}
            color={"primary"}
          >
            <AiOutlinePlusCircle className={"text-base"} />
            {t<string>("postParser.action.addTasks")}
          </Button>
          <Button
            size={"sm"}
            onPress={() => {
              createPortal(ConfigurationModal, {});
            }}
          >
            <AiOutlineSetting className={"text-base"} />
            {t<string>("postParser.action.configuration")}
          </Button>
          <Button
            size={"sm"}
            color={"success"}
            variant={"flat"}
            onPress={handleExport}
            isDisabled={tasks.length === 0}
          >
            <AiOutlineDownload className={"text-base"} />
            {t<string>("postParser.action.export")}
          </Button>
          <Button
            color={"danger"}
            size={"sm"}
            variant={"light"}
            isDisabled={tasks.length === 0}
            onPress={async () => {
              await BApi.postParser.deleteAllPostParserTasks();
            }}
          >
            <AiOutlineDelete className={"text-base"} />
            {t<string>("postParser.action.deleteAll")}
          </Button>
        </div>
        <div className={"flex items-center gap-2"}>
          <Checkbox
            isSelected={thirdPartyOptions.automaticallyParsingPosts}
            onValueChange={async (v) => {
              const r = await BApi.options.patchThirdPartyOptions({
                automaticallyParsingPosts: v,
              });
              if (v && !r.code) {
                await BApi.postParser.startAllPostParserTasks();
              }
            }}
            size={"sm"}
            color={"secondary"}
          >
            {t<string>("postParser.label.automaticallyParsing")}
          </Checkbox>
          <Button
            color={"success"}
            size={"sm"}
            variant={"light"}
            onPress={() => {
              createPortal(Modal, {
                defaultVisible: true,
                size: "xl",
                title: t<string>("postParser.action.instructions"),
                children: (
                  <div>
                    <Alert
                      description={
                        <div>
                          <div>
                            {t<string>("postParser.tip.curlVersion")}
                          </div>
                          <div>
                            {t<string>("postParser.tip.requestInterval")}
                          </div>
                        </div>
                      }
                      title={t<string>("postParser.label.curl")}
                    />
                    <Alert
                      description={
                        <div>
                          <div>
                            {t<string>("postParser.tip.aiRequired")}
                          </div>
                        </div>
                      }
                      title={t<string>("postParser.label.ai")}
                    />
                    <TampermonkeyInstallButton
                      descriptions={[
                        t<string>("thirdPartyIntegration.tip.soulPlusClick"),
                      ]}
                    />
                  </div>
                ),
                footer: {
                  actions: ["cancel"],
                },
              });
            }}
          >
            <AiOutlineQuestionCircle className={"text-base"} />
            {t<string>("postParser.action.instructions")}
          </Button>
        </div>
      </div>
      <div className={"mt-2"}>
        <Table isStriped removeWrapper className={"break-all"}>
          <TableHeader>
            <TableColumn>{t<string>("postParser.table.id")}</TableColumn>
            <TableColumn>{t<string>("postParser.table.target")}</TableColumn>
            <TableColumn>{t<string>("postParser.table.results")}</TableColumn>
            <TableColumn>{t<string>("common.label.operations")}</TableColumn>
          </TableHeader>
          <TableBody>
            {tasks.map((task) => {
              return (
                <TableRow key={task.id}>
                  <TableCell>{task.id}</TableCell>
                  <TableCell>
                    <div className={"flex flex-col gap-1"}>
                      <div className={"flex items-center gap-1"}>
                        <ThirdPartyIcon
                          thirdPartyId={ThirdPartyMap[task.source]}
                        />
                        <div>{task.title || ""}</div>
                      </div>
                      <div>
                        <Button
                          color={"primary"}
                          size={"sm"}
                          variant={"light"}
                          onPress={() => {
                            BApi.gui.openUrlInDefaultBrowser({
                              url: task.link,
                            });
                          }}
                        >
                          {task.link}
                        </Button>
                      </div>
                      {task.targets && task.targets.length > 0 && (
                        <div className={"flex gap-1 flex-wrap"}>
                          {task.targets.map((target) => (
                            <Chip key={target} size={"sm"} variant={"flat"}>
                              {t<string>(`PostParseTarget.${PostParseTargetLabel[target]}`)}
                            </Chip>
                          ))}
                        </div>
                      )}
                    </div>
                  </TableCell>
                  <TableCell>
                    {renderResultCell(task.results, task.targets)}
                  </TableCell>
                  <TableCell>
                    <div className={"flex items-center gap-1"}>
                      <Button
                        isIconOnly
                        color={"danger"}
                        size={"sm"}
                        variant={"light"}
                        onPress={async () => {
                          await BApi.postParser.deletePostParserTask(task.id);
                        }}
                      >
                        <AiOutlineDelete className={"text-base"} />
                      </Button>
                    </div>
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      </div>
    </div>
  );
};

PostParserPage.displayName = "PostParserPage";

export default PostParserPage;
