import {
  CheckOutlined,
  ClockCircleOutlined,
  ScanOutlined,
  SettingOutlined,
  ThunderboltOutlined,
} from "@ant-design/icons";
import { Radio, RadioGroup } from "@/components/bakaui";

const icons = {
  manual: SettingOutlined,
  keep: CheckOutlined,
  ai: ThunderboltOutlined,
  defer: ClockCircleOutlined,
  detect: ScanOutlined,
};

export default function ProcessingChoiceGroup<T extends string>({
  label,
  value,
  choices,
  onChange,
  isDisabled,
  isRequired,
}: {
  label: string;
  value?: T;
  choices: ReadonlyArray<{
    value: T;
    label: string;
    icon: keyof typeof icons;
    isDisabled?: boolean;
  }>;
  onChange: (next: T) => void;
  isDisabled?: boolean;
  isRequired?: boolean;
}) {
  return (
    <RadioGroup
      label={label}
      value={value ?? ""}
      size="sm"
      orientation="horizontal"
      isDisabled={isDisabled}
      isRequired={isRequired}
      classNames={{ label: "text-sm text-foreground", wrapper: "flex-wrap gap-x-4 gap-y-2" }}
      onValueChange={(key) => {
        const choice = choices.find((item) => item.value === key);
        if (choice && !choice.isDisabled) onChange(choice.value);
      }}
    >
      {choices.map((choice) => {
        const Icon = icons[choice.icon];
        return (
          <Radio
            key={choice.value}
            value={choice.value}
            isDisabled={choice.isDisabled}
            classNames={{
              base: "m-0 max-w-full p-0.5",
              label: "inline-flex items-center gap-1.5 text-sm",
            }}
          >
            <Icon aria-hidden />
            {choice.label}
          </Radio>
        );
      })}
    </RadioGroup>
  );
}
