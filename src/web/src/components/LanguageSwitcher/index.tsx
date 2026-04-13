"use client";

import { Button, Dropdown, DropdownTrigger, DropdownMenu, DropdownItem } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { useAppOptionsStore } from "@/stores/options";

const SUPPORTED_LANGUAGES = [
  { code: "zh-CN", label: "简体中文", shortLabel: "中" },
  { code: "en-US", label: "English", shortLabel: "EN" },
];

const LanguageSwitcher = () => {
  const appOptions = useAppOptionsStore((state) => state.data);
  const currentLanguageCode = SUPPORTED_LANGUAGES.find(
    (lang) => lang.code === appOptions.language || lang.code.toLowerCase() === appOptions.language?.toLowerCase()
  )?.code ?? SUPPORTED_LANGUAGES[1].code;

  return (
    <Dropdown>
      <DropdownTrigger>
        <Button isIconOnly color="default" variant="light">
          <span style={{ fontSize: 16, fontWeight: 500 }}>
            {SUPPORTED_LANGUAGES.find((l) => l.code === currentLanguageCode)?.shortLabel ?? "EN"}
          </span>
        </Button>
      </DropdownTrigger>
      <DropdownMenu
        aria-label="Language"
        selectionMode="single"
        selectedKeys={[currentLanguageCode]}
        onSelectionChange={(keys) => {
          const selected = Array.from(keys)[0] as string;
          if (selected && selected !== currentLanguageCode) {
            BApi.options
              .patchAppOptions({ language: selected })
              .then(() => {
                location.reload();
              });
          }
        }}
      >
        {SUPPORTED_LANGUAGES.map((lang) => (
          <DropdownItem key={lang.code}>{lang.label}</DropdownItem>
        ))}
      </DropdownMenu>
    </Dropdown>
  );
};

LanguageSwitcher.displayName = "LanguageSwitcher";

export default LanguageSwitcher;
