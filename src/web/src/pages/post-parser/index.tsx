"use client";

import { useTranslation } from "react-i18next";
import {
  AiOutlineDelete,
  AiOutlinePlusCircle,
  AiOutlineQuestionCircle,
  AiOutlineSetting,
  AiOutlineWarning,
} from "react-icons/ai";
import _ from "lodash";
import React, { useEffect } from "react";

import {
  Alert,
  Button,
  Checkbox,
  Divider,
  Modal,
  Snippet,
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
  postParserSources,
  ThirdPartyId,
} from "@/sdk/constants";
import { useThirdPartyOptionsStore } from "@/stores/options";
import { usePostParserTasksStore } from "@/stores/postParserTasks";
import ConfigurationModal from "@/pages/post-parser/components/ConfigurationModal";
import ThirdPartyIcon from "@/components/ThirdPartyIcon";

const ThirdPartyMap: Record<PostParserSource, ThirdPartyId> = {
  [PostParserSource.SoulPlus]: ThirdPartyId.SoulPlus,
};
const PostParserPage = () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const thirdPartyOptions = useThirdPartyOptionsStore((state) => state.data);

  const tasks = usePostParserTasksStore((state) => state.tasks);

  useEffect(() => {}, []);

  return (
    <div>
      <div className={"flex justify-between items-center"}>
        <div className={"flex items-center gap-2"}>
          <Button
            onPress={() => {
              let linksTextMap: Record<number, string> = {};
              createPortal(Modal, {
                defaultVisible: true,
                size: 'xl',
                title: t<string>("postParser.action.addTasks"),
                children: (
                  <div>
                    {postParserSources.map(s => {
                      return (
                        <Textarea
                          onValueChange={v => {
                            linksTextMap[s.value] = v;
                          }}
                          label={t<string>("postParser.input.postLinks", { source: s.label })}
                          minRows={10}
                          placeholder={`https://xxxxxxx
https://xxxxxxx
...`}
                        />
                      );
                    })}
                    <FeatureStatusTip status={'developing'} name={t<string>("postParser.tip.otherSitesSupport")} />
                  </div>
                ),
                onOk: async () => {
                  const linksMap = _.mapValues(linksTextMap, value => value.split('\n').map(x => x.trim()).filter(x => x));
                  await BApi.postParser.addPostParserTasks(linksMap);
                },
              });
            }}
            size={'sm'}
            // variant={'flat'}
            color={'primary'}
          >
            <AiOutlinePlusCircle className={"text-base"} />
            {t<string>("Add tasks")}
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
        </div>
        <div className={"flex items-center gap-2"}>
          <Checkbox
            isSelected={thirdPartyOptions.automaticallyParsingPosts}
            onValueChange={async v => {
              const r = await BApi.options.patchThirdPartyOptions({ automaticallyParsingPosts: v });
              if (v && !r.code) {
                await BApi.postParser.startAllPostParserTasks();
              }
            }}
            size={'sm'}
            // variant={'flat'}
            color={'secondary'}
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
                            {t<string>("postParser.tip.ollamaRequired")}
                          </div>
                          <div>
                            {t<string>("postParser.tip.ollamaModel")}
                          </div>
                          <div>
                            {t<string>("postParser.tip.ollamaTested")}
                          </div>
                        </div>
                      }
                      title={t<string>("postParser.label.ollama")}
                    />
                    <Alert
                      color={"success"}
                      description={
                        <div>
                          <div>
                            {t<string>("postParser.tip.browserIntegration")}
                          </div>
                        </div>
                      }
                      title={t<string>("postParser.label.browserIntegrations")}
                      variant={"flat"}
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
            {/* <TableColumn>{t<string>('Content')}</TableColumn> */}
            <TableColumn>{t<string>("postParser.table.items")}</TableColumn>
            <TableColumn>{t<string>("postParser.table.parsedAt")}</TableColumn>
            <TableColumn>{t<string>("common.label.operations")}</TableColumn>
          </TableHeader>
          <TableBody>
            {tasks.map((task) => {
              return (
                <TableRow>
                  <TableCell>{task.id}</TableCell>
                  <TableCell>
                    {task.title ? (
                      <div className={"flex flex-col gap-1"}>
                        <div className={"flex items-center gap-1"}>
                          {/* <Chip */}
                          {/*   size={'sm'} */}
                          {/*   variant={'flat'} */}
                          {/* >{t<string>(`DownloadTaskParserSource.${DownloadTaskParserSource[task.source]}`)}</Chip> */}
                          <ThirdPartyIcon
                            thirdPartyId={ThirdPartyMap[task.source]}
                          />
                          <div>{task.title}</div>
                        </div>
                        <div>
                          <Button
                            color={"primary"}
                            size={"sm"}
                            variant={"light"}
                            onPress={(e) => {
                              BApi.gui.openUrlInDefaultBrowser({
                                url: task.link,
                              });
                            }}
                          >
                            {task.link}
                          </Button>
                        </div>
                      </div>
                    ) : (
                      <div className={"flex items-center gap-1"}>
                        {/* <Chip */}
                        {/*   size={'sm'} */}
                        {/*   variant={'flat'} */}
                        {/* >{t<string>(`DownloadTaskParserSource.${DownloadTaskParserSource[task.source]}`)}</Chip> */}
                        <ThirdPartyIcon
                          thirdPartyId={ThirdPartyMap[task.source]}
                        />
                        <div>
                          <Button
                            color={"primary"}
                            size={"sm"}
                            variant={"light"}
                            onPress={(e) => {
                              BApi.gui.openUrlInDefaultBrowser({
                                url: task.link,
                              });
                            }}
                          >
                            {task.link}
                          </Button>
                        </div>
                      </div>
                    )}
                  </TableCell>
                  {/* <TableCell> */}
                  {/*   {task.content && ( */}
                  {/*     <Button onPress={() => { */}
                  {/*       createPortal(Modal, { */}
                  {/*         size: 'xl', */}
                  {/*         defaultVisible: true, */}
                  {/*         children: ( */}
                  {/*           <pre>{task.content}</pre> */}
                  {/*         ), */}
                  {/*       }); */}
                  {/*     }} */}
                  {/*     > */}
                  {/*       {t<string>('View')} */}
                  {/*     </Button> */}
                  {/*   )} */}
                  {/* </TableCell> */}
                  <TableCell>
                    {task.items?.map((item, idx) => {
                      return (
                        <React.Fragment key={idx}>
                          <div className={"flex flex-col gap-1"}>
                            {item.link && (
                              <Button
                                color={"primary"}
                                size={"sm"}
                                variant={"light"}
                                onPress={(e) => {
                                  BApi.gui.openUrlInDefaultBrowser({
                                    url: item.link,
                                  });
                                }}
                              >
                                {item.link}
                              </Button>
                            )}
                            <div>
                              {item.accessCode && (
                                <Snippet
                                  size={"sm"}
                                  symbol={t<string>("postParser.label.accessCode")}
                                >
                                  {item.accessCode}
                                </Snippet>
                              )}
                              {item.decompressionPassword && (
                                <Snippet
                                  size={"sm"}
                                  symbol={t<string>("postParser.label.decompressionPassword")}
                                >
                                  {}
                                </Snippet>
                              )}
                            </div>
                          </div>
                          {idx < task.items!.length - 1 && <Divider />}
                        </React.Fragment>
                      );
                    })}
                  </TableCell>
                  <TableCell>
                    {task.parsedAt ? (
                      task.parsedAt
                    ) : task.error ? (
                      <Button
                        isIconOnly
                        color={"danger"}
                        size={"sm"}
                        variant={"light"}
                        onPress={() => {
                          createPortal(Modal, {
                            defaultVisible: true,
                            size: "xl",
                            children: <pre>{task.error}</pre>,
                            footer: {
                              actions: ["cancel"],
                            },
                          });
                        }}
                      >
                        <AiOutlineWarning className={"text-base"} />
                      </Button>
                    ) : null}
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
