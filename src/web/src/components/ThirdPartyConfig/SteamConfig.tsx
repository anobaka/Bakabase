"use client";

import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Input, Button } from "@heroui/react";

import { toast } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { useThirdPartyOptionsStore } from "@/stores/options";

export default function SteamConfig() {
  const { t } = useTranslation();
  const thirdPartyOptions = useThirdPartyOptionsStore((state) => state.data);
  const [apiKey, setApiKey] = useState("");
  const [steamId, setSteamId] = useState("");

  useEffect(() => {
    setApiKey((thirdPartyOptions as any)?.steamApiKey || "");
    setSteamId((thirdPartyOptions as any)?.steamId || "");
  }, [thirdPartyOptions]);

  const handleSave = async () => {
    const rsp = await BApi.options.patchThirdPartyOptions({
      steamApiKey: apiKey,
      steamId: steamId,
    } as any);

    if (!rsp.code) {
      toast.success(t<string>("thirdPartyConfig.success.saved"));
    }
  };

  return (
    <div className="space-y-4">
      <div>
        <Input
          label={t<string>("resourceSource.steam.config.apiKey")}
          placeholder="XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX"
          size="sm"
          value={apiKey}
          onValueChange={setApiKey}
        />
      </div>
      <div>
        <Input
          label={t<string>("resourceSource.steam.config.steamId")}
          placeholder="76561198XXXXXXXXX"
          size="sm"
          value={steamId}
          onValueChange={setSteamId}
        />
      </div>
      <div className="operations">
        <Button color="primary" size="sm" onPress={handleSave}>
          {t<string>("thirdPartyConfig.action.save")}
        </Button>
      </div>
    </div>
  );
}
