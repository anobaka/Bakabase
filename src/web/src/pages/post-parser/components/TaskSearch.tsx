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
    <div className="flex flex-wrap items-center gap-3">
      <Input
        isClearable
        aria-label={t<string>("postParser.search.label")}
        className="min-w-0 flex-[1_1_280px] sm:max-w-md"
        placeholder={t<string>("postParser.search.placeholder")}
        size="sm"
        startContent={<AiOutlineSearch aria-hidden className="shrink-0 text-default-400" />}
        value={value}
        onClear={() => onChange("")}
        onValueChange={onChange}
      />
      <span aria-live="polite" className="text-xs tabular-nums text-default-500">
        {t<string>(value.trim() ? "postParser.search.filteredCount" : "postParser.search.total", {
          count: total,
          shown,
        })}
      </span>
    </div>
  );
};

export default TaskSearch;
