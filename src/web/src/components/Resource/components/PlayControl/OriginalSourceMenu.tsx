import type { PlayControlPortalProps } from ".";

import { ExportOutlined } from "@ant-design/icons";
import { useTranslation } from "react-i18next";

import {
  Button,
  Dropdown,
  DropdownItem,
  DropdownMenu,
  DropdownTrigger,
  Tooltip,
} from "@/components/bakaui";

/** Metadata navigation remains usable even before files or opening rules are available. */
const OriginalSourceMenu = ({
  actions,
  className,
}: {
  actions: PlayControlPortalProps["originalSourceActions"];
  className?: string;
}) => {
  const { t } = useTranslation();

  if (!actions?.length) return null;

  return (
    <Dropdown>
      <Tooltip content={t("resource.playbackActions.originalPages")}>
        <DropdownTrigger>
          <Button
            isIconOnly
            aria-label={t("resource.playbackActions.originalPages")}
            className={className}
            size="sm"
            variant="light"
          >
            <ExportOutlined className="text-lg" />
          </Button>
        </DropdownTrigger>
      </Tooltip>
      <DropdownMenu aria-label={t("resource.playbackActions.originalPages")}>
        {actions.map((action) => (
          <DropdownItem key={action.key} onPress={action.onOpen}>
            {action.label}
          </DropdownItem>
        ))}
      </DropdownMenu>
    </Dropdown>
  );
};

export default OriginalSourceMenu;
