"use client";

import React, { useCallback, useEffect, useState } from "react";
import { Checkbox, Modal } from "@/components/bakaui";
import { useTranslation } from "react-i18next";

import { createPortalOfComponent } from "@/components/utils";
import BApi from "@/sdk/BApi";

interface Props {
  resourceIds: number[];
}

const FavoritesSelector = React.memo(({ resourceIds = [] }: Props) => {
  const [favoritesResourcesMappings, setFavoritesResourcesMappings] = useState<{
    [favId: number]: number[];
  }>({});
  const [favorites, setFavorites] = useState<{ id: number; name: string }[]>(
    [],
  );
  const { t } = useTranslation();
  const [visible, setVisible] = useState(true);

  useEffect(() => {
    BApi.favorites.getAllFavorites().then((t) => {
      // @ts-ignore
      setFavorites(t.data || []);
    });

    BApi.resource
      .getFavoritesResourcesMappings({ ids: resourceIds })
      .then((a) => {
        // @ts-ignore
        setFavoritesResourcesMappings(a.data || {});
      });
  }, []);

  useEffect(() => {
    console.log("new favoritesResourcesMappings", favoritesResourcesMappings);
  }, [favoritesResourcesMappings]);

  // console.log(favoritesResourcesMappings);

  const close = useCallback(() => {
    setVisible(false);
  }, []);

  return (
    <Modal
      className={"resource-page-favorites-selector"}
      closeMode={["esc", "mask", "close"]}
      style={{ minWidth: 600 }}
      title={t<string>("Select favorites")}
      visible={visible}
      onCancel={close}
      onClose={close}
      onOk={() => {
        const resourcesFavoritesMapping = resourceIds.reduce((s, t) => {
          s[t] = [];

          return s;
        }, {});

        Object.keys(favoritesResourcesMappings).forEach((favId) => {
          const resourceIds = favoritesResourcesMappings[favId] || [];

          resourceIds.forEach((rId) => {
            resourcesFavoritesMapping[rId].push(favId);
          });
        }, {});
        BApi.favorites
          .putResourcesFavorites(resourcesFavoritesMapping)
          .then((t) => {
            if (!t.code) {
              close();
            }
          });
      }}
    >
      {favorites.map((f) => {
        const allResourceIds = favoritesResourcesMappings[f.id] || [];
        const intersection = allResourceIds.filter(
          (id) => resourceIds.indexOf(id) > -1,
        );

        return (
          <Checkbox
            key={f.id}
            checked={intersection.length == resourceIds.length}
            indeterminate={
              intersection.length > 0 &&
              intersection.length < resourceIds.length
            }
            value={f.id}
            onChange={(checked) => {
              if (checked) {
                favoritesResourcesMappings[f.id] = resourceIds.slice();
              } else {
                favoritesResourcesMappings[f.id] = [];
              }
              setFavoritesResourcesMappings({ ...favoritesResourcesMappings });
            }}
          >
            {f.name}
          </Checkbox>
        );
      })}
    </Modal>
  );
});

const Wrapped = Object.assign({}, FavoritesSelector, {
  show: (props: Props) => createPortalOfComponent(FavoritesSelector, props),
});

export default Wrapped;
