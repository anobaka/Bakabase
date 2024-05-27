import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { InfoCircleOutlined, QuestionCircleOutlined, WarningOutlined } from '@ant-design/icons';
import { getResultFromExecAll } from '../../helpers';
import { Button, Card, CardBody, CardHeader, Chip, Input, Radio, RadioGroup, Tooltip } from '@/components/bakaui';
import { splitPathIntoSegments } from '@/components/utils';

type Props = {
  modeIsSelected: boolean;
  onSelectMode: () => void;
  onRegexChange: (regex: string) => void;
  regex?: string;
  textToBeMatched?: string;
  isResourceProperty: boolean;
};

export default ({
                  onRegexChange,
                  modeIsSelected,
                  onSelectMode,
                  regex,
                  textToBeMatched,
                  isResourceProperty,
                }: Props) => {
  const { t } = useTranslation();

  const [testResult, setTestResult] = useState<{
    success: boolean;
    error?: string;
    groups?: string[];
    text?: string;
    tip?: string;
    index?: number;
  }>();
  const [value, setValue] = useState(regex);

  if (!textToBeMatched) {
    return null;
  }

  const renderTestResult = () => {
    if (testResult) {
      if (!testResult.success) {
        return (
          <div className={'mt-1'}>
            {testResult.error && (
              <>
                <Chip
                  size={'sm'}
                  variant={'light'}
                  color={'danger'}
                  startContent={(
                    <WarningOutlined className={'text-sm'} />
                  )}
                >
                  {testResult.error}
                </Chip>
                <br />
              </>
            )}
            <Chip
              size={'sm'}
              variant={'light'}
              color={'danger'}
            >
              {t('Test failed, please check your regex')}
            </Chip>
          </div>

        );
      } else {
        let tip: string | undefined;
        if (testResult.groups && testResult.groups.length > 0) {
          tip = t('Capturing groups are used, only matched text will be applied');
        } else {
          if (testResult.text != undefined && testResult.text.length > 0) {
            tip = t('Whole segment will be applied if capturing group is not used');
          }
        }

        let values: string[] | undefined;

        if (testResult.groups != undefined) {
          values = testResult.groups;
        } else {
          const sub = textToBeMatched.substring(0, testResult.index! + testResult.text!.length);
          const segments = splitPathIntoSegments(sub);
          const match = splitPathIntoSegments(textToBeMatched)[segments.length - 1];
          values = [match];
        }

        return (
          <div className="mt-1">
            {tip && (
              <Chip
                startContent={(
                  <InfoCircleOutlined
                    className={'text-sm'}
                  />
                )}
                size={'sm'}
                variant={'light'}
                color={'warning'}
              >

                {tip}
              </Chip>
            )}
            {values && values.length > 0 && (
              <div className="flex flex-wrap gap-1 items-center">
                <div className="font-bold">
                  {t('Match result')}
                </div>
                {values.map(v => (
                  <Chip
                    size={'sm'}
                    radius={'sm'}
                  >
                    {v}
                  </Chip>
                ))}
              </div>
            )}
          </div>
        );
      }
    }
    return;
  };

  return (
    <Card
      className="mb-2 cursor-pointer w-full"
      isHoverable
    >
      <CardHeader className="text-lg font-bold">
        <RadioGroup
          value={modeIsSelected ? 'regex' : ''}
          onValueChange={onSelectMode}
        >
          <Radio value={'regex'}>
            {t('Set by {{thing}}', { thing: t('regex') })}
          </Radio>
        </RadioGroup>
        <Tooltip
          content={(
            <div>
              {t('/ is the directory separator always, not \\')}
              <br />
              {t('The whole matched text will be ignored if capturing groups are used')}
              <br />
              {t('You should not use capturing groups on Resource property due to partial path is not available to match a file system entry')}
            </div>
          )}
        >
          <QuestionCircleOutlined
            className={'text-base ml-2'}
          />
        </Tooltip>
      </CardHeader>
      <CardBody>
        <div>
          <div className="flex items-center gap-2 mb-2">
            <span>{t('Text to be matched')}</span>
            <Chip
              size={'sm'}
              variant={'light'}
              color={'success'}
            >
              {textToBeMatched}
            </Chip>
          </div>
          <div className={'flex items-center gap-1'}>
            <Input
              isClearable
              onClear={() => {
                setValue(undefined);
              }}
              value={value}
              style={{ width: '100%' }}
              aria-label="please input"
              onValueChange={v => {
                setValue(v);
              }}
              onClick={() => {
                if (!modeIsSelected) {
                  onSelectMode();
                }
              }}
            />
            <Button
              disabled={value == undefined}
              onClick={() => {
                if (value) {
                  try {
                    const v = getResultFromExecAll(new RegExp(value), textToBeMatched);
                    console.log(v, isResourceProperty);
                    if (v && v.groups && v.groups.length > 0 && isResourceProperty) {
                      throw new Error(t('Capturing groups are not allowed on Resource property'));
                    }
                    if (v) {
                      setTestResult({
                        success: true,
                        ...v,
                      });
                      onRegexChange(value);
                    } else {
                      setTestResult({
                        success: false,
                      });
                    }
                  } catch (e) {
                    setTestResult({
                      success: false,
                      error: e.message,
                    });
                  }
                }
              }}
            >
              {t('Test')}
            </Button>
          </div>
          {renderTestResult()}
        </div>
      </CardBody>
    </Card>
  );
};
