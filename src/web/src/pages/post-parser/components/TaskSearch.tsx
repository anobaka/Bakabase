import { useTranslation } from "react-i18next";
import { AiOutlineSearch } from "react-icons/ai";

import { Input } from "@/components/bakaui";

interface Props {
  value: string;
  onChange: (value: string) => void;
  total: number;
  shown: number;
}

const TaskSearch = ({ value, onChange, total, shown }: Props) => {
  const { t } = useTranslation();

  return (
    <div className="flex min-w-0 flex-[1_1_20rem] flex-wrap items-center gap-2 sm:max-w-lg sm:flex-nowrap">
      <Input
        isClearable
        aria-label={t<string>("postParser.search.label")}
        className="min-w-0 flex-1"
        placeholder={t<string>("postParser.search.placeholder")}
        size="sm"
        startContent={<AiOutlineSearch aria-hidden className="shrink-0 text-default-400" />}
        value={value}
        onClear={() => onChange("")}
        onValueChange={onChange}
      />
      <span
        aria-live="polite"
        className="shrink-0 whitespace-nowrap text-xs tabular-nums text-default-500"
      >
        {t<string>(value.trim() ? "postParser.search.filteredCount" : "postParser.search.total", {
          count: total,
          shown,
        })}
      </span>
    </div>
  );
};

export default TaskSearch;
