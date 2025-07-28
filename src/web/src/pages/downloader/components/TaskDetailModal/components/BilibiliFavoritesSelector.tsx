import type { components } from "@/sdk/BApi2";

import { Button, Chip, Radio, RadioGroup, Spinner } from "@heroui/react";
import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import Configurations from "../../Configurations";

import BApi from "@/sdk/BApi";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

type Favorites =
  components["schemas"]["Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models.Favorites"];

type Props = {
  isDisabled?: boolean;
  value?: number;
  onChange?: (value: number) => void;
};

const BilibiliFavoritesSelector = ({ isDisabled, value, onChange }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [loadingFavorites, setLoadingFavorites] = useState(false);
  const [favorites, setFavorites] = useState<Favorites[]>([]);

  useEffect(() => {
    loadFavorites();
  }, []);

  const loadFavorites = async () => {
    setLoadingFavorites(true);
    try {
      const r = await BApi.bilibili.getBiliBiliFavorites();

      if (!r.code) {
        setFavorites(r.data || []);
      }
    } finally {
      setLoadingFavorites(false);
    }
  };

  const renderFavorites = () => {
    if (loadingFavorites) {
      return <Spinner size="sm" />;
    }

    if (favorites.length === 0) {
      return (
        <div className={"flex items-center gap-2"}>
          {t<string>(
            "Unable to retrieve Bilibili favorites. Please ensure your cookie is correctly set and that you have at least one favorite created.",
          )}
          <Button
            color={"primary"}
            size={"sm"}
            variant={"light"}
            onPress={() => {
              createPortal(Configurations, {
                onSubmitted: async () => {
                  await loadFavorites();
                },
              });
            }}
          >
            {t<string>("Setup now")}
          </Button>
        </div>
      );
    }

    return (
      <RadioGroup
        // color="secondary"
        isDisabled={isDisabled}
        label={t<string>("Select favorites")}
        orientation="horizontal"
        size={"sm"}
        value={value?.toString()}
        onValueChange={(value) => {
          onChange?.(parseInt(value, 10));
        }}
      >
        {favorites.map((f) => {
          return (
            <Radio key={f.id} value={f.id.toString()}>
              {f.title}({f.mediaCount})
            </Radio>
          );
        })}
      </RadioGroup>
    );
  };

  return (
    <div className="flex items-center gap-2">
      <Chip radius="sm">{t<string>("Select favorites")}</Chip>
      {renderFavorites()}
    </div>
  );
};

export default BilibiliFavoritesSelector;
