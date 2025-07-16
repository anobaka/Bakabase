"use client";

import  { useCallback, useState, useEffect } from "react";
import dayjs from "dayjs";
import { useTranslation } from "react-i18next";

import { TimeInput, toast } from "@/components/bakaui";
import { convertDurationToTime } from "@/components/utils";

const TimeRanger = ({
  duration,
  onChange: propsOnChange = (_values) => {},
  onProcess: propsOnProcess = (_values) => {},
}: {
  onProcess?: (values: number[]) => void;
  onChange?: (values: number[]) => void;
  duration: any;
}) => {
  const { t } = useTranslation();

  // 使用两个时间输入，一个用于开始时间，一个用于结束时间
  const [startTime, setStartTime] = useState(dayjs.duration(0));
  const [endTime, setEndTime] = useState(duration);

  // 当duration改变时，更新结束时间
  useEffect(() => {
    setEndTime(duration);
  }, [duration]);

  const handleStartTimeChange = useCallback(
    (value: any) => {
      setStartTime(value);
      const startSeconds = value.asSeconds();
      const endSeconds = endTime.asSeconds();

      if (startSeconds <= endSeconds) {
        propsOnChange([startSeconds, endSeconds]);
        propsOnProcess([startSeconds, endSeconds]);
      } else {
        toast.danger(t<string>("Start time cannot be greater than end time"));
      }
    },
    [endTime, propsOnChange, propsOnProcess, t],
  );

  const handleEndTimeChange = useCallback(
    (value: any) => {
      setEndTime(value);
      const startSeconds = startTime.asSeconds();
      const endSeconds = value.asSeconds();

      if (startSeconds <= endSeconds) {
        propsOnChange([startSeconds, endSeconds]);
        propsOnProcess([startSeconds, endSeconds]);
      } else {
        toast.danger(t<string>("End time cannot be less than start time"));
      }
    },
    [startTime, propsOnChange, propsOnProcess, t],
  );

  return (
    <div className="flex items-center gap-4">
      <div className="flex-1">
        <TimeInput
          granularity="second"
          label={t<string>("Start Time")}
          maxValue={convertDurationToTime(endTime)}
          value={startTime}
          onChange={handleStartTimeChange}
        />
      </div>
      <div className="flex-1">
        <TimeInput
          granularity="second"
          label={t<string>("End Time")}
          minValue={convertDurationToTime(startTime)}
          value={endTime}
          onChange={handleEndTimeChange}
        />
      </div>
    </div>
  );
};

TimeRanger.displayName = "TimeRanger";

export default TimeRanger;
