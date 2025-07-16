"use client";

import type { IPscPropertyMatcherValue } from "../models/PscPropertyMatcherValue";
import type { PathSegmentConfigurationPropsMatcherOptions } from "..";

import React, { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { useUpdateEffect } from "react-use";

import { PscPropertyType } from "../models/PscPropertyType";
import { PscMatcherValue } from "../models/PscMatcherValue";

import PscMatcher from "@/components/PathSegmentsConfiguration/models/PscMatcher";
import { ResourceProperty } from "@/sdk/constants";
import { Button, Modal, Badge, Popover } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import BusinessConstants from "@/components/BusinessConstants";
import { buildLayerBasedPathRegexString } from "@/components/utils";
import PscProperty from "@/components/PathSegmentsConfiguration/models/PscProperty";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

type Props = {
  segments: string[];
  value: IPscPropertyMatcherValue[];
  isDirectory: boolean;
  matchers: PathSegmentConfigurationPropsMatcherOptions[];
  onChange: (value: IPscPropertyMatcherValue[]) => void;
};

export default ({
  segments,
  value,
  isDirectory,
  matchers,
  onChange,
}: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [fileResource, setFileResource] = useState<string>();
  const [fileResourceExtensions, setFileResourceExtensions] =
    useState<{ ext: string; count: number }[]>();
  const [fileResourceExtensionCandidates, setFileResourceExtensionCandidates] =
    useState<string[]>([]);
  const [loadingFileResourceExtensions, setLoadingFileResourceExtensions] =
    useState(false);
  const resourceMatcherMatchesLastLayerRef = useRef(false);

  const fileSegments = fileResource?.split(".") ?? [];
  let currentFileExt;

  if (fileSegments?.length > 1) {
    currentFileExt = `.${fileSegments[fileSegments.length - 1]}`;
  }

  useEffect(() => {
    if (!isDirectory) {
      if (
        matchers.some(
          (a) => a.propertyType == PscPropertyType.Resource && !a.readonly,
        )
      ) {
        const resourceMatcherValue = value[ResourceProperty.Resource]?.[0];

        if (resourceMatcherValue) {
          const rootMatch = PscMatcher.matchFirst(
            segments,
            value
              .filter((v) => v.property.isRootPath && v.value != undefined)
              .map((v) => v.value!),
          );
          const rootSegmentIndex = rootMatch?.index ?? -1;

          const resourceSegmentIndex =
            PscMatcher.match(
              segments,
              resourceMatcherValue,
              rootSegmentIndex,
              segments.length,
            )?.index ?? -1;

          if (resourceSegmentIndex > -1) {
            const resourceMatcherMatchesLastLayer =
              resourceSegmentIndex == segments.length - 1;

            if (
              resourceMatcherMatchesLastLayer !=
              resourceMatcherMatchesLastLayerRef.current
            ) {
              resourceMatcherMatchesLastLayerRef.current =
                resourceMatcherMatchesLastLayer;
              if (resourceMatcherMatchesLastLayer) {
                setFileResource(segments.join("/"));
              } else {
                setFileResource(undefined);
              }
            }
          }
        }
      }
    }
  }, [value]);

  useUpdateEffect(() => {
    if (fileResource == undefined) {
      setFileResourceExtensions(undefined);
      setFileResourceExtensionCandidates([]);
    }
  }, [fileResource]);

  if (fileResource == undefined) {
    return null;
  }

  const rootSegmentIndex =
    PscMatcher?.match(
      segments,
      value.find((v) => v.property.isRootPath)?.value,
      -1,
      undefined,
    )?.index ?? -1;
  const rootPathIsSelected = rootSegmentIndex > -1;
  const loaderBtn = (
    <Button
      color={"primary"}
      isDisabled={!rootPathIsSelected}
      isLoading={loadingFileResourceExtensions}
      onClick={() => {
        setLoadingFileResourceExtensions(true);
        BApi.file
          .getFileExtensionCounts({
            sampleFile: fileResource,
            rootPath: segments
              .slice(0, rootSegmentIndex + 1)
              .join(BusinessConstants.pathSeparator),
          })
          .then((a) => {
            if (!a.code) {
              const counts = a.data ?? {};
              const list = Object.keys(counts)
                .sort((x, y) => counts[y]! - counts[x]!)
                .map((b) => ({
                  ext: b,
                  count: counts[b]!,
                }));

              setFileResourceExtensions(list);
              if (currentFileExt) {
                setFileResourceExtensionCandidates([currentFileExt]);

                // we should change the value of resource matcher to file-extension-based regex immediately when user click this button
                const newRegex = buildLayerBasedPathRegexString(
                  segments.length - rootSegmentIndex - 1,
                  [currentFileExt],
                );
                const newValue = value.filter(
                  (x) => !x.property.equals(PscProperty.Resource),
                );

                newValue.push({
                  property: PscProperty.Resource,
                  value: PscMatcherValue.Regex(newRegex),
                });
                onChange(newValue);
              }
            }
          })
          .finally(() => {
            setLoadingFileResourceExtensions(false);
          });
      }}
    >
      {t<string>("I need choose files with specific types as resources")}
    </Button>
  );

  return (
    <div className="file-resource-helper psc-block">
      <div className="title">{t<string>("Match by file extensions")}</div>
      {fileResourceExtensions == undefined ? (
        <>
          <Message type={"warning"}>
            <>
              {t<string>(
                "Please pay attention, you marked a file as a resource. We'll treat all files or folders with same path layer as resources by default. It may not be your actual design(if you chose a movie and its subtitle file has the same path layer, then the subtitle file will be another resource).",
              )}
              {t<string>(
                "If you want to mark resources with specific file types, you can click button below to load all file types and select some of them.",
              )}
            </>
          </Message>
          {rootPathIsSelected ? (
            loaderBtn
          ) : (
            <Popover
              align={"t"}
              trigger={loaderBtn}
              triggerType={"hover"}
            >
              {t<string>("Please select root path first.")}
            </Popover>
          )}
        </>
      ) : fileResourceExtensions.length > 0 ? (
        <>
          <Message type={"notice"}>
            <>
              {t<string>("You can mark resources by file types.")}
              {fileResourceExtensionCandidates.length > 0
                ? `${t<string>("You selected {{count}} file types:", { count: fileResourceExtensionCandidates.length })}${fileResourceExtensionCandidates.join(", ")}`
                : t<string>(
                    "None of file types has been selected, we'll treat all files or folders with same path layer as your file as resources.",
                  )}
            </>
          </Message>
          <Tag.Group style={{ marginTop: 5 }}>
            {fileResourceExtensions.map((e) => {
              return (
                <Tag.Selectable
                  checked={fileResourceExtensionCandidates.indexOf(e.ext) > -1}
                  size={"small"}
                  onChange={(c) => {
                    const candidates = fileResourceExtensionCandidates || [];
                    const idx = candidates.indexOf(e.ext);

                    const apply = (candidates) => {
                      const newRegex = buildLayerBasedPathRegexString(
                        segments.length - rootSegmentIndex - 1,
                        candidates,
                      );
                      const newValue = value.filter(
                        (x) => !x.property.equals(PscProperty.Resource),
                      );

                      newValue.push({
                        property: PscProperty.Resource,
                        value: PscMatcherValue.Regex(newRegex),
                      });
                      setFileResourceExtensionCandidates([...candidates]);
                    };

                    if (idx == -1) {
                      candidates.push(e.ext);
                    } else {
                      if (e.ext == currentFileExt) {
                        createPortal(Modal, {
                          defaultVisible: true,
                          title: t<string>(
                            "You are deselecting extension same as file you selected",
                          ),
                          children: t<string>(
                            "After your deselection, the current file will not be treat as a resource, and you need reset the Resource part from path segments, are you sure to remove file type: {{ext}}?",
                            { ext: currentFileExt },
                          ),
                          onOk: () => async () => {
                            candidates.splice(idx, 1);
                            apply(candidates);
                          },
                        });

                        return;
                      } else {
                        candidates.splice(idx, 1);
                      }
                    }
                    apply(candidates);
                  }}
                >
                  {e.ext}
                  &nbsp;
                  <Badge
                    count={e.count}
                    overflowCount={99999999}
                    style={{ backgroundColor: "#87d068" }}
                  />
                </Tag.Selectable>
              );
            })}
          </Tag.Group>
        </>
      ) : (
        <>{t<string>("No known file type is found.")}</>
      )}
    </div>
  );
};
