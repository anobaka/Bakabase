"use client";

import { useTranslation } from "react-i18next";
import { GrInstallOption } from "react-icons/gr";

import { Accordion, AccordionItem, Alert, Button } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { TampermonkeyScript } from "@/sdk/constants";
const ThirdPartyIntegrationPage = () => {
  const { t } = useTranslation();

  return (
    <Accordion defaultSelectedKeys={"all"} selectionMode={"multiple"} variant="splitted">
      <AccordionItem key="SoulPlus" title="SoulPlus">
        <div>
          <div className={"flex items-center gap-2"}>
            <div>{t<string>("thirdPartyIntegration.label.tampermonkeyScript")}</div>
            <Button
              color={"primary"}
              variant={"light"}
              onPress={() => {
                BApi.tampermonkey.installTampermonkeyScript({
                  script: TampermonkeyScript.SoulPlus,
                });
              }}
            >
              <GrInstallOption className={"text-base"} />
              {t<string>("thirdPartyIntegration.action.oneClickInstall")}
            </Button>
          </div>
          <Alert
            color={'default'}
            variant={'flat'}
            title={t<string>("thirdPartyIntegration.tip.scriptModifies")}
            // hideIcon
            description={(
              <div>
                <div>1. {t<string>("thirdPartyIntegration.tip.soulPlusClick")}</div>
              </div>
          )}
          />
        </div>
      </AccordionItem>
      <AccordionItem key="ExHentai" title="ExHentai">
        <div>
          <div className={"flex items-center gap-2"}>
            <div>{t<string>("thirdPartyIntegration.label.tampermonkeyScript")}</div>
            <Button
              color={"primary"}
              variant={"light"}
              onPress={() => {
                BApi.tampermonkey.installTampermonkeyScript({
                  script: TampermonkeyScript.Bakabase,
                });
              }}
            >
              <GrInstallOption className={"text-base"} />
              {t<string>("thirdPartyIntegration.action.oneClickInstall")}
            </Button>
          </div>
          <Alert
            color={'default'}
            variant={'flat'}
            title={t<string>("thirdPartyIntegration.tip.scriptModifies")}
            // hideIcon
            description={(
              <div>
                <div>1. {t<string>("thirdPartyIntegration.tip.exHentaiClick")}</div>
              </div>
            )}
          />
        </div>
      </AccordionItem>
    </Accordion>
  );
};

ThirdPartyIntegrationPage.displayName = "ThirdPartyIntegrationPage";

export default ThirdPartyIntegrationPage;
