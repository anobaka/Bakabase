"use client";

import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { DeleteOutlined } from "@ant-design/icons";

import BApi from "@/sdk/BApi";
import {
  Button,
  Card,
  CardBody,
  CardHeader,
  Chip,
  Divider,
} from "@/components/bakaui";
import { ResourceCacheType } from "@/sdk/constants";

type MediaLibraryCacheOverview = {
  mediaLibraryId: number;
  mediaLibraryName: string;
  resourceCacheCountMap: Record<string, number>;
  resourceCount: number;
};

type UnassociatedCacheOverview = {
  resourceCacheCountMap: Record<string, number>;
  resourceCount: number;
};

type CacheOverview = {
  mediaLibraryCaches: MediaLibraryCacheOverview[];
  unassociatedCaches?: UnassociatedCacheOverview;
};
const CachePage = () => {
  const { t } = useTranslation();
  const [cacheOverview, setCacheOverview] = useState<CacheOverview>();

  const reload = async () => {
    const r = await BApi.cache.getCacheOverview();

    setCacheOverview(
      r.data ?? {
        mediaLibraryCaches: [],
      },
    );
  };

  useEffect(() => {
    reload();
  }, []);

  return (
    <div>
      <div className={"flex flex-col gap-2"}>
        {cacheOverview?.mediaLibraryCaches &&
          cacheOverview.mediaLibraryCaches.length > 0 && (
            <div className={"grid grid-cols-4 gap-2"}>
              {cacheOverview.mediaLibraryCaches.map((item, idx) => {
                return (
                  <Card key={idx}>
                    <CardHeader>
                      <div>
                        <div className="text-md">{item.mediaLibraryName}</div>
                        <div className="text-small text-default-500">
                          {t<string>("cache.label.resourceCount")}: {item.resourceCount}
                        </div>
                      </div>
                    </CardHeader>
                    <Divider />
                    <CardBody>
                      {Object.keys(item.resourceCacheCountMap).map(
                        (key, idx) => {
                          const type = parseInt(key, 10) as ResourceCacheType;

                          return (
                            <div
                              key={idx}
                              className={"flex items-center gap-2"}
                            >
                              <Chip radius={"sm"} size={"sm"}>
                                {t<string>("cache.label.cached")}{" "}
                                {t<string>(ResourceCacheType[type])}
                              </Chip>
                              {item.resourceCacheCountMap[type]}
                              <Button
                                isIconOnly
                                color={"danger"}
                                size={"sm"}
                                variant={"light"}
                                onClick={() => {
                                  BApi.cache
                                    .deleteResourceCacheByMediaLibraryIdAndCacheType(
                                      item.mediaLibraryId,
                                      parseInt(key, 10) as ResourceCacheType,
                                    )
                                    .then((r) => {
                                      if (!r.code) {
                                        reload();
                                      }
                                    });
                                }}
                              >
                                <DeleteOutlined className={"text-base"} />
                              </Button>
                            </div>
                          );
                        },
                      )}
                    </CardBody>
                    {/* <Divider /> */}
                    {/* <CardFooter> */}
                    {/*   <Link isExternal showAnchorIcon href="https://github.com/nextui-org/nextui"> */}
                    {/*     Visit source code on GitHub. */}
                    {/*   </Link> */}
                    {/* </CardFooter> */}
                  </Card>
                );
              })}
            </div>
          )}
        {cacheOverview?.unassociatedCaches &&
          cacheOverview.unassociatedCaches.resourceCount > 0 && (
            <div className={"grid grid-cols-4 gap-2"}>
              <Card>
                <CardHeader>
                  <div>
                    <div className="text-md">
                      {t<string>("cache.label.unassociatedResources")}
                    </div>
                    <div className="text-small text-default-500">
                      {t<string>("cache.label.resourceCount")}:{" "}
                      {cacheOverview.unassociatedCaches.resourceCount}
                    </div>
                  </div>
                </CardHeader>
                <Divider />
                <CardBody>
                  {Object.keys(
                    cacheOverview.unassociatedCaches.resourceCacheCountMap,
                  ).map((key, idx) => {
                    const type = parseInt(key, 10) as ResourceCacheType;

                    return (
                      <div key={idx} className={"flex items-center gap-2"}>
                        <Chip radius={"sm"} size={"sm"}>
                          {t<string>("cache.label.cached")}{" "}
                          {t<string>(ResourceCacheType[type])}
                        </Chip>
                        {cacheOverview.unassociatedCaches!.resourceCacheCountMap[type]}
                        <Button
                          isIconOnly
                          color={"danger"}
                          size={"sm"}
                          variant={"light"}
                          onClick={() => {
                            BApi.cache
                              .deleteUnassociatedResourceCacheByCacheType(
                                parseInt(key, 10) as ResourceCacheType,
                              )
                              .then((r) => {
                                if (!r.code) {
                                  reload();
                                }
                              });
                          }}
                        >
                          <DeleteOutlined className={"text-base"} />
                        </Button>
                      </div>
                    );
                  })}
                </CardBody>
              </Card>
            </div>
          )}
      </div>
    </div>
  );
};

CachePage.displayName = "CachePage";

export default CachePage;
