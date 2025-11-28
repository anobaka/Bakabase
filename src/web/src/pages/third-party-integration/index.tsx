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
            <div>{t<string>("Tampermonkey script")}</div>
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
              {t<string>("One-click installation")}
            </Button>
          </div>
          <Alert
            color={'default'}
            variant={'flat'}
            title={t<string>('This script modifies some interactive behaviors of the target website. You can manually enable or disable the script in Tampermonkey.')}
            // hideIcon
            description={(
              <div>
                <div>1. {t<string>('Left-clicking on covers or post titles will trigger the creation of a post parsing task on Bakabase. You can still use the middle button to open posts.')}</div>
              </div>
          )}
          />
        </div>
      </AccordionItem>
      <AccordionItem key="ExHentai" title="ExHentai">
        <div>
          <div className={"flex items-center gap-2"}>
            <div>{t<string>("Tampermonkey script")}</div>
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
              {t<string>("One-click installation")}
            </Button>
          </div>
          <Alert
            color={'default'}
            variant={'flat'}
            title={t<string>('This script modifies some interactive behaviors of the target website. You can manually enable or disable the script in Tampermonkey.')}
            // hideIcon
            description={(
              <div>
                <div>1. {t<string>('Left-clicking on covers will trigger the creation of a torrent download task on Bakabase. You can still use the middle button to open posts.')}</div>
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
