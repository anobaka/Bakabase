import type { CoverOperationsOptions, CoverOperationsSelection } from "./coverOperations";

import {
  DeleteOutlined,
  ExportOutlined,
  FireOutlined,
  FolderOpenOutlined,
  PushpinOutlined,
  ReloadOutlined,
  VideoCameraAddOutlined,
} from "@ant-design/icons";
import { AiOutlinePicture } from "react-icons/ai";
import { useTranslation } from "react-i18next";

import { resolveCoverOperations, updateCoverShortcut } from "./coverOperations";

import { Checkbox, Switch } from "@/components/bakaui";

type Props = {
  options?: CoverOperationsOptions;
  onChange: (options: CoverOperationsSelection) => unknown;
};

export default function CoverOperationsSettings({ options, onChange }: Props) {
  const { t } = useTranslation();
  const { showAggregate, directOperations } = resolveCoverOperations(options);
  const shortcuts = [
    { key: "pin", icon: <PushpinOutlined />, label: "resource.display.pinUnpin" },
    { key: "openFolder", icon: <FolderOpenOutlined />, label: "resource.display.openFolder" },
    { key: "enhancements", icon: <FireOutlined />, label: "resource.display.enhancements" },
    { key: "preview", icon: <AiOutlinePicture />, label: "resource.display.preview" },
    {
      key: "addToPlaylist",
      icon: <VideoCameraAddOutlined />,
      label: "resource.display.addToPlaylist",
    },
    { key: "move", icon: <ExportOutlined />, label: "resource.operation.move" },
    { key: "delete", icon: <DeleteOutlined />, label: "common.action.delete" },
    { key: "refreshCache", icon: <ReloadOutlined />, label: "resource.action.refreshCache" },
  ];

  return (
    <section className="flex flex-col gap-2">
      <div className="text-sm font-medium">{t("resource.display.operations")}</div>
      <Switch
        isSelected={showAggregate}
        size="sm"
        onValueChange={(checked) =>
          onChange({
            showAggregateOperations: checked,
            displayOperations: directOperations,
          })
        }
      >
        {t("resource.display.aggregateButton")}
      </Switch>
      <p className="text-xs text-default-500">{t("resource.display.aggregateButtonHint")}</p>
      <div className="mt-1 text-sm">{t("resource.display.directOperations")}</div>
      <p className="text-xs text-default-500">{t("resource.display.directOperationsHint")}</p>
      <div className="flex flex-wrap gap-2">
        {shortcuts.map(({ key, icon, label }) => (
          <Checkbox
            key={key}
            isSelected={directOperations.includes(key)}
            size="sm"
            onValueChange={(checked) => onChange(updateCoverShortcut(options, key, checked))}
          >
            <span className={`flex items-center gap-1 ${key === "delete" ? "text-danger" : ""}`}>
              <span aria-hidden className="text-base">
                {icon}
              </span>
              {t(label)}
            </span>
          </Checkbox>
        ))}
      </div>
    </section>
  );
}
