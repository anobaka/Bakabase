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
import DeprecatedChip from "@/components/Chips/DeprecatedChip";

type CategoryCacheOverview = {
  categoryId: number;
  categoryName: string;
  resourceCacheCountMap: Record<number, number>;
  resourceCount: number;
};
type MediaLibraryCacheOverview = {
  mediaLibraryId: number;
  mediaLibraryName: string;
  resourceCacheCountMap: Record<string, number>;
  resourceCount: number;
};

type CacheOverview = {
  categoryCaches: CategoryCacheOverview[];
  mediaLibraryCaches: MediaLibraryCacheOverview[];
};
const CachePage = () => {
  const { t } = useTranslation();
  const [cacheOverview, setCacheOverview] = useState<CacheOverview>();

  const reload = async () => {
    const r = await BApi.cache.getCacheOverview();

    setCacheOverview(
      r.data ?? {
        categoryCaches: [],
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
                          {t<string>("Resource count")}: {item.resourceCount}
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
                                {t<string>("Cached")}{" "}
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
        {cacheOverview?.categoryCaches &&
          cacheOverview.categoryCaches.length > 0 && (
            <div className={"grid grid-cols-4 gap-2"}>
              {cacheOverview.categoryCaches.map((item, idx) => {
                return (
                  <Card key={idx}>
                    <CardHeader>
                      <div>
                        <div className="text-md">
                          {item.categoryName}
                          <DeprecatedChip />
                        </div>
                        <div className="text-small text-default-500">
                          {t<string>("Resource count")}: {item.resourceCount}
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
                                {t<string>("Cached")}{" "}
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
                                    .deleteResourceCacheByCategoryIdAndCacheType(
                                      item.categoryId,
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
      </div>
    </div>
  );
};

CachePage.displayName = "CachePage";

export default CachePage;
