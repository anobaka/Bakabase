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
import { useThirdPartyOptionsStore } from "@/models/options";
import { usePostParserTasksStore } from "@/models/postParserTasks";
import ConfigurationModal from "@/pages/post-parser/components/ConfigurationModal";
import ThirdPartyIcon from "@/components/ThirdPartyIcon";

const ThirdPartyMap: Record<PostParserSource, ThirdPartyId> = {
  [PostParserSource.SoulPlus]: ThirdPartyId.SoulPlus,
};

export default () => {
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
                title: t<string>('Add tasks'),
                children: (
                  <div>
                    {postParserSources.map(s => {
                      return (
                        <Textarea
                          onValueChange={v => {
                            linksTextMap[s.value] = v;
                          }}
                          label={t<string>('Post links in {{source}}', { source: s.label })}
                          minRows={10}
                          placeholder={`https://xxxxxxx
https://xxxxxxx
...`}
                        />
                      );
                    })}
                    <FeatureStatusTip status={'developing'} name={t<string>('Support for other sites')} />
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
            {t<string>("Configuration")}
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
            {t<string>("Automatically parsing")}
          </Checkbox>
          <Button
            color={"success"}
            size={"sm"}
            variant={"light"}
            onPress={() => {
              createPortal(Modal, {
                defaultVisible: true,
                size: "xl",
                title: t<string>("Instructions for use"),
                children: (
                  <div>
                    <Alert
                      description={
                        <div>
                          <div>
                            {t<string>(
                              "This feature internally uses curl. If your system-level curl version is lower than 8.14, please configure the correct curl path in the system settings.",
                            )}
                          </div>
                          <div>
                            {t<string>(
                              "The request interval configuration is temporarily unavailable. The built-in default interval is 3 seconds.",
                            )}
                          </div>
                        </div>
                      }
                      title={t<string>("curl")}
                    />
                    <Alert
                      description={
                        <div>
                          <div>
                            {t<string>(
                              "This feature internally uses Ollama. You need to install and run Ollama first, and install at least one model. Then configure the Ollama API address in the system settings.",
                            )}
                          </div>
                          <div>
                            {t<string>(
                              "This feature will prioritize the largest model in the Ollama model list.",
                            )}
                          </div>
                          <div>
                            {t<string>(
                              "Currently, deepseek-r1:14b and 32b have been tested and can be used safely.",
                            )}
                          </div>
                        </div>
                      }
                      title={t<string>("ollama")}
                    />
                    <Alert
                      color={"success"}
                      description={
                        <div>
                          <div>
                            {t<string>(
                              'Some sites can integrate quick actions through the browser (such as one-click task creation). You can check the "Third-Party Integrations" section.',
                            )}
                          </div>
                        </div>
                      }
                      title={t<string>("browser integrations")}
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
            {t<string>("Instructions for use")}
          </Button>
        </div>
      </div>
      <div className={"mt-2"}>
        <Table isStriped removeWrapper className={"break-all"}>
          <TableHeader>
            <TableColumn>{t<string>("#")}</TableColumn>
            <TableColumn>{t<string>("Target")}</TableColumn>
            {/* <TableColumn>{t<string>('Content')}</TableColumn> */}
            <TableColumn>{t<string>("Items")}</TableColumn>
            <TableColumn>{t<string>("ParsedAt")}</TableColumn>
            <TableColumn>{t<string>("Operations")}</TableColumn>
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
                                  symbol={t<string>("Access code")}
                                >
                                  {item.accessCode}
                                </Snippet>
                              )}
                              {item.decompressionPassword && (
                                <Snippet
                                  size={"sm"}
                                  symbol={t<string>("Decompression password")}
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
