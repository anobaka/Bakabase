import React, { useCallback } from 'react';
import dayjs from 'dayjs';
import { Message, Range } from '@alifd/next';
import { useTranslation } from 'react-i18next';

const MarkCount = 5;

export default ({
                  duration,
                  onChange: propsOnChange = (values) => {
                  },
                  onProcess: propsOnProcess = (values) => {
                  },
                }: { onProcess?: (values: number[]) => void; onChange?: (values: number[]) => void; duration: any }) => {
  const { t } = useTranslation();
  const marks = {};
  const totalSeconds = duration.asSeconds();

  const secondsPerMark = Math.floor(totalSeconds / MarkCount);
  const restSeconds = totalSeconds - secondsPerMark * MarkCount;

  let idx = 0;
  while (idx <= MarkCount) {
    if (idx == MarkCount) {
      if (restSeconds / secondsPerMark < 0.4) {
        break;
      }
    }
    const ts = secondsPerMark * idx;
    marks[ts] = dayjs.duration(ts * 1000)
      .format('HH:mm:ss');
    idx++;
  }

  marks[totalSeconds] = duration.format('HH:mm:ss');

  // console.log(marks, totalSeconds);

  const onChange = useCallback((value: number | [number, number]) => {
    if (value instanceof Array) {
      propsOnChange(value);
    } else {
      Message.error(t('Not supported'));
    }
  }, [propsOnChange]);

  const onProcess = useCallback((value: number | [number, number]) => {
    if (value instanceof Array) {
      propsOnProcess(value);
    } else {
      Message.error(t('Not supported'));
    }
  }, [propsOnProcess]);

  return (
    <Range
      tooltipVisible
      slider="double"
      // @ts-ignore
      tipRender={(v) => dayjs.duration(v * 1000)
        .format('HH:mm:ss')}
      defaultValue={[0, totalSeconds]}
      onChange={onChange}
      onProcess={onProcess}
      min={0}
      marks={marks}
      max={totalSeconds}
    />
  );
};
